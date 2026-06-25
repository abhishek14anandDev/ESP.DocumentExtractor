"""DWG/DXF to GeoJSON conversion.

A DWG file is a proprietary binary AutoCAD format that pure-Python libraries
cannot parse directly, so this module relies on external tooling. Two
independent pipelines are supported, selected automatically:

* ``.dwg`` (preferred): LibreDWG ``dwgread -O GeoJSON`` emits a GeoJSON
  ``FeatureCollection`` natively, straight from the DWG. This is fast and avoids
  the malformed-MTEXT issues seen in LibreDWG's DXF writer.
    - install: ``brew install libredwg`` / ``apt-get install libredwg``
* ``.dxf`` (and ``.dwg`` fallback): the file is parsed with :mod:`ezdxf` and the
  modelspace entities are translated to GeoJSON. For ``.dwg`` fallback the file
  is first converted with LibreDWG ``dwg2dxf`` or the ODA File Converter.

Output coordinates are rounded to keep payloads compact and properties are
normalized to ``entityType`` / ``layer`` / ``handle`` / ``color``.
"""

from __future__ import annotations

import json
import logging
import math
import os
import shutil
import subprocess
import tempfile
from dataclasses import dataclass, field
from typing import Any

import ezdxf
from ezdxf.document import Drawing
from ezdxf.entities import DXFEntity

logger = logging.getLogger(__name__)

# Number of straight segments used to approximate a full circle.
_CIRCLE_SEGMENTS = 64
# Decimal places kept for projected (metre) coordinates.
_COORD_PRECISION = 6
# Decimal places kept for WGS84 lon/lat (~1e-7 deg ≈ 1 cm).
_WGS84_PRECISION = 8

# Default source CRS for this dataset family (UK utility "as laid" drawings are
# almost always British National Grid). Override per-request when needed.
DEFAULT_SOURCE_EPSG = 27700

# Plausible British National Grid extent (metres). Used as a coarse first pass
# to discard title-block / annotation near the local origin and gross outliers.
_BNG_BBOX = (1_000.0, 1_000.0, 700_000.0, 1_300_000.0)  # (minx, miny, maxx, maxy)

# After the coarse pass, features are clustered around the robust (median)
# centre of the drawing and anything farther than this radius (metres) is
# treated as a stray symbol/outlier. A single "as laid" sheet spans a few km.
_DEFAULT_CLUSTER_RADIUS_M = 25_000.0


class ConversionError(Exception):
    """Raised when a DWG/DXF file cannot be converted to GeoJSON."""


@dataclass
class ConversionStats:
    """Lightweight diagnostics returned alongside the GeoJSON."""

    feature_count: int = 0
    entity_counts: dict[str, int] = field(default_factory=dict)
    skipped: dict[str, int] = field(default_factory=dict)
    converter: str | None = None
    source_epsg: int | None = None
    reprojected: bool = False
    filtered_out: int = 0


# --------------------------------------------------------------------------- #
# Native DWG -> GeoJSON via LibreDWG dwgread
# --------------------------------------------------------------------------- #
def _round_coords(node: Any) -> Any:
    """Recursively round numeric coordinates within a GeoJSON geometry."""
    if isinstance(node, list):
        if node and all(isinstance(v, (int, float)) for v in node):
            return [round(float(v), _COORD_PRECISION) for v in node]
        return [_round_coords(child) for child in node]
    if isinstance(node, (int, float)):
        return round(float(node), _COORD_PRECISION)
    return node


def _entity_type_from_subclasses(subclasses: str) -> str:
    """'AcDbEntity : AcDbLine' -> 'LINE'."""
    if not subclasses:
        return "UNKNOWN"
    last = subclasses.split(":")[-1].strip()
    if last.startswith("AcDb"):
        last = last[4:]
    return last.upper()


def _normalize_native_feature(feature: dict[str, Any]) -> dict[str, Any]:
    """Normalize a dwgread GeoJSON feature to our property schema."""
    raw = feature.get("properties") or {}
    props: dict[str, Any] = {
        "entityType": _entity_type_from_subclasses(raw.get("SubClasses", "")),
    }
    if "Layer" in raw:
        props["layer"] = raw["Layer"]
    handle = raw.get("EntityHandle") or feature.get("id")
    if handle is not None:
        props["handle"] = handle
    if "Color" in raw:
        props["color"] = raw["Color"]
    if raw.get("Text"):
        props["text"] = raw["Text"]

    geometry = feature.get("geometry")
    if geometry and "coordinates" in geometry:
        geometry = dict(geometry)
        geometry["coordinates"] = _round_coords(geometry["coordinates"])

    return {"type": "Feature", "geometry": geometry, "properties": props}


