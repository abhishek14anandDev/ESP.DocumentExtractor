# MapPlotter

React app for plotting stored CAD GeoJSON conversions on Google Maps.

## Setup

```powershell
npm install
Copy-Item .env.example .env
```

Set `VITE_GOOGLE_MAPS_API_KEY` in `.env`.

## Run

Start the Python Azure Functions API first:

```powershell
cd ..\ESP.DocumentExtractor.Python
func start
```

Then run the app:

```powershell
cd ..\MapPlotter
npm run dev
```

The app reads conversion metadata from `GET /api/cad/geojson` and loads a selected
conversion envelope from `GET /api/cad/geojson/{conversionId}`.

## Detected CAD assets

Conversions can contain generated point markers for detected `Substation`,
`Station`, `Cable route`, `Joint / termination`, and `Pole / cabinet` assets.
They render above the original CAD geometry in category-specific colors. Hover a
marker to see its category, confidence, matched CAD evidence, coordinates, and
source metadata. Low-confidence markers originate from text-only matching and
should be reviewed against the drawing.

## Curated drawing content and map pins

After plotting a drawing, the sidebar contains a JSON editor for curated drawing
facts: title-block details, cable lengths, layouts, stored blocks, caveats, and
annotations. Save it to persist the separate Cosmos analysis item.

For point annotations, choose a category, enter a title/optional notes, select
**Place point pin**, then click the map. The pin is added to the editor; select
**Save drawing content** to persist it. Route and boundary annotations use the
same JSON editor and must follow the API geometry rules documented in the Python
project README.

The original CAD GeoJSON is never changed by this workflow. The curated
annotations render in a distinct orange overlay above it.
