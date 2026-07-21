import { GoogleMapsOverlay } from "@deck.gl/google-maps";
import { GeoJsonLayer } from "@deck.gl/layers";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";

const API_BASE_URL = import.meta.env.VITE_API_BASE_URL;
const GOOGLE_MAPS_API_KEY = import.meta.env.VITE_GOOGLE_MAPS_API_KEY || "";
const DEFAULT_CENTER = { lat: 52.049, lng: -0.712 };
const CAD_VIEW_MAX_METERS = 900;
const ANNOTATION_CATEGORIES = [
  "primary-substation",
  "cable-route-segment",
  "road-footway-crossing",
  "directional-drilling",
  "utility-service-route",
  "commercial-boundary",
  "custom",
];

const layerColors = new Map();

function colorForLayer(name) {
  const key = name || "(none)";
  if (layerColors.has(key)) {
    return layerColors.get(key);
  }

  let hash = 0;
  for (let index = 0; index < key.length; index += 1) {
    hash = (hash * 31 + key.charCodeAt(index)) >>> 0;
  }

  const color = hslToRgb((hash % 360) / 360, 0.62, 0.52);
  layerColors.set(key, color);
  return color;
}

function hslToRgb(h, s, l) {
  if (s === 0) {
    const value = Math.round(l * 255);
    return [value, value, value];
  }

  const hueToRgb = (p, q, t) => {
    let adjusted = t;
    if (adjusted < 0) adjusted += 1;
    if (adjusted > 1) adjusted -= 1;
    if (adjusted < 1 / 6) return p + (q - p) * 6 * adjusted;
    if (adjusted < 1 / 2) return q;
    if (adjusted < 2 / 3) return p + (q - p) * (2 / 3 - adjusted) * 6;
    return p;
  };

  const q = l < 0.5 ? l * (1 + s) : l + s - l * s;
  const p = 2 * l - q;
  return [
    Math.round(hueToRgb(p, q, h + 1 / 3) * 255),
    Math.round(hueToRgb(p, q, h) * 255),
    Math.round(hueToRgb(p, q, h - 1 / 3) * 255),
  ];
}

