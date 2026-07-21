"""Application service that reconstructs stored GeoJSON from Cosmos chunks."""

from __future__ import annotations

import logging
from typing import Any

from .repository import GeoJsonRepository, PersistenceError, StoredGeoJsonNotFoundError

logger = logging.getLogger("dwg_geojson.retrieval_service")

#comment
class GeoJsonRetrievalService:
    """Reads persisted metadata and chunks and rebuilds a FeatureCollection."""

    def __init__(self, repository: GeoJsonRepository) -> None:
        self._repository = repository

    def get(self, conversion_id: str) -> dict[str, Any]:
        conversion_id = (conversion_id or "").strip()
        if not conversion_id:
            raise ValueError("conversion_id is required.")

        logger.info("Retrieving stored GeoJSON: conversionId=%s", conversion_id)
        self._repository.ensure_ready()
        metadata = self._repository.get_metadata(conversion_id)
        if metadata is None:
            logger.info("Stored GeoJSON metadata not found: conversionId=%s", conversion_id)
            raise StoredGeoJsonNotFoundError(
                f"GeoJSON conversion '{conversion_id}' was not found."
            )

        chunks = self._repository.get_chunks(conversion_id)
        chunk_count = int(metadata.get("chunkCount") or 0)
        if chunk_count != len(chunks):
            logger.info(
                "Stored GeoJSON chunk count mismatch: conversionId=%s expected=%s found=%s",
                conversion_id,
                chunk_count,
                len(chunks),
            )
            raise PersistenceError(
                f"GeoJSON conversion '{conversion_id}' is incomplete: "
                f"expected {chunk_count} chunks, found {len(chunks)}."
            )

        chunks = sorted(chunks, key=lambda chunk: int(chunk.get("chunkIndex", 0)))
        for expected_index, chunk in enumerate(chunks):
            if int(chunk.get("chunkIndex", -1)) != expected_index:
                logger.info(
                    "Stored GeoJSON chunk order mismatch: conversionId=%s expectedIndex=%s",
                    conversion_id,
                    expected_index,
                )
                raise PersistenceError(
                    f"GeoJSON conversion '{conversion_id}' has a missing or "
                    f"out-of-order chunk at index {expected_index}."
                )

        features: list[dict[str, Any]] = []
        for chunk in chunks:
            chunk_features = chunk.get("features")
            if not isinstance(chunk_features, list):
                logger.info(
                    "Stored GeoJSON chunk is invalid: conversionId=%s chunkIndex=%s",
                    conversion_id,
                    chunk.get("chunkIndex"),
                )
                raise PersistenceError(
                    f"GeoJSON conversion '{conversion_id}' contains an invalid chunk."
                )
            features.extend(chunk_features)

        logger.info(
            "Stored GeoJSON reconstructed: conversionId=%s chunks=%s features=%s",
            conversion_id,
            len(chunks),
            len(features),
        )
        return {
            "conversionId": conversion_id,
            "metadata": metadata,
            "analysis": self._repository.get_analysis(conversion_id),
            "geojson": {
                "type": "FeatureCollection",
                "features": features,
            },
        }

    def list(self, limit: int = 100) -> list[dict[str, Any]]:
        limit = max(1, min(int(limit), 500))
        logger.info("Listing stored GeoJSON metadata: limit=%s", limit)
        metadata_items = self._repository.list_metadata(limit)
        metadata_items = sorted(
            metadata_items,
            key=lambda item: str(item.get("createdUtc") or ""),
            reverse=True,
        )
        result = [_to_summary(item) for item in metadata_items[:limit]]
        logger.info("Stored GeoJSON metadata list ready: returned=%s", len(result))
        return result


def _to_summary(item: dict[str, Any]) -> dict[str, Any]:
    source = item.get("source") if isinstance(item.get("source"), dict) else {}
    return {
        "conversionId": item.get("conversionId"),
        "fileName": source.get("fileName") or "",
        "createdUtc": item.get("createdUtc"),
        "featureCount": item.get("featureCount"),
        "chunkCount": item.get("chunkCount"),
        "sourceType": source.get("sourceType"),
        "sourceReference": source.get("sourceReference"),
        "sourceSystem": source.get("sourceSystem"),
    }
