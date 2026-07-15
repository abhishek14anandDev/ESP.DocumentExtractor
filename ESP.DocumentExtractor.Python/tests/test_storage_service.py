from __future__ import annotations

import json
import logging
import unittest

import function_app
from dwg_geojson.converter import ConversionStats
from dwg_geojson.repository import PersistenceError, StoredGeoJsonNotFoundError
from dwg_geojson.retrieval_service import GeoJsonRetrievalService
from dwg_geojson.storage_models import SourceInfo
from dwg_geojson.storage_service import GeoJsonStorageService


class FakeRepository:
    container_name = "cad-geojson-test"

    def __init__(self) -> None:
        self.ready = False
        self.metadata = None
        self.chunks = []

    def ensure_ready(self) -> None:
        self.ready = True

    def upsert_metadata(self, metadata) -> None:
        self.metadata = metadata

    def upsert_chunk(self, chunk) -> None:
        self.chunks.append(chunk)

    def get_metadata(self, conversion_id: str):
        if self.metadata is None or self.metadata.conversion_id != conversion_id:
            return None
        return self.metadata.to_item()

    def get_chunks(self, conversion_id: str):
        return [
            chunk.to_item()
            for chunk in self.chunks
            if chunk.conversion_id == conversion_id
        ]


class FailingRepository(FakeRepository):
    def upsert_metadata(self, metadata) -> None:
        raise PersistenceError("cosmos unavailable")


def _feature(payload: str = "x"):
    return {
        "type": "Feature",
        "geometry": {"type": "Point", "coordinates": [1, 2]},
        "properties": {"payload": payload},
    }


def _stats(feature_count: int = 1):
    return ConversionStats(
        feature_count=feature_count,
        entity_counts={"POINT": feature_count},
        converter="test",
        source_epsg=27700,
        reprojected=True,
        filtered_out=0,
    )


class GeoJsonStorageServiceTests(unittest.TestCase):
    def test_small_geojson_creates_one_chunk(self) -> None:
        repo = FakeRepository()
        service = GeoJsonStorageService(repo, target_chunk_bytes=10_000)
        geojson = {"type": "FeatureCollection", "features": [_feature(), _feature()]}

        metadata = service.store(
            conversion_id="conversion-1",
            correlation_id="correlation-1",
            geojson=geojson,
            stats=_stats(2),
            source=SourceInfo(source_type="test", file_name="drawing.dwg"),
            request_options={"reproject_to_wgs84": True},
        )

        self.assertTrue(repo.ready)
        self.assertEqual(metadata.chunk_count, 1)
        self.assertEqual(repo.metadata.conversion_id, "conversion-1")
        self.assertEqual(len(repo.chunks), 1)
        self.assertEqual(repo.chunks[0].features, geojson["features"])

    def test_large_geojson_creates_ordered_chunks(self) -> None:
        repo = FakeRepository()
        service = GeoJsonStorageService(repo, target_chunk_bytes=250)
        features = [_feature("x" * 180), _feature("y" * 180), _feature("z" * 180)]
        geojson = {"type": "FeatureCollection", "features": features}

        metadata = service.store(
            conversion_id="conversion-2",
            correlation_id="correlation-2",
            geojson=geojson,
            stats=_stats(3),
            source=SourceInfo(source_type="test", file_name="large.dxf"),
            request_options={},
        )

        self.assertGreater(metadata.chunk_count, 1)
        self.assertEqual([chunk.chunk_index for chunk in repo.chunks], list(range(metadata.chunk_count)))
        self.assertEqual(sum(len(chunk.features) for chunk in repo.chunks), len(features))

    def test_oversized_single_feature_fails_clearly(self) -> None:
        repo = FakeRepository()
        service = GeoJsonStorageService(repo, target_chunk_bytes=100, max_chunk_bytes=200)
        geojson = {"type": "FeatureCollection", "features": [_feature("x" * 500)]}

        with self.assertRaisesRegex(PersistenceError, "single GeoJSON feature"):
            service.store(
                conversion_id="conversion-3",
                correlation_id="correlation-3",
                geojson=geojson,
                stats=_stats(1),
                source=SourceInfo(source_type="test", file_name="huge.dwg"),
                request_options={},
            )


class GeoJsonRetrievalServiceTests(unittest.TestCase):
    def test_get_rebuilds_geojson_with_metadata(self) -> None:
        repo = FakeRepository()
        storage = GeoJsonStorageService(repo, target_chunk_bytes=250)
        features = [_feature("x" * 180), _feature("y" * 180)]
        storage.store(
            conversion_id="conversion-4",
            correlation_id="correlation-4",
            geojson={"type": "FeatureCollection", "features": features},
            stats=_stats(2),
            source=SourceInfo(source_type="test", file_name="drawing.dwg"),
            request_options={},
        )

        result = GeoJsonRetrievalService(repo).get("conversion-4")

        self.assertEqual(result["conversionId"], "conversion-4")
        self.assertEqual(result["metadata"]["documentType"], "conversionMetadata")
        self.assertEqual(result["geojson"]["features"], features)

    def test_get_missing_conversion_returns_not_found(self) -> None:
        with self.assertRaises(StoredGeoJsonNotFoundError):
            GeoJsonRetrievalService(FakeRepository()).get("missing")

    def test_get_incomplete_conversion_fails(self) -> None:
        repo = FakeRepository()
        storage = GeoJsonStorageService(repo)
        storage.store(
            conversion_id="conversion-5",
            correlation_id="correlation-5",
            geojson={"type": "FeatureCollection", "features": [_feature()]},
            stats=_stats(1),
            source=SourceInfo(source_type="test", file_name="drawing.dwg"),
            request_options={},
        )
        repo.chunks.clear()

        with self.assertRaisesRegex(PersistenceError, "incomplete"):
            GeoJsonRetrievalService(repo).get("conversion-5")