def _native_dwg_to_geojson(dwg_path: str, work_dir: str) -> tuple[dict[str, Any], ConversionStats]:
    """Convert DWG -> GeoJSON using LibreDWG dwgread (-O GeoJSON)."""
    dwgread = shutil.which("dwgread")
    if not dwgread:
        raise ConversionError("dwgread not found")

    out_path = os.path.join(work_dir, "native.geojson")
    logger.info("Converting DWG natively with LibreDWG dwgread: %s", dwgread)
    result = subprocess.run(
        [dwgread, "-O", "GeoJSON", "-o", out_path, dwg_path],
        capture_output=True,
        text=True,
    )
    if result.returncode != 0 or not os.path.exists(out_path):
        raise ConversionError(
            f"dwgread GeoJSON conversion failed (code={result.returncode}): "
            f"{result.stderr.strip()}"
        )

    with open(out_path, "r", encoding="utf-8", errors="replace") as handle:
        raw = json.load(handle)

    stats = ConversionStats(converter="libredwg:dwgread")
    features: list[dict[str, Any]] = []
    for feature in raw.get("features", []):
        if not feature.get("geometry"):
            continue
        normalized = _normalize_native_feature(feature)
        etype = normalized["properties"].get("entityType", "UNKNOWN")
        stats.entity_counts[etype] = stats.entity_counts.get(etype, 0) + 1
        features.append(normalized)

    stats.feature_count = len(features)
    return {"type": "FeatureCollection", "features": features}, stats


# --------------------------------------------------------------------------- #
# DWG -> DXF conversion (fallback path)
# --------------------------------------------------------------------------- #
def _convert_dwg_to_dxf(dwg_path: str, work_dir: str) -> tuple[str, str]:
    """Convert a DWG file to DXF, returning (dxf_path, converter_name)."""
    dwg2dxf = shutil.which("dwg2dxf")
    if dwg2dxf:
        dxf_path = os.path.join(work_dir, "converted.dxf")
        logger.info("Converting DWG with LibreDWG dwg2dxf: %s", dwg2dxf)
        result = subprocess.run(
            [dwg2dxf, "-y", "-o", dxf_path, dwg_path],
            capture_output=True,
            text=True,
        )
        if result.returncode == 0 and os.path.exists(dxf_path):
            return dxf_path, "libredwg:dwg2dxf"
        logger.warning(
            "dwg2dxf failed (code=%s): %s", result.returncode, result.stderr.strip()
        )

    try:
        from ezdxf.addons import odafc

        if odafc.is_installed():
            logger.info("Converting DWG with ODA File Converter")
            doc = odafc.readfile(dwg_path)
            dxf_path = os.path.join(work_dir, "converted.dxf")
            doc.saveas(dxf_path)
            return dxf_path, "oda:ODAFileConverter"
    except Exception as exc:  # noqa: BLE001
        logger.warning("ODA File Converter unavailable or failed: %s", exc)

    raise ConversionError(
        "No DWG converter available. Install LibreDWG (provides 'dwgread'/'dwg2dxf') "
        "or the ODA File Converter to read .dwg files."
    )


def _read_dxf(dxf_path: str) -> Drawing:
    try:
        return ezdxf.readfile(dxf_path)
    except IOError as exc:
        raise ConversionError(f"Failed to read DXF data: {exc}") from exc
    except ezdxf.DXFStructureError:
        logger.warning("Strict DXF read failed; retrying with ezdxf recover mode")
        from ezdxf import recover

        try:
            doc, auditor = recover.readfile(dxf_path)
        except Exception as exc:  # noqa: BLE001
            raise ConversionError(f"Failed to read DXF data: {exc}") from exc
        if auditor.has_errors:
            logger.warning("DXF recovered with %d issue(s)", len(auditor.errors))
        return doc


