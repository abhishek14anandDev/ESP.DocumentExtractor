"""Azure Functions (Python v2) app: convert DWG/DXF files to GeoJSON.

Endpoint:
    POST /api/cad/geojson

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
from typing import Any

import azure.functions as func

from dwg_geojson import (
    ConversionError,
    convert_bytes_to_geojson,
    convert_file_to_geojson,
)
from dwg_geojson.cosmos_repository import CosmosGeoJsonRepository
from dwg_geojson.repository import PersistenceError
from dwg_geojson.storage_models import SourceInfo
from dwg_geojson.storage_service import GeoJsonStorageService

app = func.FunctionApp(http_auth_level=func.AuthLevel.ANONYMOUS)
logger = logging.getLogger("dwg_geojson.function")
_storage_service: GeoJsonStorageService | None = None


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
    logger.info("[%s] CAD GeoJSON request received", correlation_id)

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

    try:
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


def _get_storage_service() -> GeoJsonStorageService:
    global _storage_service
    if _storage_service is None:
        _storage_service = GeoJsonStorageService(CosmosGeoJsonRepository.from_env())
    return _storage_service


def _bool_param(req: func.HttpRequest, name: str, default: bool) -> bool:
    value = req.params.get(name)
    if value is None:
        return default
    return value.strip().lower() in ("1", "true", "yes", "on")


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
        headers={"x-correlation-id": correlation_id},
    )
