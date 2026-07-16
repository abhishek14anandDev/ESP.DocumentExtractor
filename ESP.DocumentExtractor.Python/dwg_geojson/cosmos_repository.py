"""Azure Cosmos DB implementation for generated GeoJSON persistence."""

from __future__ import annotations

import logging
import os
from typing import Any

from .repository import PersistenceError
from .storage_models import GeoJsonChunk, StoredGeoJsonMetadata


DEFAULT_DATABASE_NAME = "esp-document-extractor"
DEFAULT_CONTAINER_NAME = "cad-geojson"
logger = logging.getLogger("dwg_geojson.cosmos_repository")


class CosmosGeoJsonRepository:
    """Stores conversion metadata and chunks in a Cosmos DB NoSQL container."""

    def __init__(
        self,
        connection_string: str,
        *,
        database_name: str = DEFAULT_DATABASE_NAME,
        container_name: str = DEFAULT_CONTAINER_NAME,
    ) -> None:
        if not connection_string:
            raise PersistenceError("COSMOS_CONNECTION_STRING is not configured.")
        self._connection_string = connection_string
        self._database_name = database_name
        self._container_name = container_name
        self._container: Any | None = None

    @classmethod
    def from_env(cls) -> "CosmosGeoJsonRepository":
        logger.info(
            "Loading Cosmos repository config from environment: "
            "connectionStringConfigured=%s database=%s container=%s",
            bool(os.environ.get("COSMOS_CONNECTION_STRING")),
            os.environ.get("COSMOS_DATABASE_NAME", DEFAULT_DATABASE_NAME),
            os.environ.get("COSMOS_CONTAINER_NAME", DEFAULT_CONTAINER_NAME),
        )
        return cls(
            os.environ.get("COSMOS_CONNECTION_STRING", ""),
            database_name=os.environ.get("COSMOS_DATABASE_NAME", DEFAULT_DATABASE_NAME),
            container_name=os.environ.get("COSMOS_CONTAINER_NAME", DEFAULT_CONTAINER_NAME),
        )

    @property
    def container_name(self) -> str:
        return self._container_name

    def ensure_ready(self) -> None:
        if self._container is not None:
            return

        logger.info(
            "Initializing Cosmos client/container: database=%s container=%s",
            self._database_name,
            self._container_name,
        )

        try:
            from azure.cosmos import CosmosClient, PartitionKey
        except ImportError as exc:
            logger.exception("Azure Cosmos SDK import failed")
            raise PersistenceError(
                "azure-cosmos is not installed. Run 'pip install -r requirements.txt'."
            ) from exc

        try:
            client = CosmosClient.from_connection_string(self._connection_string)
            database = client.create_database_if_not_exists(id=self._database_name)
            self._container = database.create_container_if_not_exists(
                id=self._container_name,
                partition_key=PartitionKey(path="/conversionId"),
                indexing_policy={
                    "indexingMode": "consistent",
                    "automatic": True,
                    "includedPaths": [{"path": "/*"}],
                    "excludedPaths": [{"path": "/features/*"}],
                },
            )
            logger.info(
                "Cosmos client/container ready: database=%s container=%s",
                self._database_name,
                self._container_name,
            )
        except Exception as exc:  # noqa: BLE001 - SDK exceptions vary by version.
            logger.exception(
                "Cosmos DB initialization failed: database=%s container=%s",
                self._database_name,
                self._container_name,
            )
            raise PersistenceError(f"Cosmos DB initialization failed: {exc}") from exc

    def upsert_metadata(self, metadata: StoredGeoJsonMetadata) -> None:
        logger.info(
            "Upserting Cosmos metadata: conversionId=%s chunkCount=%s featureCount=%s",
            metadata.conversion_id,
            metadata.chunk_count,
            metadata.feature_count,
        )
        self._upsert(metadata.to_item())

    def upsert_chunk(self, chunk: GeoJsonChunk) -> None:
        logger.info(
            "Upserting Cosmos chunk: conversionId=%s chunkIndex=%s chunkCount=%s features=%s",
            chunk.conversion_id,
            chunk.chunk_index,
            chunk.chunk_count,
            len(chunk.features),
        )
        self._upsert(chunk.to_item())

    def get_metadata(self, conversion_id: str) -> dict | None:
        self.ensure_ready()
        try:
            logger.info("Reading Cosmos metadata: conversionId=%s", conversion_id)
            return self._container.read_item(
                item=f"{conversion_id}:metadata",
                partition_key=conversion_id,
            )
        except Exception as exc:  # noqa: BLE001 - SDK exceptions vary by version.
            if _is_not_found(exc):
                logger.info("Cosmos metadata not found: conversionId=%s", conversion_id)
                return None
            logger.exception("Cosmos DB metadata read failed: conversionId=%s", conversion_id)
            raise PersistenceError(f"Cosmos DB metadata read failed: {exc}") from exc

    def get_chunks(self, conversion_id: str) -> list[dict]:
        self.ensure_ready()
        try:
            logger.info("Querying Cosmos chunks: conversionId=%s", conversion_id)
            chunks = self._container.query_items(
                query=(
                    "SELECT * FROM c WHERE c.conversionId = @conversionId "
                    "AND c.documentType = 'geoJsonChunk' ORDER BY c.chunkIndex"
                ),
                parameters=[{"name": "@conversionId", "value": conversion_id}],
                partition_key=conversion_id,
            )
            result = list(chunks)
            logger.info(
                "Cosmos chunks query completed: conversionId=%s chunks=%s",
                conversion_id,
                len(result),
            )
            return result
        except Exception as exc:  # noqa: BLE001 - SDK exceptions vary by version.
            logger.exception("Cosmos DB chunk read failed: conversionId=%s", conversion_id)
            raise PersistenceError(f"Cosmos DB chunk read failed: {exc}") from exc

    def list_metadata(self, limit: int) -> list[dict]:
        self.ensure_ready()
        try:
            logger.info("Querying Cosmos metadata list: limit=%s", limit)
            metadata_items = self._container.query_items(
                query=(
                    "SELECT c.conversionId, c.createdUtc, c.source, "
                    "c.featureCount, c.chunkCount FROM c "
                    "WHERE c.documentType = 'conversionMetadata' "
                    "ORDER BY c.createdUtc DESC OFFSET 0 LIMIT @limit"
                ),
                parameters=[{"name": "@limit", "value": limit}],
                enable_cross_partition_query=True,
            )
            result = list(metadata_items)
            logger.info("Cosmos metadata list query completed: returned=%s", len(result))
            return result
        except Exception as exc:  # noqa: BLE001 - SDK exceptions vary by version.
            logger.exception("Cosmos DB metadata list failed")
            raise PersistenceError(f"Cosmos DB metadata list failed: {exc}") from exc

    def _upsert(self, item: dict[str, Any]) -> None:
        self.ensure_ready()
        try:
            self._container.upsert_item(item)
        except Exception as exc:  # noqa: BLE001 - SDK exceptions vary by version.
            logger.exception(
                "Cosmos DB upsert failed: conversionId=%s documentType=%s id=%s",
                item.get("conversionId"),
                item.get("documentType"),
                item.get("id"),
            )
            raise PersistenceError(f"Cosmos DB upsert failed: {exc}") from exc


def _is_not_found(exc: Exception) -> bool:
    status_code = getattr(exc, "status_code", None)
    if status_code == 404:
        return True
    return exc.__class__.__name__ == "CosmosResourceNotFoundError"