function App() {
  const mapElementRef = useRef(null);
  const mapRef = useRef(null);
  const overlayRef = useRef(null);
  const [mapsReady, setMapsReady] = useState(false);
  const [conversions, setConversions] = useState([]);
  const [listStatus, setListStatus] = useState("loading");
  const [listError, setListError] = useState("");
  const [plotStatus, setPlotStatus] = useState("idle");
  const [plotError, setPlotError] = useState("");
  const [selected, setSelected] = useState(null);
  const [geojson, setGeojson] = useState(null);
  const [coordinateMode, setCoordinateMode] = useState("none");
  const [tooltip, setTooltip] = useState(null);
  const [lineWidth, setLineWidth] = useState(2);
  const [pointRadius, setPointRadius] = useState(3);
  const [opacity, setOpacity] = useState(0.9);
  const [analysis, setAnalysis] = useState(null);
  const [analysisText, setAnalysisText] = useState("");
  const [analysisStatus, setAnalysisStatus] = useState("idle");
  const [analysisError, setAnalysisError] = useState("");
  const [annotationCategory, setAnnotationCategory] = useState("primary-substation");
  const [annotationTitle, setAnnotationTitle] = useState("");
  const [annotationNotes, setAnnotationNotes] = useState("");
  const [pinMode, setPinMode] = useState(false);

  const loadConversions = useCallback(async () => {
    setListStatus("loading");
    setListError("");
    try {
      const response = await fetch(`${API_BASE_URL}/cad/geojson?limit=100`);
      if (!response.ok) {
        throw new Error(await readError(response));
      }
      setConversions(await response.json());
      setListStatus("ready");
    } catch (error) {
      setListError(error.message);
      setListStatus("error");
    }
  }, []);

  useEffect(() => {
    loadConversions();
  }, [loadConversions]);

  useEffect(() => {
    if (!GOOGLE_MAPS_API_KEY) {
      return;
    }

    if (window.google?.maps) {
      setMapsReady(true);
      return;
    }

    const existingScript = document.querySelector("script[data-mapplotter-google-maps]");
    if (existingScript) {
      existingScript.addEventListener("load", () => setMapsReady(true), { once: true });
      return;
    }

    const script = document.createElement("script");
    script.dataset.mapplotterGoogleMaps = "true";
    script.src = `https://maps.googleapis.com/maps/api/js?key=${GOOGLE_MAPS_API_KEY}&v=weekly`; 
    script.async = true;
    script.onload = () => setMapsReady(true);
    script.onerror = () => setPlotError("Google Maps failed to load. Check the API key and network access.");
    document.head.appendChild(script);
  }, []);

  useEffect(() => {
    if (!mapsReady || mapRef.current || !mapElementRef.current) {
      return;
    }

    mapRef.current = new window.google.maps.Map(mapElementRef.current, {
      center: DEFAULT_CENTER,
      zoom: 13,
      mapTypeId: "hybrid",
      tilt: 0,
      streetViewControl: false,
      fullscreenControl: false,
      mapTypeControl: true,
    });

    overlayRef.current = new GoogleMapsOverlay({ layers: [] });
    overlayRef.current.setMap(mapRef.current);
  }, [mapsReady]);

  const summary = useMemo(() => summarizeGeojson(geojson), [geojson]);

  const refreshLayer = useCallback(() => {
    if (!overlayRef.current) {
      return;
    }

    const alpha = Math.round(opacity * 255);
    const layers = geojson
      ? [
          new GeoJsonLayer({
            id: "stored-cad-geojson",
            data: geojson,
            pickable: true,
            stroked: true,
            filled: true,
            pointType: "circle",
            getLineColor: (feature) => [...colorForLayer(feature.properties?.layer), alpha],
            getFillColor: (feature) => [...colorForLayer(feature.properties?.layer), Math.round(alpha * 0.55)],
            getLineWidth: lineWidth,
            lineWidthUnits: "pixels",
            getPointRadius: pointRadius,
            pointRadiusUnits: "pixels",
            onHover: (info) => setTooltip(toTooltip(info)),
            updateTriggers: {
              getLineColor: opacity,
              getFillColor: opacity,
              getLineWidth: lineWidth,
              getPointRadius: pointRadius,
            },
          }),
          new GeoJsonLayer({
            id: "curated-drawing-annotations",
            data: annotationFeatureCollection(analysis?.annotations),
            pickable: true,
            stroked: true,
            filled: true,
            pointType: "circle",
            getLineColor: [175, 55, 35, 255],
            getFillColor: [175, 55, 35, 100],
            getPointColor: [175, 55, 35, 255],
            getLineWidth: 5,
            lineWidthUnits: "pixels",
            getPointRadius: 8,
            pointRadiusUnits: "pixels",
            onHover: (info) => setTooltip(toTooltip(info)),
          }),
        ]
      : [];

    overlayRef.current.setProps({ layers });
  }, [analysis?.annotations, geojson, lineWidth, opacity, pointRadius]);

  useEffect(() => {
    refreshLayer();
  }, [refreshLayer]);

  const fitToData = useCallback(() => {
    if (!geojson || !mapRef.current || !window.google?.maps) {
      return;
    }

    const bounds = new window.google.maps.LatLngBounds();
    let coordinateCount = 0;
    for (const feature of geojson.features || []) {
      visitCoordinates(feature.geometry?.coordinates, (lng, lat) => {
        bounds.extend({ lat, lng });
        coordinateCount += 1;
      });
    }

    if (coordinateCount > 0) {
      mapRef.current.fitBounds(bounds);
    }
  }, [geojson]);

  useEffect(() => {
    fitToData();
  }, [fitToData]);

  useEffect(() => {
    if (!mapRef.current || !window.google?.maps || !pinMode) {
      return undefined;
    }
    const listener = mapRef.current.addListener("click", (event) => {
      if (!isPointCategory(annotationCategory)) {
        setAnalysisError("Use the JSON editor for route or boundary geometry. Click-to-pin supports point categories.");
        return;
      }
      if (!annotationTitle.trim()) {
        setAnalysisError("Enter an annotation title before selecting a map location.");
        return;
      }
      const item = {
        id: crypto.randomUUID?.() || `annotation-${Date.now()}`,
        category: annotationCategory,
        title: annotationTitle.trim(),
        notes: annotationNotes.trim(),
        geometry: { type: "Point", coordinates: [event.latLng.lng(), event.latLng.lat()] },
      };
      const next = { ...(analysis || emptyAnalysis()), annotations: [...(analysis?.annotations || []), item] };
      setAnalysis(next);
      setAnalysisText(JSON.stringify(next, null, 2));
      setPinMode(false);
      setAnnotationTitle("");
      setAnnotationNotes("");
      setAnalysisError("");
    });
    return () => listener.remove();
  }, [analysis, annotationCategory, annotationNotes, annotationTitle, pinMode]);

  const plotConversion = async (conversion) => {
    setSelected(conversion);
    setPlotStatus("loading");
    setPlotError("");
    setTooltip(null);

    try {
      const response = await fetch(`${API_BASE_URL}/cad/geojson/${conversion.conversionId}`);
      if (!response.ok) {
        throw new Error(await readError(response));
      }

      const payload = await response.json();
      if (payload?.geojson?.type !== "FeatureCollection" || !Array.isArray(payload.geojson.features)) {
        throw new Error("The API returned an invalid GeoJSON FeatureCollection.");
      }

      const normalized = normalizeGeojsonForMap(payload.geojson);
      setGeojson(normalized.geojson);
      setCoordinateMode(normalized.mode);
      const nextAnalysis = payload.analysis || emptyAnalysis();
      setAnalysis(nextAnalysis);
      setAnalysisText(JSON.stringify(nextAnalysis, null, 2));
      setAnalysisStatus("idle");
      setAnalysisError("");
      setPlotStatus("ready");
    } catch (error) {
      setGeojson(null);
      setCoordinateMode("none");
      setAnalysis(null);
      setAnalysisText("");
      setPlotError(error.message);
      setPlotStatus("error");
    }
  };

  const saveAnalysis = async () => {
    if (!selected) return;
    let payload;
    try {
      payload = JSON.parse(analysisText || "{}");
    } catch {
      setAnalysisError("Analysis must be valid JSON before it can be saved.");
      return;
    }
    setAnalysisStatus("saving");
    setAnalysisError("");
    try {
      const response = await fetch(`${API_BASE_URL}/cad/geojson/${selected.conversionId}/analysis`, {
        method: "PUT",
        headers: { "content-type": "application/json" },
        body: JSON.stringify(payload),
      });
      if (!response.ok) throw new Error(await readError(response));
      const saved = await response.json();
      setAnalysis(saved);
      setAnalysisText(JSON.stringify(saved, null, 2));
      setAnalysisStatus("saved");
    } catch (error) {
      setAnalysisError(error.message);
      setAnalysisStatus("error");
    }
  };

  return (
    <main className="app-shell">
      <aside className="sidebar">
        <div className="title-row">
          <div>
            <h1>MapPlotter</h1>
            <p>Stored CAD GeoJSON</p>
          </div>
          <button className="icon-button" type="button" onClick={loadConversions} title="Refresh list">
            Refresh
          </button>
        </div>

        <section className="status-strip">
          <span>{conversions.length} files</span>
          <span>{selected?.fileName || "No file plotted"}</span>
        </section>

        {listStatus === "loading" && <div className="message">Loading stored conversions...</div>}
        {listStatus === "error" && <div className="message error">{listError}</div>}
        {listStatus === "ready" && conversions.length === 0 && (
          <div className="message">No stored conversions were found in Cosmos DB.</div>
        )}

        <div className="conversion-list">
          {conversions.map((conversion) => (
            <article
              className={`conversion-row ${selected?.conversionId === conversion.conversionId ? "active" : ""}`}
              key={conversion.conversionId}
            >
              <div className="conversion-main">
                <strong>{conversion.fileName || "(unnamed drawing)"}</strong>
                <span>{formatDate(conversion.createdUtc)}</span>
              </div>
              <div className="conversion-meta">
                <span>{formatNumber(conversion.featureCount)} features</span>
                <span>{formatNumber(conversion.chunkCount)} chunks</span>
              </div>
              <button type="button" onClick={() => plotConversion(conversion)}>
                Plot
              </button>
            </article>
          ))}
        </div>

        <section className="controls">
          <ControlSlider label="Line width" value={lineWidth} min="0.5" max="8" step="0.5" onChange={setLineWidth} />
          <ControlSlider label="Point radius" value={pointRadius} min="1" max="12" step="1" onChange={setPointRadius} />
          <ControlSlider label="Opacity" value={opacity} min="0.1" max="1" step="0.1" onChange={setOpacity} />
          <button type="button" className="secondary-button" onClick={fitToData} disabled={!geojson}>
            Fit to data
          </button>
        </section>

        {selected && (
          <section className="analysis-editor">
            <h2>Drawing content & annotations</h2>
            <p>Curated facts and annotations are saved separately from the original CAD GeoJSON.</p>
            <button
              type="button"
              className="secondary-button"
              onClick={() => {
                const template = suppliedHvAsLaidTemplate();
                setAnalysis(template);
                setAnalysisText(JSON.stringify(template, null, 2));
                setAnalysisError("");
              }}
            >
              Load HV As Laid template
            </button>
            <div className="pin-form">
              <select value={annotationCategory} onChange={(event) => setAnnotationCategory(event.target.value)}>
                {ANNOTATION_CATEGORIES.map((category) => <option key={category} value={category}>{labelForCategory(category)}</option>)}
              </select>
              <input value={annotationTitle} onChange={(event) => setAnnotationTitle(event.target.value)} placeholder="Pin title" />
              <input value={annotationNotes} onChange={(event) => setAnnotationNotes(event.target.value)} placeholder="Notes (optional)" />
              <button type="button" className="secondary-button" onClick={() => setPinMode(!pinMode)}>
                {pinMode ? "Cancel pin" : "Place point pin"}
              </button>
            </div>
            {pinMode && <div className="message">Click a map location to place the selected pin.</div>}
            <textarea
              aria-label="Curated drawing analysis JSON"
              value={analysisText}
              onChange={(event) => setAnalysisText(event.target.value)}
              spellCheck="false"
            />
            <button type="button" onClick={saveAnalysis} disabled={analysisStatus === "saving"}>
              {analysisStatus === "saving" ? "Saving…" : "Save drawing content"}
            </button>
            {analysisStatus === "saved" && <div className="message success">Drawing content saved.</div>}
            {analysisError && <div className="message error">{analysisError}</div>}
          </section>
        )}
      </aside>

      <section className="map-region">
        <div className="map-canvas" ref={mapElementRef} />

        {!GOOGLE_MAPS_API_KEY && (
          <div className="map-overlay">
            Set <code>VITE_GOOGLE_MAPS_API_KEY</code> in <code>MapPlotter/.env</code>.
          </div>
        )}

        {GOOGLE_MAPS_API_KEY && !mapsReady && <div className="map-overlay">Loading Google Maps...</div>}

        <div className="map-summary">
          <strong>{selected?.fileName || "Choose a file to plot"}</strong>
          <span>{summary.featureCount.toLocaleString()} features</span>
          <span>{summary.layerCount.toLocaleString()} layers</span>
          <span>{analysis?.annotations?.length || 0} annotations</span>
          {coordinateMode === "cad" && <span>CAD view</span>}
          {plotStatus === "loading" && <span>Loading GeoJSON...</span>}
          {plotStatus === "error" && <span className="error-text">{plotError}</span>}
        </div>

        {summary.layers.length > 0 && (
          <div className="legend">
            {summary.layers.slice(0, 18).map(([layer, count]) => {
              const [red, green, blue] = colorForLayer(layer);
              return (
                <div key={layer}>
                  <span style={{ backgroundColor: `rgb(${red}, ${green}, ${blue})` }} />
                  <p>{layer}</p>
                  <small>{count}</small>
                </div>
              );
            })}
          </div>
        )}

        {tooltip && (
          <div className="tooltip" style={{ left: tooltip.x + 14, top: tooltip.y + 14 }}>
            <strong>{tooltip.entityType}</strong>
            <span>{tooltip.coordinates}</span>
            <span>Layer: {tooltip.layer}</span>
            {tooltip.text && <span>Text: {tooltip.text}</span>}
            {tooltip.handle && <span>Handle: {tooltip.handle}</span>}
          </div>
        )}
      </section>
    </main>
  );
}

