from __future__ import annotations

import unittest

from dwg_geojson.asset_classifier import classify_geojson_assets, validate_asset_rules


def _feature(*, layer: str = "", text: str = "", block_name: str = "") -> dict:
    properties = {"handle": "A1"}
    if layer:
        properties["layer"] = layer
    if text:
        properties["text"] = text
    if block_name:
        properties["blockName"] = block_name
    return {
        "type": "Feature",
        "geometry": {"type": "Point", "coordinates": [100.0, 200.0]},
        "properties": properties,
    }


class AssetClassifierTests(unittest.TestCase):
    def test_substation_has_precedence_over_station(self) -> None:
        collection = {"type": "FeatureCollection", "features": [_feature(text="Proposed Substation")]}

        counts = classify_geojson_assets(collection)

        self.assertEqual({"substation": 1}, counts)
        source, marker = collection["features"]
        self.assertEqual("substation", source["properties"]["asset"]["type"])
        self.assertEqual("low", marker["properties"]["asset"]["confidence"])
        self.assertTrue(marker["properties"]["asset"]["isMarker"])

    def test_block_evidence_creates_high_confidence_marker(self) -> None:
        collection = {"type": "FeatureCollection", "features": [_feature(block_name="PRI Substation")]}

        classify_geojson_assets(collection)

        marker = collection["features"][1]
        self.assertEqual("high", marker["properties"]["asset"]["confidence"])
        self.assertEqual("blockName", marker["properties"]["asset"]["detectionSource"])

    def test_custom_rules_are_validated_and_applied(self) -> None:
        rules = validate_asset_rules({"pole-cabinet": ["feeder pillar"]})
        collection = {"type": "FeatureCollection", "features": [_feature(layer="Feeder Pillar")]}

        counts = classify_geojson_assets(collection, rules)

        self.assertEqual({"pole-cabinet": 1}, counts)

    def test_unknown_rule_category_is_rejected(self) -> None:
        with self.assertRaisesRegex(ValueError, "Unsupported asset type"):
            validate_asset_rules({"unknown": ["value"]})
