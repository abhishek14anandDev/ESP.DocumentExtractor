"""Azure Functions (Python v2) app: convert DWG/DXF files to GeoJSON.

Endpoint:
    POST /api/cad/geojson
    GET  /api/cad/geojson/{conversion_id}

Two request styles are supported:
  1. multipart/form-data with a file field named ``file`` (the DWG/DXF upload).
  2. application/json body ``{"filePath": "/abs/path/to/file.dwg"}`` for files
     already accessible to the host (useful for local development).

The response body is the GeoJSON FeatureCollection (``application/geo+json``).
Conversion diagnostics are returned in the ``x-conversion-*`` response headers.
"""

from __future__ import annotations

import hashlib
import json
import logging
import os
import uuid
from dataclasses import dataclass
from datetime import datetime, timezone
from typing import Any

import azure.functions as func

from dwg_geojson import (
    ConversionError,
    convert_bytes_to_geojson,
    convert_file_to_geojson,
)
from dwg_geojson.cosmos_repository import CosmosGeoJsonRepository
from dwg_geojson.repository import PersistenceError, StoredGeoJsonNotFoundError
from dwg_geojson.retrieval_service import GeoJsonRetrievalService
from dwg_geojson.storage_models import DocumentAnalysis, SourceInfo, validate_annotation
from dwg_geojson.storage_service import GeoJsonStorageService

app = func.FunctionApp(http_auth_level=func.AuthLevel.ANONYMOUS)
logger = logging.getLogger("dwg_geojson.function")
DEFAULT_GEOJSON_LOG_MAX_CHARS = 20_000
_geojson_repository: CosmosGeoJsonRepository | None = None
_storage_service: GeoJsonStorageService | None = None
_retrieval_service: GeoJsonRetrievalService | None = None


@dataclass(frozen=True)
class ConvertedRequest:
    geojson: dict[str, Any]
    stats: Any
    source_info: SourceInfo
    request_options: dict[str, Any]


@app.route(route="cad/geojson", methods=["POST"])
def cad_geojson(req: func.HttpRequest) -> func.HttpResponse:
    correlation_id = req.headers.get("x-correlation-id") or uuid.uuid4().hex
    conversion_id = uuid.uuid4().hex
    logger.info(
        "[%s] CAD GeoJSON request received: method=POST contentType=%s conversionId=%s",
        correlation_id,
        req.headers.get("content-type", ""),
        conversion_id,
    )

    try:
        converted = _convert_request(req)
    except ConversionError as exc:
        logger.warning("[%s] Conversion failed: %s", correlation_id, exc)
        return _error(correlation_id, "cad.conversion_failed", str(exc), 400)
    except ValueError as exc:
        logger.warning("[%s] Bad request: %s", correlation_id, exc)
        return _error(correlation_id, "request.invalid", str(exc), 400)
    except Exception as exc:  # noqa: BLE001 - surface unexpected errors as 500
        logger.exception("[%s] Unexpected error", correlation_id)
        return _error(correlation_id, "cad.internal_error", str(exc), 500)

    logger.info(
        "[%s] CAD conversion completed: conversionId=%s sourceType=%s fileName=%s "
        "converter=%s features=%s filteredOut=%s reprojected=%s sourceEpsg=%s",
        correlation_id,
        conversion_id,
        converted.source_info.source_type,
        converted.source_info.file_name,
        converted.stats.converter or "unknown",
        converted.stats.feature_count,
        converted.stats.filtered_out,
        converted.stats.reprojected,
        converted.stats.source_epsg,
    )
    _log_geojson_payload(correlation_id, conversion_id, "converted", converted.geojson)

    try:
        logger.info("[%s] Cosmos persistence starting: conversionId=%s", correlation_id, conversion_id)
        stored = _get_storage_service().store(
            conversion_id=conversion_id,
            correlation_id=correlation_id,
            geojson=converted.geojson,
            stats=converted.stats,
            source=converted.source_info,
            request_options=converted.request_options,
        )
    except PersistenceError as exc:
        logger.exception("[%s] Cosmos persistence failed", correlation_id)
        return _error(correlation_id, "cad.persistence_failed", str(exc), 500)

    logger.info(
        "[%s] Cosmos persistence completed: conversionId=%s container=%s chunks=%s features=%s",
        correlation_id,
        stored.conversion_id,
        stored.container_name,
        stored.chunk_count,
        stored.feature_count,
    )

    headers = {
        "x-correlation-id": correlation_id,
        "x-conversion-converter": converted.stats.converter or "unknown",
        "x-conversion-feature-count": str(converted.stats.feature_count),
        "x-conversion-reprojected": str(converted.stats.reprojected).lower(),
        "x-conversion-source-epsg": str(converted.stats.source_epsg or ""),
        "x-conversion-filtered-out": str(converted.stats.filtered_out),
        "x-cosmos-conversion-id": stored.conversion_id,
        "x-cosmos-container": stored.container_name,
        "x-cosmos-chunk-count": str(stored.chunk_count),
        "access-control-expose-headers": "x-correlation-id,x-conversion-converter,"
        "x-conversion-feature-count,x-conversion-reprojected,x-conversion-source-epsg,"
        "x-conversion-filtered-out,x-cosmos-conversion-id,x-cosmos-container,"
        "x-cosmos-chunk-count",
        "access-control-allow-origin": "*",
    }
    return func.HttpResponse(
        body=json.dumps(converted.geojson),
        status_code=200,
        mimetype="application/geo+json",
        headers=headers,
    )