function ControlSlider({ label, value, min, max, step, onChange }) {
  return (
    <label className="control-slider">
      <span>
        {label} <strong>{value}</strong>
      </span>
      <input
        type="range"
        min={min}
        max={max}
        step={step}
        value={value}
        onChange={(event) => onChange(Number(event.target.value))}
      />
    </label>
  );
}

async function readError(response) {
  try {
    const body = await response.json();
    return body.message || body.error || `${response.status} ${response.statusText}`;
  } catch {
    return `${response.status} ${response.statusText}`;
  }
}

function emptyAnalysis() {
  return {
    drawing: {
      number: "",
      revision: "",
      title: "",
      author: "",
      format: "",
      savedUsing: "",
    },
    routeSummary: "",
    cableLengths: [],
    layouts: [],
    cadBlocks: [],
    caveats: [],
    annotations: [],
  };
}

function suppliedHvAsLaidTemplate() {
  return {
    drawing: {
      number: "P2122392-041",
      revision: "Rev 4",
      title: "HV As Laid",
      format: "AutoCAD 2018–2020",
      savedUsing: "AutoCAD LT 2023",
      author: "Nikita Wells",
    },
    routeSummary: "Multi-sheet high-voltage electrical cable route drawing. Includes an overall site/general arrangement, the as-laid HV mains alignment, detailed road/junction/development sections, a proposed/new primary substation and Proposed Primary Substation – Option 2, service routes, crossings, directional drilling, ducted/direct-laid arrangements, auxiliary/fibre routes and commercial boundary mapping.",
    cableLengths: [
      { label: "Earlier/original total cable length", metres: 10404 },
      { label: "Revised total cable length after mains added", metres: 17106 },
      { label: "Referenced quantity (context requires verification)", quantity: "2 × 4,710 metres" },
    ],
    layouts: ["Sheet 1", "Sheet 2", "Sheet 6"],
    cadBlocks: [
      "Combined services arrangements",
      "Electrical, gas and water services",
      "200A HDCO GF95 riser",
      "400A HDCO GF185 riser",
      "Three-phase metering board",
      "Gas ECV and GRP cabinet arrangements",
      "Vertical, horizontal and flanged service inlet details",
      "General Arrangement",
      "Advanced Road Crossings",
      "Legal Plan",
      "PRI location",
    ],
    caveats: [
      "This is a user-curated record based on a low-resolution drawing inspection.",
      "Individual symbol quantities, smaller labels and exact coordinates require high-resolution PDF/DXF or CAD verification.",
      "Stored CAD blocks/details may be reusable definitions and may not be physically placed on the HV route.",
    ],
    annotations: [],
  };
}

