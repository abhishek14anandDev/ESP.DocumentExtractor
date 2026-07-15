# ESP.DocumentExtractor.Python

A standalone **Azure Function (Python v2)** that reads AutoCAD **DWG/DXF** files
and returns their geometry as **GeoJSON**.

This project is independent of the .NET solution in the repository root.

## How it works

DWG is a proprietary binary format that pure-Python libraries cannot parse, so
the converter uses external tooling and picks the best path automatically:

| Input | Pipeline | Tool |
| ----- | -------- | ---- |
| `.dwg` (preferred) | native GeoJSON export | LibreDWG `dwgread -O GeoJSON` |
| `.dwg` (fallback)  | DWG → DXF → GeoJSON | LibreDWG `dwg2dxf` or ODA File Converter, then `ezdxf` |
| `.dxf`             | DXF → GeoJSON | `ezdxf` |

Each CAD entity becomes a GeoJSON `Feature`:

- `LINE`, `ARC`, `LWPOLYLINE`/`POLYLINE` (open), `SPLINE`, `ELLIPSE` → `LineString`
- closed polylines, `CIRCLE` → `Polygon`
- `POINT`, `TEXT`/`MTEXT` → `Point`

Properties are normalized to `entityType`, `layer`, `handle`, `color` (and
`text` where available). Coordinates are rounded to 6 decimals.

> Note: DWG/DXF coordinates are in the drawing's own units, not WGS84
> longitude/latitude. The output is valid GeoJSON geometry but is not
> geo-referenced unless the source drawing already uses real-world coordinates.

## Prerequisites