@app.route(route="cad/geojson", methods=["GET"])
def list_cad_geojson(req: func.HttpRequest) -> func.HttpResponse:
    correlation_id = req.headers.get("x-correlation-id") or uuid.uuid4().hex
    logger.info("[%s] CAD GeoJSON metadata list requested", correlation_id)

    try:
        limit = _int_param(req, "limit", 100)
        logger.info("[%s] Cosmos metadata list starting: limit=%s", correlation_id, limit)
        items = _get_retrieval_service().list(limit)
    except ValueError as exc:
        logger.warning("[%s] Bad metadata list request: %s", correlation_id, exc)
        return _error(correlation_id, "request.invalid", str(exc), 400)
    except PersistenceError as exc:
        logger.exception("[%s] Cosmos metadata list failed", correlation_id)
        return _error(correlation_id, "cad.persistence_failed", str(exc), 500)

    logger.info("[%s] Cosmos metadata list completed: returned=%s", correlation_id, len(items))
    logger.info(
        "[%s] CAD GeoJSON payload not logged for metadata list endpoint: "
        "returned=%s reason=list endpoint returns metadata only; "
        "use POST /api/cad/geojson or GET /api/cad/geojson/{conversion_id}",
        correlation_id,
        len(items),
    )
    return func.HttpResponse(
        body=json.dumps(items),
        status_code=200,
        mimetype="application/json",
        headers=_cors_headers(correlation_id),
    )


@app.route(route="cad/geojson/{conversion_id}", methods=["GET"])
def get_cad_geojson(req: func.HttpRequest) -> func.HttpResponse:
    correlation_id = req.headers.get("x-correlation-id") or uuid.uuid4().hex
    conversion_id = (req.route_params.get("conversion_id") or "").strip()
    logger.info("[%s] CAD GeoJSON retrieval requested: %s", correlation_id, conversion_id)

    try:
        logger.info("[%s] Cosmos retrieval starting: conversionId=%s", correlation_id, conversion_id)
        stored = _get_retrieval_service().get(conversion_id)
    except ValueError as exc:
        logger.warning("[%s] Bad retrieval request: %s", correlation_id, exc)
        return _error(correlation_id, "request.invalid", str(exc), 400)
    except StoredGeoJsonNotFoundError as exc:
        logger.warning("[%s] Stored GeoJSON not found: %s", correlation_id, exc)
        return _error(correlation_id, "cad.not_found", str(exc), 404)
    except PersistenceError as exc:
        logger.exception("[%s] Cosmos retrieval failed", correlation_id)
        return _error(correlation_id, "cad.persistence_failed", str(exc), 500)

    metadata = stored["metadata"]
    logger.info(
        "[%s] Cosmos retrieval completed: conversionId=%s chunks=%s features=%s format=%s",
        correlation_id,
        stored["conversionId"],
        metadata.get("chunkCount", ""),
        metadata.get("featureCount", ""),
        (req.params.get("format") or "envelope").strip().lower(),
    )
    _log_geojson_payload(correlation_id, stored["conversionId"], "retrieved", stored["geojson"])
    headers = {
        "x-correlation-id": correlation_id,
        "x-cosmos-conversion-id": stored["conversionId"],
        "x-cosmos-container": metadata.get("containerName", ""),
        "x-cosmos-chunk-count": str(metadata.get("chunkCount", "")),
        "x-conversion-feature-count": str(metadata.get("featureCount", "")),
        "access-control-expose-headers": "x-correlation-id,x-cosmos-conversion-id,"
        "x-cosmos-container,x-cosmos-chunk-count,x-conversion-feature-count",
        "access-control-allow-origin": "*",
    }

    if (req.params.get("format") or "").strip().lower() == "geojson":
        return func.HttpResponse(
            body=json.dumps(stored["geojson"]),
            status_code=200,
            mimetype="application/geo+json",
            headers=headers,
        )

    return func.HttpResponse(
        body=json.dumps(stored),
        status_code=200,
        mimetype="application/json",
        headers=headers,
    )


