"""Command-line DWG/DXF -> GeoJSON converter.

Usage:
    python cli.py <input.dwg|input.dxf> [-o output.geojson]

Prints conversion stats to stderr and writes the GeoJSON FeatureCollection to
the output file (or stdout if -o is omitted).
"""

from __future__ import annotations

import argparse
import json
import sys

from dwg_geojson import ConversionError, convert_file_to_geojson


def main() -> int:
    parser = argparse.ArgumentParser(description="Convert a DWG/DXF file to GeoJSON.")
    parser.add_argument("input", help="Path to the .dwg or .dxf file")
    parser.add_argument("-o", "--output", help="Output .geojson path (default: stdout)")
    parser.add_argument(
        "--indent", type=int, default=None, help="Pretty-print indent (default: compact)"
    )
    parser.add_argument(
        "--wgs84",
        action="store_true",
        help="Reproject to WGS84 lon/lat (EPSG:4326) for web/Google Maps.",
    )
    parser.add_argument(
        "--source-epsg",
        type=int,
        default=None,
        help="Source CRS EPSG code (default 27700 / British National Grid when --wgs84).",
    )
    parser.add_argument(
        "--filter",
        action="store_true",
        help="Drop features outside the plausible source-CRS extent (title block, outliers).",
    )
    args = parser.parse_args()

    try:
        geojson, stats = convert_file_to_geojson(
            args.input,
            source_epsg=args.source_epsg,
            reproject_to_wgs84=args.wgs84,
            filter_to_source_bbox=args.filter,
        )
    except ConversionError as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        return 1

    print(
        f"converter={stats.converter} features={stats.feature_count} "
        f"reprojected={stats.reprojected} source_epsg={stats.source_epsg} "
        f"filtered_out={stats.filtered_out}",
        file=sys.stderr,
    )
    print(f"entity_counts={json.dumps(stats.entity_counts)}", file=sys.stderr)
    if stats.skipped:
        print(f"skipped={json.dumps(stats.skipped)}", file=sys.stderr)

    text = json.dumps(geojson, indent=args.indent)
    if args.output:
        with open(args.output, "w", encoding="utf-8") as handle:
            handle.write(text)
        print(f"Wrote {args.output}", file=sys.stderr)
    else:
        print(text)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
