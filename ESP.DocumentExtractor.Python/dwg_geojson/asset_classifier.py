"""Rule-based semantic asset detection for converted CAD GeoJSON."""

from __future__ import annotations

import hashlib
import re
from collections import Counter
from typing import Any


ASSET_TYPES = (
    "substation",
    "station",
    "cable-route",
    "joint-termination",
    "pole-cabinet",
)

# Longest/specific terms take precedence over their more general counterparts.
DEFAULT_ASSET_RULES: dict[str, tuple[str, ...]] = {
    "substation": ("substation", "sub station", "primary substation", "pri"),
    "station": ("station",),
    "cable-route": ("cable route", "cable", "hv route", "lv route"),
    "joint-termination": ("joint", "termination", "terminate"),
    "pole-cabinet": ("pole", "cabinet", "kiosk"),
}


def validate_asset_rules(value: Any) -> dict[str, tuple[str, ...]]:
    """Validate optional caller-supplied category keywords."""
    if value is None:
        return DEFAULT_ASSET_RULES
    if not isinstance(value, dict):
        raise ValueError("'assetRules' must be an object mapping asset types to keyword arrays.")

    rules = dict(DEFAULT_ASSET_RULES)
    for asset_type, keywords in value.items():
        if asset_type not in ASSET_TYPES:
            raise ValueError(f"Unsupported asset type '{asset_type}'.")
        if not isinstance(keywords, list) or not keywords:
            raise ValueError(f"'assetRules.{asset_type}' must be a non-empty array of keywords.")
        if len(keywords) > 50:
            raise ValueError(f"'assetRules.{asset_type}' cannot contain more than 50 keywords.")
        normalized = []
        for keyword in keywords:
            text = str(keyword).strip()
            if not text or len(text) > 100:
                raise ValueError("Asset rule keywords must contain 1 to 100 characters.")
            normalized.append(text)
        rules[asset_type] = tuple(normalized)

    return rules


def classify_geojson_assets(
    feature_collection: dict[str, Any],
    rules: dict[str, tuple[str, ...]] | None = None,
) -> dict[str, int]:
    """Append one point marker per detected CAD asset and return category counts.

    The original CAD feature is retained and linked to the generated marker by
    ``assetId``. Text-only evidence is deliberately emitted as low confidence
    for user review; block and layer evidence are more reliable.
    """
    effective_rules = rules or DEFAULT_ASSET_RULES
    features = feature_collection.get("features")
    if not isinstance(features, list):
        return {}

    markers: list[dict[str, Any]] = []
    counts: Counter[str] = Counter()
    for index, feature in enumerate(features):
        properties = feature.get("properties")
        geometry = feature.get("geometry")
        if not isinstance(properties, dict) or not isinstance(geometry, dict):
            continue

        match = _find_match(properties, effective_rules)
        coordinate = _representative_coordinate(geometry.get("coordinates"))
        if match is None or coordinate is None:
            continue

        asset_type, source, evidence = match
        asset_id = _asset_id(index, properties, asset_type, coordinate)
        confidence = {"blockName": "high", "layer": "medium", "text": "low"}[source]
        asset = {
            "id": asset_id,
            "type": asset_type,
            "confidence": confidence,
            "detectionSource": source,
            "evidence": evidence,
            "isMarker": False,
        }
        properties["asset"] = asset
        markers.append(
            {
                "type": "Feature",
                "geometry": {"type": "Point", "coordinates": coordinate},
                "properties": {
                    "entityType": "DETECTED_ASSET",
                    "layer": "Detected CAD assets",
                    "asset": {**asset, "isMarker": True},
                    "sourceCadHandle": properties.get("handle"),
                    "sourceCadLayer": properties.get("layer"),
                },
            }
        )
        counts[asset_type] += 1

    features.extend(markers)
    return dict(counts)


def _find_match(
    properties: dict[str, Any], rules: dict[str, tuple[str, ...]]
) -> tuple[str, str, str] | None:
    fields = (
        ("blockName", properties.get("blockName")),
        ("layer", properties.get("layer")),
        ("text", properties.get("text")),
    )
    for asset_type in ASSET_TYPES:
        for source, value in fields:
            text = str(value or "").strip()
            if not text:
                continue
            for keyword in rules.get(asset_type, ()):
                if _contains_keyword(text, keyword):
                    return asset_type, source, text
    return None


def _contains_keyword(value: str, keyword: str) -> bool:
    return bool(re.search(rf"(?<!\w){re.escape(keyword)}(?!\w)", value, re.IGNORECASE))


def _representative_coordinate(value: Any) -> list[float] | None:
    if isinstance(value, list):
        if len(value) >= 2 and isinstance(value[0], (int, float)) and isinstance(value[1], (int, float)):
            return [float(value[0]), float(value[1])]
        for child in value:
            coordinate = _representative_coordinate(child)
            if coordinate is not None:
                return coordinate
    return None


def _asset_id(index: int, properties: dict[str, Any], asset_type: str, coordinate: list[float]) -> str:
    identity = f"{index}|{properties.get('handle', '')}|{asset_type}|{coordinate[0]}|{coordinate[1]}"
    return f"asset-{hashlib.sha1(identity.encode('utf-8')).hexdigest()[:16]}"