function annotationFeatureCollection(annotations) {
  return {
    type: "FeatureCollection",
    features: (annotations || []).filter((item) => item?.geometry).map((item) => ({
      type: "Feature",
      geometry: item.geometry,
      properties: {
        entityType: labelForCategory(item.category),
        layer: "Curated drawing annotation",
        text: item.notes,
        annotationId: item.id,
      },
    })),
  };
}

function isPointCategory(category) {
  return !["cable-route-segment", "utility-service-route", "commercial-boundary"].includes(category);
}

function labelForCategory(category) {
  return String(category || "custom").replaceAll("-", " ").replace(/\b\w/g, (letter) => letter.toUpperCase());
}

function summarizeGeojson(data) {
  const features = data?.features || [];
  const counts = new Map();
  for (const feature of features) {
    const layer = feature.properties?.layer || "(none)";
    counts.set(layer, (counts.get(layer) || 0) + 1);
  }

  const layers = [...counts.entries()].sort((left, right) => right[1] - left[1]);
  return {
    featureCount: features.length,
    layerCount: layers.length,
    layers,
  };
}

function normalizeGeojsonForMap(data) {
  const bounds = collectCoordinateBounds(data);
  if (!bounds.count) {
    return { geojson: data, mode: "none" };
  }

  if (isLikelyWgs84(bounds)) {
    return { geojson: data, mode: "wgs84" };
  }

  const width = Math.max(bounds.maxX - bounds.minX, 1);
  const height = Math.max(bounds.maxY - bounds.minY, 1);
  const scale = CAD_VIEW_MAX_METERS / Math.max(width, height);
  const sourceCenterX = (bounds.minX + bounds.maxX) / 2;
  const sourceCenterY = (bounds.minY + bounds.maxY) / 2;
  const metersPerLatDegree = 110_540;
  const metersPerLngDegree = 111_320 * Math.cos((DEFAULT_CENTER.lat * Math.PI) / 180);

  return {
    mode: "cad",
    geojson: mapGeojsonCoordinates(data, (x, y, rest) => {
      const eastMeters = (x - sourceCenterX) * scale;
      const northMeters = (y - sourceCenterY) * scale;
      return [
        DEFAULT_CENTER.lng + eastMeters / metersPerLngDegree,
        DEFAULT_CENTER.lat + northMeters / metersPerLatDegree,
        ...rest,
      ];
    }),
  };
}