@app.route(route="cad/geojson/{conversion_id}/analysis", methods=["GET"])
def get_document_analysis(req: func.HttpRequest) -> func.HttpResponse:
    correlation_id = req.headers.get("x-correlation-id") or uuid.uuid4().hex
    conversion_id = (req.route_params.get("conversion_id") or "").strip()
    try:
        _require_conversion(conversion_id)
        analysis = _get_repository().get_analysis(conversion_id)
    except ValueError as exc:
        return _error(correlation_id, "request.invalid", str(exc), 400)
    except StoredGeoJsonNotFoundError as exc:
        return _error(correlation_id, "cad.not_found", str(exc), 404)
    except PersistenceError as exc:
        logger.exception("[%s] Analysis retrieval failed", correlation_id)
        return _error(correlation_id, "cad.persistence_failed", str(exc), 500)

    return func.HttpResponse(
        body=json.dumps(analysis),
        status_code=200,
        mimetype="application/json",
        headers=_cors_headers(correlation_id),
    )


@app.route(route="cad/geojson/{conversion_id}/analysis", methods=["PUT"])
def put_document_analysis(req: func.HttpRequest) -> func.HttpResponse:
    correlation_id = req.headers.get("x-correlation-id") or uuid.uuid4().hex
    conversion_id = (req.route_params.get("conversion_id") or "").strip()
    try:
        _require_conversion(conversion_id)
        try:
            payload = req.get_json()
        except ValueError as exc:
            raise ValueError("Request body must be a JSON document analysis.") from exc
        existing = _get_repository().get_analysis(conversion_id)
        analysis = _analysis_from_payload(conversion_id, payload, existing)
        _get_repository().upsert_analysis(analysis)
    except ValueError as exc:
        return _error(correlation_id, "request.invalid", str(exc), 400)
    except StoredGeoJsonNotFoundError as exc:
        return _error(correlation_id, "cad.not_found", str(exc), 404)
    except PersistenceError as exc:
        logger.exception("[%s] Analysis persistence failed", correlation_id)
        return _error(correlation_id, "cad.persistence_failed", str(exc), 500)

    return func.HttpResponse(
        body=json.dumps(analysis.to_item()),
        status_code=200,
        mimetype="application/json",
        headers=_cors_headers(correlation_id),
    )


@app.route(route="cad/geojson/{conversion_id}/analysis", methods=["OPTIONS"])
def document_analysis_options(req: func.HttpRequest) -> func.HttpResponse:
    correlation_id = req.headers.get("x-correlation-id") or uuid.uuid4().hex
    return func.HttpResponse(status_code=204, headers=_cors_headers(correlation_id))


def _get_repository() -> CosmosGeoJsonRepository:
    global _geojson_repository
    if _geojson_repository is None:
        logger.info(
            "Creating Cosmos repository: connectionStringConfigured=%s database=%s container=%s",
            bool(os.environ.get("COSMOS_CONNECTION_STRING")),
            os.environ.get("COSMOS_DATABASE_NAME", "esp-document-extractor"),
            os.environ.get("COSMOS_CONTAINER_NAME", "cad-geojson"),
        )
        _geojson_repository = CosmosGeoJsonRepository.from_env()
    return _geojson_repository


def _get_storage_service() -> GeoJsonStorageService:
    global _storage_service
    if _storage_service is None:
        logger.info("Creating GeoJsonStorageService")
        _storage_service = GeoJsonStorageService(_get_repository())
    return _storage_service


def _get_retrieval_service() -> GeoJsonRetrievalService:
    global _retrieval_service
    if _retrieval_service is None:
        logger.info("Creating GeoJsonRetrievalService")
        _retrieval_service = GeoJsonRetrievalService(_get_repository())
    return _retrieval_service