# --------------------------------------------------------------------------- #
# Geometry helpers (DXF entity -> GeoJSON geometry, ezdxf path)
# --------------------------------------------------------------------------- #
def _pt(point) -> list[float]:
    return [round(float(point[0]), _COORD_PRECISION), round(float(point[1]), _COORD_PRECISION)]


def _arc_points(center, radius: float, start_deg: float, end_deg: float) -> list[list[float]]:
    if end_deg < start_deg:
        end_deg += 360.0
    sweep = end_deg - start_deg
    steps = max(2, int(_CIRCLE_SEGMENTS * sweep / 360.0))
    points = []
    for i in range(steps + 1):
        angle = math.radians(start_deg + sweep * i / steps)
        points.append(
            [
                round(center[0] + radius * math.cos(angle), _COORD_PRECISION),
                round(center[1] + radius * math.sin(angle), _COORD_PRECISION),
            ]
        )
    return points


def _circle_points(center, radius: float) -> list[list[float]]:
    points = []
    for i in range(_CIRCLE_SEGMENTS + 1):
        angle = 2.0 * math.pi * i / _CIRCLE_SEGMENTS
        points.append(
            [
                round(center[0] + radius * math.cos(angle), _COORD_PRECISION),
                round(center[1] + radius * math.sin(angle), _COORD_PRECISION),
            ]
        )
    return points


def _geometry_for_entity(entity: DXFEntity) -> dict[str, Any] | None:
    dxftype = entity.dxftype()

    if dxftype == "LINE":
        return {"type": "LineString", "coordinates": [_pt(entity.dxf.start), _pt(entity.dxf.end)]}

    if dxftype == "LWPOLYLINE":
        coords = [[round(float(x), _COORD_PRECISION), round(float(y), _COORD_PRECISION)]
                  for x, y, *_ in entity.get_points()]
        if len(coords) < 2:
            return None
        if entity.closed and coords[0] != coords[-1]:
            coords.append(coords[0])
            return {"type": "Polygon", "coordinates": [coords]}
        return {"type": "LineString", "coordinates": coords}

    if dxftype == "POLYLINE":
        coords = [_pt(v.dxf.location) for v in entity.vertices]
        if len(coords) < 2:
            return None
        if entity.is_closed and coords[0] != coords[-1]:
            coords.append(coords[0])
            return {"type": "Polygon", "coordinates": [coords]}
        return {"type": "LineString", "coordinates": coords}

    if dxftype == "POINT":
        return {"type": "Point", "coordinates": _pt(entity.dxf.location)}

    if dxftype == "CIRCLE":
        ring = _circle_points(entity.dxf.center, float(entity.dxf.radius))
        return {"type": "Polygon", "coordinates": [ring]}

    if dxftype == "ARC":
        coords = _arc_points(
            entity.dxf.center,
            float(entity.dxf.radius),
            float(entity.dxf.start_angle),
            float(entity.dxf.end_angle),
        )
        return {"type": "LineString", "coordinates": coords}

    if dxftype in ("TEXT", "MTEXT"):
        insert = entity.dxf.get("insert", None) or entity.dxf.get("align_point", None)
        if insert is None:
            return None
        return {"type": "Point", "coordinates": _pt(insert)}

    if dxftype in ("ELLIPSE", "SPLINE"):
        try:
            coords = [_pt(p) for p in entity.flattening(distance=0.1)]
        except Exception:  # noqa: BLE001
            return None
        if len(coords) < 2:
            return None
        return {"type": "LineString", "coordinates": coords}

    return None


def _properties_for_entity(entity: DXFEntity) -> dict[str, Any]:
    props: dict[str, Any] = {"entityType": entity.dxftype()}
    dxf = entity.dxf
    if dxf.hasattr("layer"):
        props["layer"] = dxf.layer
    if dxf.hasattr("handle"):
        props["handle"] = dxf.handle
    if entity.dxftype() in ("TEXT", "MTEXT"):
        text = getattr(dxf, "text", None)
        if text:
            props["text"] = text
    color = dxf.get("color", None)
    if color is not None:
        props["color"] = color
    return props


