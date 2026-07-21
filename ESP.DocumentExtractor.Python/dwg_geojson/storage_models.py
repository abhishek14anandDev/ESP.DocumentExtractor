"""Persistence domain models for generated GeoJSON."""

from __future__ import annotations

from dataclasses import dataclass, field
from typing import Any


ANNOTATION_CATEGORIES = frozenset(
    {
        "primary-substation",
        "cable-route-segment",
        "road-footway-crossing",
        "directional-drilling",
        "utility-service-route",
        "commercial-boundary",
        "custom",
    }
)


def validate_annotation(annotation: dict[str, Any]) -> dict[str, Any]:
    """Validate one curated map annotation and return a safe copy."""
    if not isinstance(annotation, dict):
        raise ValueError("Each annotation must be an object.")

    category = str(annotation.get("category") or "").strip()
    if category not in ANNOTATION_CATEGORIES:
        raise ValueError(f"Unsupported annotation category '{category}'.")

    title = str(annotation.get("title") or "").strip()
    if not title:
        raise ValueError("Each annotation requires a title.")

    geometry = annotation.get("geometry")
    if not isinstance(geometry, dict):
        raise ValueError("Each annotation requires GeoJSON geometry.")
    geometry_type = geometry.get("type")
    coordinates = geometry.get("coordinates")
    allowed_types = {
        "primary-substation": {"Point"},
        "road-footway-crossing": {"Point"},
        "directional-drilling": {"Point"},
        "custom": {"Point"},
        "cable-route-segment": {"LineString"},
        "utility-service-route": {"LineString"},
        "commercial-boundary": {"LineString", "Polygon"},
    }
    if geometry_type not in allowed_types[category] or not _has_coordinates(coordinates):
        allowed = ", ".join(sorted(allowed_types[category]))
        raise ValueError(f"Annotation category '{category}' requires {allowed} geometry.")

    return {
        "id": str(annotation.get("id") or "").strip(),
        "category": category,
        "title": title,
        "notes": str(annotation.get("notes") or "").strip(),
        "geometry": {"type": geometry_type, "coordinates": coordinates},
        "sourceCadHandle": _optional_text(annotation.get("sourceCadHandle")),
        "sourceCadLayer": _optional_text(annotation.get("sourceCadLayer")),
        "createdUtc": _optional_text(annotation.get("createdUtc")),
        "updatedUtc": _optional_text(annotation.get("updatedUtc")),
    }


def _has_coordinates(value: Any) -> bool:
    if not isinstance(value, list) or not value:
        return False
    if isinstance(value[0], (int, float)):
        return len(value) >= 2 and all(isinstance(item, (int, float)) for item in value[:2])
    return all(_has_coordinates(item) for item in value)


def _optional_text(value: Any) -> str | None:
    text = str(value or "").strip()
    return text or None


@dataclass(frozen=True)
class SourceInfo:
    """Describes where the CAD input came from."""

    source_type: str
    file_name: str
    source_reference: str | None = None
    source_system: str | None = None
    source_hash: str | None = None
    content_type: str | None = None

    def to_dict(self) -> dict[str, Any]:
        return {
            "sourceType": self.source_type,
            "fileName": self.file_name,
            "sourceReference": self.source_reference,
            "sourceSystem": self.source_system,
            "sourceHash": self.source_hash,
            "contentType": self.content_type,
        }


@dataclass(frozen=True)
class StoredGeoJsonMetadata:
    """Metadata document for one persisted GeoJSON conversion."""

    conversion_id: str
    correlation_id: str
    source: SourceInfo
    feature_count: int
    converter: str | None
    source_epsg: int | None
    reprojected: bool
    filtered_out: int
    chunk_count: int
    output_hash: str
    created_utc: str
    container_name: str
    request_options: dict[str, Any] = field(default_factory=dict)
    entity_counts: dict[str, int] = field(default_factory=dict)
    skipped: dict[str, int] = field(default_factory=dict)

    @property
    def id(self) -> str:
        return f"{self.conversion_id}:metadata"

    def to_item(self) -> dict[str, Any]:
        return {
            "id": self.id,
            "conversionId": self.conversion_id,
            "documentType": "conversionMetadata",
            "correlationId": self.correlation_id,
            "source": self.source.to_dict(),
            "featureCount": self.feature_count,
            "converter": self.converter,
            "sourceEpsg": self.source_epsg,
            "reprojected": self.reprojected,
            "filteredOut": self.filtered_out,
            "chunkCount": self.chunk_count,
            "outputHash": self.output_hash,
            "createdUtc": self.created_utc,
            "containerName": self.container_name,
            "requestOptions": self.request_options,
            "entityCounts": self.entity_counts,
            "skipped": self.skipped,
        }


@dataclass(frozen=True)
class GeoJsonChunk:
    """Ordered chunk of GeoJSON features for one conversion."""

    conversion_id: str
    chunk_index: int
    chunk_count: int
    features: list[dict[str, Any]]
    created_utc: str

    @property
    def id(self) -> str:
        return f"{self.conversion_id}:chunk:{self.chunk_index:06d}"

    def to_item(self) -> dict[str, Any]:
        return {
            "id": self.id,
            "conversionId": self.conversion_id,
            "documentType": "geoJsonChunk",
            "chunkIndex": self.chunk_index,
            "chunkCount": self.chunk_count,
            "createdUtc": self.created_utc,
            "features": self.features,
        }


@dataclass(frozen=True)
class DocumentAnalysis:
    """Curated drawing facts and map annotations associated with one conversion."""

    conversion_id: str
    updated_utc: str
    created_utc: str
    drawing: dict[str, Any] = field(default_factory=dict)
    route_summary: str = ""
    cable_lengths: list[dict[str, Any]] = field(default_factory=list)
    layouts: list[str] = field(default_factory=list)
    cad_blocks: list[str] = field(default_factory=list)
    caveats: list[str] = field(default_factory=list)
    annotations: list[dict[str, Any]] = field(default_factory=list)

    @property
    def id(self) -> str:
        return f"{self.conversion_id}:analysis"

    def to_item(self) -> dict[str, Any]:
        return {
            "id": self.id,
            "conversionId": self.conversion_id,
            "documentType": "documentAnalysis",
            "schemaVersion": 1,
            "provenance": "user-curated",
            "createdUtc": self.created_utc,
            "updatedUtc": self.updated_utc,
            "drawing": self.drawing,
            "routeSummary": self.route_summary,
            "cableLengths": self.cable_lengths,
            "layouts": self.layouts,
            "cadBlocks": self.cad_blocks,
            "caveats": self.caveats,
            "annotations": self.annotations,
        }