class FunctionAppPersistenceTests(unittest.TestCase):
    def setUp(self) -> None:
        self._original_convert_request = function_app._convert_request
        self._original_repository = function_app._geojson_repository
        self._original_storage_service = function_app._storage_service
        self._original_retrieval_service = function_app._retrieval_service
        self._logging_disable_level = logging.root.manager.disable
        logging.disable(logging.CRITICAL)

    def tearDown(self) -> None:
        function_app._convert_request = self._original_convert_request
        function_app._geojson_repository = self._original_repository
        function_app._storage_service = self._original_storage_service
        function_app._retrieval_service = self._original_retrieval_service
        logging.disable(self._logging_disable_level)

    def test_source_info_prefers_body_then_headers_then_query(self) -> None:
        req = _FakeRequest(
            headers={"x-source-system": "HeaderSystem", "x-source-reference": "HeaderRef"},
            params={"sourceSystem": "QuerySystem", "sourceReference": "QueryRef"},
        )

        source = function_app._source_info(
            req,
            body={"sourceSystem": "BodySystem", "sourceReference": "BodyRef"},
            source_type="local-file-path",
            file_name="drawing.dwg",
            source_reference="C:/input/drawing.dwg",
            content_type="application/json",
            source_hash="abc",
        )

        self.assertEqual(source.source_system, "BodySystem")
        self.assertEqual(source.source_reference, "BodyRef")
        self.assertEqual(source.file_name, "drawing.dwg")
        self.assertEqual(source.source_hash, "abc")

    def test_successful_save_preserves_geojson_response_and_adds_headers(self) -> None:
        repo = FakeRepository()
        function_app._storage_service = GeoJsonStorageService(repo)
        geojson = {"type": "FeatureCollection", "features": [_feature()]}

        function_app._convert_request = lambda req: function_app.ConvertedRequest(
            geojson=geojson,
            stats=_stats(1),
            source_info=SourceInfo(source_type="test", file_name="drawing.dwg"),
            request_options={},
        )

        response = function_app.cad_geojson(_FakeRequest())

        self.assertEqual(response.status_code, 200)
        self.assertEqual(json.loads(response.get_body()), geojson)
        self.assertEqual(response.headers["x-cosmos-container"], "cad-geojson-test")
        self.assertEqual(response.headers["x-cosmos-chunk-count"], "1")
        self.assertTrue(response.headers["x-cosmos-conversion-id"])

    def test_persistence_failure_returns_error(self) -> None:
        function_app._storage_service = GeoJsonStorageService(FailingRepository())
        function_app._convert_request = lambda req: function_app.ConvertedRequest(
            geojson={"type": "FeatureCollection", "features": [_feature()]},
            stats=_stats(1),
            source_info=SourceInfo(source_type="test", file_name="drawing.dwg"),
            request_options={},
        )

        response = function_app.cad_geojson(_FakeRequest())
        body = json.loads(response.get_body())

        self.assertEqual(response.status_code, 500)
        self.assertEqual(body["error"], "cad.persistence_failed")

    def test_get_returns_metadata_envelope_from_cosmos(self) -> None:
        repo = FakeRepository()
        storage = GeoJsonStorageService(repo)
        features = [_feature()]
        storage.store(
            conversion_id="conversion-6",
            correlation_id="correlation-6",
            geojson={"type": "FeatureCollection", "features": features},
            stats=_stats(1),
            source=SourceInfo(source_type="test", file_name="drawing.dwg"),
            request_options={},
        )
        function_app._retrieval_service = GeoJsonRetrievalService(repo)

        response = function_app.get_cad_geojson(
            _FakeRequest(route_params={"conversion_id": "conversion-6"})
        )
        body = json.loads(response.get_body())

        self.assertEqual(response.status_code, 200)
        self.assertEqual(body["conversionId"], "conversion-6")
        self.assertEqual(body["geojson"]["features"], features)
        self.assertEqual(response.headers["x-cosmos-chunk-count"], "1")

    def test_get_can_return_raw_geojson(self) -> None:
        repo = FakeRepository()
        storage = GeoJsonStorageService(repo)
        geojson = {"type": "FeatureCollection", "features": [_feature()]}
        storage.store(
            conversion_id="conversion-7",
            correlation_id="correlation-7",
            geojson=geojson,
            stats=_stats(1),
            source=SourceInfo(source_type="test", file_name="drawing.dwg"),
            request_options={},
        )
        function_app._retrieval_service = GeoJsonRetrievalService(repo)

        response = function_app.get_cad_geojson(
            _FakeRequest(
                params={"format": "geojson"},
                route_params={"conversion_id": "conversion-7"},
            )
        )

        self.assertEqual(response.status_code, 200)
        self.assertEqual(response.mimetype, "application/geo+json")
        self.assertEqual(json.loads(response.get_body()), geojson)

    def test_get_missing_conversion_returns_404(self) -> None:
        function_app._retrieval_service = GeoJsonRetrievalService(FakeRepository())

        response = function_app.get_cad_geojson(
            _FakeRequest(route_params={"conversion_id": "missing"})
        )
        body = json.loads(response.get_body())

        self.assertEqual(response.status_code, 404)
        self.assertEqual(body["error"], "cad.not_found")


class _FakeRequest:
    def __init__(self, headers=None, params=None, route_params=None) -> None:
        self.headers = headers or {}
        self.params = params or {}
        self.route_params = route_params or {}


if __name__ == "__main__":
    unittest.main()
