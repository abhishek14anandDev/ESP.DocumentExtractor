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

The app reads conversion metadata from `GET /api/cad/geojson` and plots selected stored GeoJSON from `GET /api/cad/geojson/{conversionId}?format=geojson`.