def _require_conversion(conversion_id: str) -> None:
    if not conversion_id:
        raise ValueError("conversion_id is required.")
    if _get_repository().get_metadata(conversion_id) is None:
        raise StoredGeoJsonNotFoundError(f"GeoJSON conversion '{conversion_id}' was not found.")


def _analysis_from_payload(
    conversion_id: str,
    payload: Any,
    existing: dict[str, Any] | None,
) -> DocumentAnalysis:
    if not isinstance(payload, dict):
        raise ValueError("Document analysis must be a JSON object.")

    now = datetime.now(timezone.utc).isoformat()
    drawing = payload.get("drawing", {})
    if not isinstance(drawing, dict):
        raise ValueError("'drawing' must be an object.")
    cable_lengths = _object_list(payload.get("cableLengths", []), "cableLengths")
    annotations = []
    for annotation in _object_list(payload.get("annotations", []), "annotations"):
        normalized = validate_annotation(annotation)
        normalized["id"] = normalized["id"] or uuid.uuid4().hex
        normalized["createdUtc"] = normalized["createdUtc"] or now
        normalized["updatedUtc"] = now
        annotations.append(normalized)

    return DocumentAnalysis(
        conversion_id=conversion_id,
        created_utc=(existing or {}).get("createdUtc") or now,
        updated_utc=now,
        drawing={str(key): value for key, value in drawing.items()},
        route_summary=_limited_text(payload.get("routeSummary"), "routeSummary", 10_000),
        cable_lengths=cable_lengths,
        layouts=_text_list(payload.get("layouts", []), "layouts"),
        cad_blocks=_text_list(payload.get("cadBlocks", []), "cadBlocks"),
        caveats=_text_list(payload.get("caveats", []), "caveats"),
        annotations=annotations,
    )


def _object_list(value: Any, name: str) -> list[dict[str, Any]]:
    if not isinstance(value, list) or not all(isinstance(item, dict) for item in value):
        raise ValueError(f"'{name}' must be an array of objects.")
    return value


def _text_list(value: Any, name: str) -> list[str]:
    if not isinstance(value, list):
        raise ValueError(f"'{name}' must be an array.")
    return [_limited_text(item, name, 1_000) for item in value if str(item).strip()]


def _limited_text(value: Any, name: str, maximum: int) -> str:
    text = str(value or "").strip()
    if len(text) > maximum:
        raise ValueError(f"'{name}' cannot exceed {maximum} characters.")
    return text


def _log_geojson_payload(
    correlation_id: str,
    conversion_id: str,
    stage: str,
    geojson: dict[str, Any],
) -> None:
    payload = json.dumps(geojson, separators=(",", ":"), ensure_ascii=False)
    max_chars = _int_env("GEOJSON_LOG_MAX_CHARS", DEFAULT_GEOJSON_LOG_MAX_CHARS)
    if max_chars == 0:
        logged_payload = payload
    else:
        logged_payload = payload[:max_chars]
        if len(payload) > max_chars:
            logged_payload += f"...[truncated {len(payload) - max_chars} chars]"

    logger.info(
        "[%s] CAD GeoJSON payload %s: conversionId=%s chars=%s loggedChars=%s "
        "truncated=%s geojson=%s",
        correlation_id,
        stage,
        conversion_id,
        len(payload),
        len(logged_payload),
        max_chars != 0 and len(payload) > max_chars,
        logged_payload,
    )


def _int_env(name: str, default: int) -> int:
    value = os.environ.get(name)
    if value is None:
        return default
    try:
        return max(0, int(value))
    except ValueError:
        logger.warning("Invalid integer environment setting %s=%r; using %s", name, value, default)
        return default


def _bool_param(req: func.HttpRequest, name: str, default: bool) -> bool:
    value = req.params.get(name)
    if value is None:
        return default
    return value.strip().lower() in ("1", "true", "yes", "on")


def _int_param(req: func.HttpRequest, name: str, default: int) -> int:
    value = req.params.get(name)
    if value is None:
        return default
    try:
        parsed = int(value)
    except ValueError as exc:
        raise ValueError(f"Query parameter '{name}' must be an integer.") from exc
    return max(1, min(parsed, 500))