def drawing_to_geojson(doc: Drawing) -> tuple[dict[str, Any], ConversionStats]:
    """Convert an ezdxf Drawing's modelspace into a GeoJSON FeatureCollection."""
    stats = ConversionStats()
    features: list[dict[str, Any]] = []

    for entity in doc.modelspace():
        dxftype = entity.dxftype()
        stats.entity_counts[dxftype] = stats.entity_counts.get(dxftype, 0) + 1
        try:
            geometry = _geometry_for_entity(entity)
        except Exception as exc:  # noqa: BLE001
            logger.debug("Skipping %s due to error: %s", dxftype, exc)
            geometry = None

        if geometry is None:
            stats.skipped[dxftype] = stats.skipped.get(dxftype, 0) + 1
            continue

        features.append(
            {
                "type": "Feature",
                "geometry": geometry,
                "properties": _properties_for_entity(entity),
            }
        )

    stats.feature_count = len(features)
    return {"type": "FeatureCollection", "features": features}, stats


# --------------------------------------------------------------------------- #
# Post-processing: bounding-box filter + reprojection to WGS84
# --------------------------------------------------------------------------- #
def _first_coord(node: Any) -> list[float] | None:
    """Return the first [x, y] position found in a geometry coordinates tree."""
    if isinstance(node, list):
        if node and all(isinstance(v, (int, float)) for v in node):
            return node
        for child in node:
            found = _first_coord(child)
            if found is not None:
                return found
    return None


def _in_bbox(coord: list[float], bbox: tuple[float, float, float, float]) -> bool:
    minx, miny, maxx, maxy = bbox
    return minx <= coord[0] <= maxx and miny <= coord[1] <= maxy


def _reproject_node(node: Any, transformer, precision: int) -> Any:
    """Recursively reproject a geometry coordinates tree with a pyproj transformer."""
    if isinstance(node, list):
        if node and all(isinstance(v, (int, float)) for v in node):
            lon, lat = transformer.transform(node[0], node[1])
            return [round(lon, precision), round(lat, precision)]
        return [_reproject_node(child, transformer, precision) for child in node]
    return node


def _median(values: list[float]) -> float:
    ordered = sorted(values)
    n = len(ordered)
    mid = n // 2
    if n % 2:
        return ordered[mid]
    return 0.5 * (ordered[mid - 1] + ordered[mid])


def _cluster_centre(
    feature_collection: dict[str, Any],
    bbox: tuple[float, float, float, float],
) -> tuple[float, float] | None:
    """Robust (median) centre of features falling inside the coarse bbox."""
    xs: list[float] = []
    ys: list[float] = []
    for feature in feature_collection.get("features", []):
        geometry = feature.get("geometry")
        if not geometry or "coordinates" not in geometry:
            continue
        rep = _first_coord(geometry["coordinates"])
        if rep is not None and _in_bbox(rep, bbox):
            xs.append(rep[0])
            ys.append(rep[1])
    if not xs:
        return None
    return _median(xs), _median(ys)


def _postprocess(
    feature_collection: dict[str, Any],
    stats: ConversionStats,
    *,
    source_epsg: int | None,
    reproject_to_wgs84: bool,
    bbox: tuple[float, float, float, float] | None,
    cluster_radius_m: float | None,
) -> tuple[dict[str, Any], ConversionStats]:
    """Apply optional cluster filtering and reprojection to WGS84.

    Filtering is two-stage: a coarse source-CRS bbox removes near-origin
    annotation and gross outliers, then features farther than
    ``cluster_radius_m`` from the robust data centre are dropped so only the
    real network (not stray symbols) remains.
    """
    transformer = None
    if reproject_to_wgs84:
        try:
            from pyproj import Transformer
        except ImportError as exc:  # pragma: no cover
            raise ConversionError(
                "pyproj is required for reprojection. Install it with 'pip install pyproj'."
            ) from exc
        epsg = source_epsg or DEFAULT_SOURCE_EPSG
        transformer = Transformer.from_crs(f"EPSG:{epsg}", "EPSG:4326", always_xy=True)
        stats.source_epsg = epsg
        stats.reprojected = True

    centre = None
    if bbox is not None and cluster_radius_m:
        centre = _cluster_centre(feature_collection, bbox)

    if transformer is None and bbox is None:
        return feature_collection, stats

    kept: list[dict[str, Any]] = []
    for feature in feature_collection.get("features", []):
        geometry = feature.get("geometry")
        if not geometry or "coordinates" not in geometry:
            continue
        rep = _first_coord(geometry["coordinates"])
        if rep is None:
            continue
        if bbox is not None and not _in_bbox(rep, bbox):
            stats.filtered_out += 1
            continue
        if centre is not None and (
            abs(rep[0] - centre[0]) > cluster_radius_m
            or abs(rep[1] - centre[1]) > cluster_radius_m
        ):
            stats.filtered_out += 1
            continue
        if transformer is not None:
            geometry = dict(geometry)
            geometry["coordinates"] = _reproject_node(
                geometry["coordinates"], transformer, _WGS84_PRECISION
            )
            feature = {**feature, "geometry": geometry}
        kept.append(feature)

    feature_collection["features"] = kept
    stats.feature_count = len(kept)
    return feature_collection, stats


