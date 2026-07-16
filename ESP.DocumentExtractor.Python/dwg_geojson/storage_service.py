"""Application service that stores generated GeoJSON as Cosmos-safe chunks."""

from __future__ import annotations

import hashlib
import json
import logging
from datetime import UTC, datetime
from typing import Any

from .converter import ConversionStats
from .repository import GeoJsonRepository, PersistenceError
from .storage_models import GeoJsonChunk, SourceInfo, StoredGeoJsonMetadata


DEFAULT_TARGET_CHUNK_BYTES = 900 * 1024
DEFAULT_MAX_CHUNK_BYTES = 1_800_000
logger = logging.getLogger("dwg_geojson.storage_service")


class GeoJsonStorageService:
    """Persists a GeoJSON FeatureCollection using metadata plus feature chunks."""

    def __init__(
        self,
        repository: GeoJsonRepository,
        *,
        target_chunk_bytes: int = DEFAULT_TARGET_CHUNK_BYTES,
        max_chunk_bytes: int = DEFAULT_MAX_CHUNK_BYTES,
    ) -> None:
        self._repository = repository
        self._target_chunk_bytes = target_chunk_bytes
        self._max_chunk_bytes = max_chunk_bytes

    def store(
        self,
        *,
        conversion_id: str,
        correlation_id: str,
        geojson: dict[str, Any],
        stats: ConversionStats,
        source: SourceInfo,
        request_options: dict[str, Any],
    ) -> StoredGeoJsonMetadata:
        logger.info(
            "[%s] Preparing GeoJSON for Cosmos storage: conversionId=%s sourceType=%s "
            "fileName=%s features=%s",
            correlation_id,
            conversion_id,
            source.source_type,
            source.file_name,
            stats.feature_count,
        )

        if geojson.get("type") != "FeatureCollection":
            raise PersistenceError("Only GeoJSON FeatureCollection output can be stored.")

        raw_features = geojson.get("features")
        if not isinstance(raw_features, list):
            raise PersistenceError("GeoJSON FeatureCollection is missing a features array.")

        created_utc = datetime.now(UTC).isoformat()
        feature_groups = self._group_features(raw_features)
        chunk_count = len(feature_groups)
        logger.info(
            "[%s] GeoJSON chunking completed: conversionId=%s chunks=%s "
            "targetChunkBytes=%s maxChunkBytes=%s",
            correlation_id,
            conversion_id,
            chunk_count,
            self._target_chunk_bytes,
            self._max_chunk_bytes,
        )
        chunks = [
            GeoJsonChunk(
                conversion_id=conversion_id,
                chunk_index=index,
                chunk_count=chunk_count,
                features=features,
                created_utc=created_utc,
            )
            for index, features in enumerate(feature_groups)
        ]

        for chunk in chunks:
            if _json_size(chunk.to_item()) > self._max_chunk_bytes:
                raise PersistenceError(
                    "A GeoJSON chunk exceeds the Cosmos item size budget. "
                    "Reduce the input size or store unusually large features externally."
                )

        metadata = StoredGeoJsonMetadata(
            conversion_id=conversion_id,
            correlation_id=correlation_id,
            source=source,
            feature_count=stats.feature_count,
            converter=stats.converter,
            source_epsg=stats.source_epsg,
            reprojected=stats.reprojected,
            filtered_out=stats.filtered_out,
            chunk_count=chunk_count,
            output_hash=_sha256_json(geojson),
            created_utc=created_utc,
            container_name=self._repository.container_name,
            request_options=request_options,
            entity_counts=stats.entity_counts,
            skipped=stats.skipped,
        )

        self._repository.ensure_ready()
        for chunk in chunks:
            self._repository.upsert_chunk(chunk)
        self._repository.upsert_metadata(metadata)
        logger.info(
            "[%s] GeoJSON stored in Cosmos: conversionId=%s chunks=%s features=%s "
            "container=%s",
            correlation_id,
            conversion_id,
            chunk_count,
            stats.feature_count,
            metadata.container_name,
        )
        return metadata

    def _group_features(self, features: list[dict[str, Any]]) -> list[list[dict[str, Any]]]:
        if not features:
            return [[]]

        groups: list[list[dict[str, Any]]] = []
        current: list[dict[str, Any]] = []
        current_bytes = 0

        for feature in features:
            feature_bytes = _json_size(feature)
            if feature_bytes > self._max_chunk_bytes:
                raise PersistenceError(
                    "A single GeoJSON feature exceeds the Cosmos item size budget."
                )

            next_bytes = current_bytes + feature_bytes + (1 if current else 0)
            if current and next_bytes > self._target_chunk_bytes:
                groups.append(current)
                current = []
                current_bytes = 0

            current.append(feature)
            current_bytes += feature_bytes + (1 if len(current) > 1 else 0)

        if current:
            groups.append(current)
        return groups


def _json_size(value: Any) -> int:
    return len(_json_bytes(value))


def _sha256_json(value: Any) -> str:
    return hashlib.sha256(_json_bytes(value)).hexdigest()


def _json_bytes(value: Any) -> bytes:
    return json.dumps(value, separators=(",", ":"), ensure_ascii=False).encode("utf-8")
