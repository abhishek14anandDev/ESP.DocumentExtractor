"""DWG/DXF -> GeoJSON conversion package."""

from .converter import (
    ConversionError,
    ConversionStats,
    convert_bytes_to_geojson,
    convert_file_to_geojson,
    drawing_to_geojson,
)
from .repository import PersistenceError, StoredGeoJsonNotFoundError
from .storage_models import GeoJsonChunk, SourceInfo, StoredGeoJsonMetadata

__all__ = [
    "ConversionError",
    "ConversionStats",
    "GeoJsonChunk",
    "PersistenceError",
    "SourceInfo",
    "StoredGeoJsonMetadata",
    "StoredGeoJsonNotFoundError",
    "convert_bytes_to_geojson",
    "convert_file_to_geojson",
    "drawing_to_geojson",
]