# --------------------------------------------------------------------------- #
# Public API
# --------------------------------------------------------------------------- #
def _convert_path(file_path: str, work_dir: str) -> tuple[dict[str, Any], ConversionStats]:
    ext = os.path.splitext(file_path)[1].lower()

    if ext == ".dwg":
        # Preferred: native dwgread GeoJSON export.
        if shutil.which("dwgread"):
            try:
                return _native_dwg_to_geojson(file_path, work_dir)
            except ConversionError as exc:
                logger.warning("Native dwgread path failed, falling back to DXF: %s", exc)
        # Fallback: DWG -> DXF -> ezdxf.
        dxf_path, converter = _convert_dwg_to_dxf(file_path, work_dir)
        geojson, stats = drawing_to_geojson(_read_dxf(dxf_path))
        stats.converter = converter
        return geojson, stats

    if ext == ".dxf":
        geojson, stats = drawing_to_geojson(_read_dxf(file_path))
        stats.converter = "ezdxf"
        return geojson, stats

    raise ConversionError(f"Unsupported file extension '{ext}'. Expected .dwg or .dxf.")


def convert_file_to_geojson(
    file_path: str,
    *,
    source_epsg: int | None = None,
    reproject_to_wgs84: bool = False,
    filter_to_source_bbox: bool = False,
    cluster_radius_m: float | None = _DEFAULT_CLUSTER_RADIUS_M,
) -> tuple[dict[str, Any], ConversionStats]:
    """Path to a .dwg/.dxf file -> (GeoJSON dict, stats).

    Args:
        source_epsg: EPSG code of the drawing's coordinates (default 27700 / BNG
            when reprojecting). Ignored unless ``reproject_to_wgs84`` is set.
        reproject_to_wgs84: reproject output coordinates to EPSG:4326 (lon/lat),
            required for Google Maps / web mapping.
        filter_to_source_bbox: drop features outside the plausible source-CRS
            extent (removes title-block/annotation near origin and outliers).
        cluster_radius_m: when filtering, also drop features farther than this
            distance (metres) from the robust data centre. Set None to disable.
    """
    if not os.path.exists(file_path):
        raise ConversionError(f"File '{file_path}' does not exist.")
    bbox = _BNG_BBOX if filter_to_source_bbox else None
    with tempfile.TemporaryDirectory(prefix="dwg2geojson_") as work_dir:
        geojson, stats = _convert_path(file_path, work_dir)
    return _postprocess(
        geojson, stats,
        source_epsg=source_epsg,
        reproject_to_wgs84=reproject_to_wgs84,
        bbox=bbox,
        cluster_radius_m=cluster_radius_m,
    )


def convert_bytes_to_geojson(
    data: bytes,
    file_name: str,
    *,
    source_epsg: int | None = None,
    reproject_to_wgs84: bool = False,
    filter_to_source_bbox: bool = False,
    cluster_radius_m: float | None = _DEFAULT_CLUSTER_RADIUS_M,
) -> tuple[dict[str, Any], ConversionStats]:
    """Convert in-memory file bytes (e.g. an uploaded file) to GeoJSON."""
    ext = os.path.splitext(file_name)[1].lower() or ".dwg"
    bbox = _BNG_BBOX if filter_to_source_bbox else None
    with tempfile.TemporaryDirectory(prefix="dwg2geojson_") as work_dir:
        src_path = os.path.join(work_dir, f"input{ext}")
        with open(src_path, "wb") as handle:
            handle.write(data)
        geojson, stats = _convert_path(src_path, work_dir)
    return _postprocess(
        geojson, stats,
        source_epsg=source_epsg,
        reproject_to_wgs84=reproject_to_wgs84,
        bbox=bbox,
        cluster_radius_m=cluster_radius_m,
    )
