"""Persistence repository port for generated GeoJSON."""

from __future__ import annotations

from typing import Protocol

from .storage_models import GeoJsonChunk, StoredGeoJsonMetadata


class PersistenceError(Exception):
    """Raised when generated GeoJSON cannot be persisted."""


class GeoJsonRepository(Protocol):
    """Repository abstraction used by the function layer."""

    @property
    def container_name(self) -> str:
        """Cosmos container name used for metadata and chunks."""

    def ensure_ready(self) -> None:
        """Create or connect to required persistence resources."""

    def upsert_metadata(self, metadata: StoredGeoJsonMetadata) -> None:
        """Persist conversion metadata."""

    def upsert_chunk(self, chunk: GeoJsonChunk) -> None:
        """Persist one ordered GeoJSON feature chunk."""