- Python 3.9–3.11
- [Azure Functions Core Tools v4](https://learn.microsoft.com/azure/azure-functions/functions-run-local) (to run the function locally)
- **LibreDWG** (required for `.dwg` input):

```bash
# macOS
brew install libredwg
# Debian/Ubuntu
sudo apt-get install -y libredwg0 libredwg-tools
```

## Setup

```bash
cd ESP.DocumentExtractor.Python
python3 -m venv .venv
source .venv/bin/activate
pip install -r requirements.txt
```

### Cosmos DB persistence

Every successful HTTP conversion is persisted to Azure Cosmos DB for NoSQL as
one metadata item plus ordered feature chunk items. This avoids the Cosmos DB
2 MB item limit for large GeoJSON outputs.

Configure these settings in Azure Function App settings, or in the ignored
`local.settings.json` for local runs:

| Setting | Default | Purpose |
| ------- | ------- | ------- |
| `COSMOS_CONNECTION_STRING` | none | Cosmos DB connection string. Required for successful conversions. |
| `COSMOS_DATABASE_NAME` | `esp-document-extractor` | Database created/read by the function. |
| `COSMOS_CONTAINER_NAME` | `cad-geojson` | Container for conversion metadata and chunks. |

Use `local.settings.sample.json` as the local template. Do not commit real
Cosmos keys. If a key was shared in chat or email, rotate it before using it in
the function app.

## Quick test (CLI)

Convert a DWG file to GeoJSON without running the function host:

```bash
# Raw drawing coordinates (all ~172,000 features)
python cli.py "/path/to/drawing.dwg" -o output.geojson

# WGS84 lon/lat for web maps, with junk filtered out (recommended for mapping)
python cli.py "/path/to/drawing.dwg" --wgs84 --filter -o network.wgs84.geojson
```

The provided sample (`P2122392-041-Rev4-HV As Laid.dwg`) yields ~172,000 raw
features; with `--wgs84 --filter` it reduces to ~42,500 real network features.

## Mapping to Google Maps

CAD coordinates are **not** longitude/latitude, so they must be reprojected
before they can go on a web map. This drawing family is georeferenced in
**British National Grid (EPSG:27700)**; the converter reprojects to **WGS84
(EPSG:4326)** with `pyproj`.

CLI / API flags:

| Flag | Query param | Effect |
| ---- | ----------- | ------ |
| `--wgs84` | `?reproject=true` | reproject source coords → WGS84 lon/lat |
| `--source-epsg N` | `?sourceEpsg=N` | source CRS (default `27700`) |
| `--filter` | `?filter=true` | drop title-block/annotation/outliers, keep the network cluster |

Filtering is two-stage: a coarse British-National-Grid bbox removes geometry
drawn near the local origin and gross outliers, then anything farther than
~25 km from the robust (median) data centre is dropped, leaving only the
real network.

> The function endpoint defaults to `reproject=true&filter=true` (map-ready
> output). Pass `?reproject=false` for raw drawing coordinates.

### Viewer (deck.gl over Google Maps)

`viewer.html` overlays the WGS84 GeoJSON on a Google Maps base using
[deck.gl](https://deck.gl/) `GoogleMapsOverlay`, which renders 100k+ features on
the GPU (the native Google Maps Data layer cannot).

1. **Get a Google Maps JavaScript API key:**
   - [Google Cloud Console](https://console.cloud.google.com/) → create/select a project
   - Enable **Maps JavaScript API** (APIs & Services → Library)
   - Credentials → **Create credentials → API key**
   - Restrict it: HTTP referrers `http://localhost:*`, API = *Maps JavaScript API*
   - Enable billing (large free monthly map-load allowance)
2. Open `viewer.html`, set `GOOGLE_MAPS_API_KEY` near the top.
3. Open the page in a browser and pick your `network.wgs84.geojson` via the file
   picker (no server needed). Features are colored by layer with hover tooltips.

A ready-made sample is generated at `sample_output/network.wgs84.geojson`.

## Run the Azure Function locally

```bash
func start
```

### Endpoint

`POST http://localhost:7071/api/cad/geojson`

**Option 1 — upload a file (multipart/form-data):**

```bash
curl -X POST http://localhost:7071/api/cad/geojson \
  -F "file=@/path/to/drawing.dwg" \
  -o output.geojson
```

**Option 2 — raw binary body:**

```bash
curl -X POST http://localhost:7071/api/cad/geojson \
  -H "Content-Type: application/octet-stream" \
  -H "x-file-name: drawing.dwg" \
  --data-binary @/path/to/drawing.dwg \
  -o output.geojson
```

**Option 3 — local file path (JSON), for files on the host:**

```bash
curl -X POST http://localhost:7071/api/cad/geojson \
  -H "Content-Type: application/json" \
  -d '{"filePath": "/abs/path/to/drawing.dwg"}' \
  -o output.geojson
```

### Response

- `200 OK`, body = GeoJSON `FeatureCollection` (`application/geo+json`)
- Headers: `x-correlation-id`, `x-conversion-converter`, `x-conversion-feature-count`,
  `x-cosmos-conversion-id`, `x-cosmos-container`, `x-cosmos-chunk-count`
- `400` / `500` with a JSON error body `{ correlationId, error, message }`

Optional source metadata can be passed without changing the CAD payload:

- JSON body: `sourceSystem`, `sourceReference`
- Headers: `x-source-system`, `x-source-reference`
- Query string fallback: `?sourceSystem=...&sourceReference=...`

The persisted metadata records the file name, source type, source reference,
source system, input SHA-256 hash, conversion diagnostics, output SHA-256 hash,
and chunk count.

Example Cosmos query for a file/source:

```sql
SELECT c.conversionId, c.createdUtc, c.source.fileName, c.source.sourceSystem,
       c.featureCount, c.chunkCount
FROM c
WHERE c.documentType = "conversionMetadata"
  AND c.source.fileName = "drawing.dwg"
```

### Read persisted GeoJSON

List stored conversions from Cosmos metadata:

```bash
curl "http://localhost:7071/api/cad/geojson?limit=100"
```

The response is a compact array for UI list screens:

```json
[
  {
    "conversionId": "<conversion-id>",
    "fileName": "drawing.dwg",
    "createdUtc": "2026-07-15T00:00:00+00:00",
    "featureCount": 42500,
    "chunkCount": 48,
    "sourceType": "multipart-upload",
    "sourceReference": null,
    "sourceSystem": null
  }
]
```

Use the Cosmos conversion id returned by `x-cosmos-conversion-id` after a
successful POST:

```bash
curl http://localhost:7071/api/cad/geojson/<conversion-id> -o stored.json
```

Default response:

```json
{
  "conversionId": "<conversion-id>",
  "metadata": { "...": "conversion metadata from Cosmos" },
  "geojson": {
    "type": "FeatureCollection",
    "features": []
  }
}
```

To return only the reconstructed GeoJSON FeatureCollection:

```bash
curl "http://localhost:7071/api/cad/geojson/<conversion-id>?format=geojson" -o output.geojson
```

Read response headers include `x-correlation-id`, `x-cosmos-conversion-id`,
`x-cosmos-container`, `x-cosmos-chunk-count`, and `x-conversion-feature-count`.

## Deployment notes

The Azure Functions Linux Python host does **not** include LibreDWG. To process
`.dwg` files in the cloud, either:

1. Deploy in a **custom container** (recommended) with LibreDWG installed:

```dockerfile
FROM mcr.microsoft.com/azure-functions/python:4-python3.11
RUN apt-get update && apt-get install -y libredwg0 libredwg-tools && rm -rf /var/lib/apt/lists/*
COPY requirements.txt /
RUN pip install -r /requirements.txt
COPY . /home/site/wwwroot
ENV AzureWebJobsScriptRoot=/home/site/wwwroot AzureFunctionsJobHost__Logging__Console__IsEnabled=true
```

2. Or restrict the deployed function to `.dxf` input (no external binary needed).

3. Add the Cosmos settings as Function App configuration values. The connection
   string must not be stored in source control.

## Project layout

```
ESP.DocumentExtractor.Python/
|-- function_app.py              # Azure Functions v2 HTTP app
|-- dwg_geojson/
|   |-- converter.py             # DWG/DXF to GeoJSON conversion
|   |-- storage_service.py       # metadata/chunk persistence service
|   |-- retrieval_service.py     # metadata/chunk read and reconstruction service
|   |-- storage_models.py        # persistence domain models
|   |-- repository.py            # persistence repository port
|   `-- cosmos_repository.py     # Cosmos DB repository adapter
|-- cli.py                       # Local command-line converter
|-- viewer.html                  # deck.gl-over-Google-Maps viewer
|-- Dockerfile                   # Custom Functions image bundling LibreDWG
|-- requirements.txt
|-- host.json
|-- local.settings.sample.json   # placeholder local settings
|-- local.settings.json          # ignored local secrets
`-- README.md
```
