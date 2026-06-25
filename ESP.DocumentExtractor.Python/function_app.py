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

import json
import logging
import uuid

import azure.functions as func

from dwg_geojson import (
    ConversionError,
    convert_bytes_to_geojson,
    convert_file_to_geojson,
)

app = func.FunctionApp(http_auth_level=func.AuthLevel.ANONYMOUS)
logger = logging.getLogger("dwg_geojson.function")


@app.route(route="cad/geojson", methods=["POST"])
def cad_geojson(req: func.HttpRequest) -> func.HttpResponse:
    correlation_id = req.headers.get("x-correlation-id") or uuid.uuid4().hex
    logger.info("[%s] CAD GeoJSON request received", correlation_id)

    try:
        geojson, stats = _convert_request(req)
    except ConversionError as exc:
        logger.warning("[%s] Conversion failed: %s", correlation_id, exc)
        return _error(correlation_id, "cad.conversion_failed", str(exc), 400)
    except ValueError as exc:
        logger.warning("[%s] Bad request: %s", correlation_id, exc)
        return _error(correlation_id, "request.invalid", str(exc), 400)
    except Exception as exc:  # noqa: BLE001 - surface unexpected errors as 500
        logger.exception("[%s] Unexpected error", correlation_id)
        return _error(correlation_id, "cad.internal_error", str(exc), 500)

    headers = {
        "x-correlation-id": correlation_id,
        "x-conversion-converter": stats.converter or "unknown",
        "x-conversion-feature-count": str(stats.feature_count),
        "x-conversion-reprojected": str(stats.reprojected).lower(),
        "x-conversion-source-epsg": str(stats.source_epsg or ""),
        "x-conversion-filtered-out": str(stats.filtered_out),
        "access-control-expose-headers": "x-correlation-id,x-conversion-converter,"
        "x-conversion-feature-count,x-conversion-reprojected,x-conversion-source-epsg,"
        "x-conversion-filtered-out",
        "access-control-allow-origin": "*",
    }
    return func.HttpResponse(
        body=json.dumps(geojson),
        status_code=200,
        mimetype="application/geo+json",
        headers=headers,
    )


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


def _convert_request(req: func.HttpRequest):
    """Dispatch on content type and return (geojson, stats)."""
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
        return convert_bytes_to_geojson(data, file_name, **options)

    # Raw binary body (e.g. application/octet-stream) with file name in a header.
    if content_type.startswith("application/octet-stream"):
        data = req.get_body()
        if not data:
            raise ValueError("Request body is empty.")
        file_name = req.headers.get("x-file-name", "input.dwg")
        return convert_bytes_to_geojson(data, file_name, **options)

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
    return convert_file_to_geojson(file_path, **options)


def _error(correlation_id: str, code: str, message: str, status: int) -> func.HttpResponse:
    payload = {"correlationId": correlation_id, "error": code, "message": message}
    return func.HttpResponse(
        body=json.dumps(payload),
        status_code=status,
        mimetype="application/json",
        headers={"x-correlation-id": correlation_id},
    )