def _geo_options(req: func.HttpRequest) -> dict:
    """Read reprojection/filtering options from query string.

    Defaults are tuned for web mapping: reproject to WGS84 and filter junk.
    Pass ?reproject=false to get raw drawing coordinates.
    """
    source_epsg = req.params.get("sourceEpsg")
    return {
        "reproject_to_wgs84": _bool_param(req, "reproject", True),
        "filter_to_source_bbox": _bool_param(req, "filter", True),
        "source_epsg": int(source_epsg) if source_epsg else None,
    }


def _convert_request(req: func.HttpRequest) -> ConvertedRequest:
    """Dispatch on content type and return converted GeoJSON plus source metadata."""
    content_type = (req.headers.get("content-type") or "").lower()
    options = _geo_options(req)

    if content_type.startswith("multipart/form-data"):
        uploaded = req.files.get("file")
        if uploaded is None:
            raise ValueError(
                "Upload a DWG or DXF file using the multipart form field named 'file'."
            )
        file_name = getattr(uploaded, "filename", "input.dwg") or "input.dwg"
        data = uploaded.stream.read()
        if not data:
            raise ValueError("Uploaded file is empty.")
        geojson, stats = convert_bytes_to_geojson(data, file_name, **options)
        return ConvertedRequest(
            geojson=geojson,
            stats=stats,
            source_info=_source_info(
                req,
                source_type="multipart-upload",
                file_name=file_name,
                content_type=content_type,
                source_hash=_sha256_bytes(data),
            ),
            request_options=options,
        )

    # Raw binary body (e.g. application/octet-stream) with file name in a header.
    if content_type.startswith("application/octet-stream"):
        data = req.get_body()
        if not data:
            raise ValueError("Request body is empty.")
        file_name = req.headers.get("x-file-name", "input.dwg")
        geojson, stats = convert_bytes_to_geojson(data, file_name, **options)
        return ConvertedRequest(
            geojson=geojson,
            stats=stats,
            source_info=_source_info(
                req,
                source_type="binary-body",
                file_name=file_name,
                content_type=content_type,
                source_hash=_sha256_bytes(data),
            ),
            request_options=options,
        )

    # Default: JSON body with a local file path.
    try:
        body = req.get_json()
    except ValueError as exc:
        raise ValueError("Request body must be JSON with a 'filePath' value.") from exc

    file_path = body.get("filePath")
    if not file_path:
        raise ValueError(
            "Provide multipart form-data with a 'file' field, or JSON with a 'filePath' value."
        )
    geojson, stats = convert_file_to_geojson(file_path, **options)
    return ConvertedRequest(
        geojson=geojson,
        stats=stats,
        source_info=_source_info(
            req,
            body=body,
            source_type="local-file-path",
            file_name=os.path.basename(file_path) or file_path,
            source_reference=file_path,
            content_type=content_type,
            source_hash=_sha256_file(file_path),
        ),
        request_options=options,
    )


def _source_info(
    req: func.HttpRequest,
    *,
    source_type: str,
    file_name: str,
    body: dict[str, Any] | None = None,
    source_reference: str | None = None,
    content_type: str | None = None,
    source_hash: str | None = None,
) -> SourceInfo:
    body = body or {}
    return SourceInfo(
        source_type=source_type,
        file_name=file_name,
        source_reference=_first_text(
            body.get("sourceReference"),
            req.headers.get("x-source-reference"),
            req.params.get("sourceReference"),
            source_reference,
        ),
        source_system=_first_text(
            body.get("sourceSystem"),
            req.headers.get("x-source-system"),
            req.params.get("sourceSystem"),
        ),
        source_hash=source_hash,
        content_type=content_type,
    )


def _first_text(*values: Any) -> str | None:
    for value in values:
        if value is None:
            continue
        text = str(value).strip()
        if text:
            return text
    return None


def _sha256_bytes(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def _sha256_file(file_path: str) -> str:
    digest = hashlib.sha256()
    with open(file_path, "rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def _error(correlation_id: str, code: str, message: str, status: int) -> func.HttpResponse:
    payload = {"correlationId": correlation_id, "error": code, "message": message}
    return func.HttpResponse(
        body=json.dumps(payload),
        status_code=status,
        mimetype="application/json",
        headers=_cors_headers(correlation_id),
    )


def _cors_headers(correlation_id: str) -> dict[str, str]:
    return {
        "x-correlation-id": correlation_id,
        "access-control-allow-origin": "*",
        "access-control-allow-methods": "GET, POST, PUT, OPTIONS",
        "access-control-allow-headers": "content-type, x-correlation-id",
        "access-control-expose-headers": "x-correlation-id",
    }