function isLikelyWgs84(bounds) {
  return (
    bounds.minX >= -180 &&
    bounds.maxX <= 180 &&
    bounds.minY >= -85 &&
    bounds.maxY <= 85
  );
}

function collectCoordinateBounds(data) {
  const bounds = {
    minX: Number.POSITIVE_INFINITY,
    minY: Number.POSITIVE_INFINITY,
    maxX: Number.NEGATIVE_INFINITY,
    maxY: Number.NEGATIVE_INFINITY,
    count: 0,
  };

  for (const feature of data?.features || []) {
    visitCoordinates(feature.geometry?.coordinates, (x, y) => {
      if (!Number.isFinite(x) || !Number.isFinite(y)) {
        return;
      }
      bounds.minX = Math.min(bounds.minX, x);
      bounds.minY = Math.min(bounds.minY, y);
      bounds.maxX = Math.max(bounds.maxX, x);
      bounds.maxY = Math.max(bounds.maxY, y);
      bounds.count += 1;
    });
  }

  return bounds;
}

function mapGeojsonCoordinates(data, mapper) {
  return {
    ...data,
    features: (data.features || []).map((feature) => ({
      ...feature,
      geometry: feature.geometry
        ? {
            ...feature.geometry,
            coordinates: mapCoordinates(feature.geometry.coordinates, mapper),
          }
        : feature.geometry,
    })),
  };
}

