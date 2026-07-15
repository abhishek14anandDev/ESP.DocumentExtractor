"""Persistence domain models for generated GeoJSON."""

from __future__ import annotations

from dataclasses import dataclass, field
from typing import Any


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