function mapCoordinates(value, mapper) {
  if (!Array.isArray(value)) {
    return value;
  }

  if (typeof value[0] === "number" && typeof value[1] === "number") {
    return mapper(value[0], value[1], value.slice(2));
  }

  return value.map((child) => mapCoordinates(child, mapper));
}

function visitCoordinates(value, visitor) {
  if (!Array.isArray(value)) {
    return;
  }

  if (typeof value[0] === "number" && typeof value[1] === "number") {
    visitor(value[0], value[1]);
    return;
  }

  for (const child of value) {
    visitCoordinates(child, visitor);
  }
}

function toTooltip(info) {
  if (!info?.object) {
    return null;
  }

  const properties = info.object.properties || {};
  const coordinate = info.coordinate
    ? `${info.coordinate[1].toFixed(6)}, ${info.coordinate[0].toFixed(6)}`
    : "Coordinate unavailable";

  return {
    x: info.x,
    y: info.y,
    entityType: properties.entityType || "CAD feature",
    coordinates: coordinate,
    layer: properties.layer || "-",
    text: properties.text,
    handle: properties.handle,
  };
}

function formatDate(value) {
  if (!value) {
    return "Date unavailable";
  }

  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return value;
  }

  return date.toLocaleString();
}

function formatNumber(value) {
  return Number(value || 0).toLocaleString();
}

export default App;
