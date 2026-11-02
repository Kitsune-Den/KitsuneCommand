<script setup lang="ts">
import { ref, onMounted, onUnmounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { getMapInfo, getMapMarkers, getMapPois } from '@/api/map'
import type { MapInfo, MapPoi } from '@/types'
import L from 'leaflet'
import 'leaflet/dist/leaflet.css'

const { t } = useI18n()
const mapContainer = ref<HTMLElement | null>(null)
const mapInfo = ref<MapInfo | null>(null)
const loading = ref(true)
const errorMsg = ref('')
let leafletMap: L.Map | null = null
let markersLayer: L.LayerGroup | null = null
let markerRefreshInterval: ReturnType<typeof setInterval> | null = null
let resizeObserver: ResizeObserver | null = null

// ── Coordinate conversion ──
// Standard L.CRS.Simple tile grid at zoom 0: one 256x256 tile
//   lng: 0 -> 256 (left to right)
//   lat: 0 -> -256 (top to bottom, since py = -lat)
//
// Game coordinates (e.g. Navezgane, 6144x6144, centered at 0,0):
//   X: -3072 (west) -> 3072 (east)  ->  lng 0 -> 256
//   Z: +3072 (north/top) -> -3072 (south/bottom)  ->  lat 0 -> -256
// The server sends the real world size; biomes.png is smaller than the world.
let _worldSize = 6144
let _halfWorld = 3072

// Player and POI names go into Leaflet's HTML popups and tooltips
function escapeHtml(text: string): string {
  return text.replace(/[&<>"']/g, c => `&#${c.charCodeAt(0)};`)
}

function gameToLatLng(gameX: number, gameZ: number): L.LatLng {
  const lng = (gameX + _halfWorld) / _worldSize * 256
  const lat = (gameZ - _halfWorld) / _worldSize * 256 // Z=1536->0, Z=-1536->-256
  return L.latLng(lat, lng)
}

function latLngToGame(latlng: L.LatLng): { x: number, z: number } {
  const gameX = latlng.lng / 256 * _worldSize - _halfWorld
  const gameZ = latlng.lat / 256 * _worldSize + _halfWorld
  return { x: gameX, z: gameZ }
}

async function initMap() {
  try {
    const info = await getMapInfo()
    mapInfo.value = info

    if (!info.isAvailable) {
      errorMsg.value = t('map.mapNotAvailable')
      loading.value = false
      return
    }

    if (!mapContainer.value) return

    _worldSize = info.worldSize
    _halfWorld = info.worldSize / 2

    // ── Map setup with standard CRS.Simple ──
    leafletMap = L.map(mapContainer.value, {
      crs: L.CRS.Simple,
      minZoom: 0,
      maxZoom: info.maxZoom,
      zoomControl: true,
      attributionControl: false,
    })

    // World bounds in CRS.Simple coordinates
    const worldBounds = L.latLngBounds(
      [-256, 0],  // SW: bottom-left
      [0, 256],   // NE: top-right
    )

    // Tile layer -- Leaflet's CRS.Simple tile grid matches our backend:
    // at zoom z, 2^z x 2^z tiles of 256px each
    // `bounds` prevents Leaflet from requesting tiles outside the world area
    const tileUrl = `${window.location.origin}/api/map/tile/{z}/{x}/{y}`
    L.tileLayer(tileUrl, {
      minZoom: 0,
      maxZoom: info.maxZoom,
      tileSize: 256,
      noWrap: true,
      bounds: worldBounds,
    }).addTo(leafletMap)

    // Constrain panning so the user can't scroll beyond the world
    leafletMap.setMaxBounds(worldBounds.pad(0.1))

    // Initial view: center of the world, zoom 1 (good default for most screens)
    const initialZoom = Math.min(1, info.maxZoom)
    leafletMap.setView([-128, 128], initialZoom)

    // ResizeObserver: when the flex container gets its final dimensions,
    // tell Leaflet to recalculate and fit the world properly
    let hasAdjustedView = false
    resizeObserver = new ResizeObserver((entries) => {
      if (!leafletMap) return
      const { width, height } = entries[0].contentRect
      if (width > 0 && height > 0) {
        leafletMap.invalidateSize({ animate: false })
        if (!hasAdjustedView) {
          leafletMap.fitBounds(worldBounds)
          hasAdjustedView = true
        }
      }
    })
    resizeObserver.observe(mapContainer.value)

    // POI layer: the footprints are drawn into the tiles; these invisible
    // rectangles just name them on hover
    const poiLayer = L.layerGroup().addTo(leafletMap)
    loadPois(poiLayer)

    // Player markers layer
    markersLayer = L.layerGroup().addTo(leafletMap)
    await refreshMarkers()

    L.control.layers(undefined, {
      [t('map.layerPlayers')]: markersLayer,
      [t('map.layerPois')]: poiLayer,
    }, { position: 'topright' }).addTo(leafletMap)

    // Refresh markers every 5 seconds
    markerRefreshInterval = setInterval(refreshMarkers, 5000)

    // Show game coordinates on click
    leafletMap.on('click', (e: L.LeafletMouseEvent) => {
      const game = latLngToGame(e.latlng)
      L.popup()
        .setLatLng(e.latlng)
        .setContent(`<b>${t('map.positionLabel')}</b><br>X: ${Math.round(game.x)}, Z: ${Math.round(game.z)}`)
        .openOn(leafletMap!)
    })

    loading.value = false
  } catch (err) {
    errorMsg.value = t('map.failedToLoad')
    loading.value = false
  }
}

async function loadPois(layer: L.LayerGroup) {
  let pois: MapPoi[]
  try {
    pois = await getMapPois()
  } catch {
    return // The map is still useful without POI names
  }

  // One canvas for all of them; a big world has thousands
  const renderer = L.canvas({ padding: 0.2 })
  for (const p of pois) {
    const rect = L.rectangle(
      L.latLngBounds(gameToLatLng(p.x, p.z), gameToLatLng(p.x + p.sizeX, p.z + p.sizeZ)),
      { renderer, color: '#ffcc66', weight: 1, opacity: 0, fillOpacity: 0 },
    )
    rect.bindTooltip(escapeHtml(p.name), { sticky: true, direction: 'top' })
    rect.on('mouseover', () => rect.setStyle({ opacity: 0.9, fillOpacity: 0.15 }))
    rect.on('mouseout', () => rect.setStyle({ opacity: 0, fillOpacity: 0 }))
    layer.addLayer(rect)
  }
}

async function refreshMarkers() {
  if (!markersLayer || !leafletMap) return

  try {
    const markers = await getMapMarkers()
    markersLayer.clearLayers()

    for (const m of markers) {
      const pos = gameToLatLng(m.x, m.z)
      const marker = L.circleMarker(pos, {
        radius: 6,
        fillColor: '#00d4ff',
        fillOpacity: 0.9,
        color: '#fff',
        weight: 2,
      })

      const name = escapeHtml(m.name)
      marker.bindPopup(
        `<b>${name}</b><br>` +
        `${t('map.positionLabel')}: ${Math.round(m.x)}, ${Math.round(m.y)}, ${Math.round(m.z)}`,
      )

      marker.bindTooltip(name, { permanent: false, direction: 'top', offset: [0, -8] })
      markersLayer.addLayer(marker)
    }
  } catch {
    // Silently fail for marker refresh
  }
}

onMounted(initMap)

onUnmounted(() => {
  if (resizeObserver) {
    resizeObserver.disconnect()
    resizeObserver = null
  }
  if (markerRefreshInterval) clearInterval(markerRefreshInterval)
  if (leafletMap) {
    leafletMap.remove()
    leafletMap = null
  }
})
</script>

<template>
  <div class="map-view">
    <div class="page-header">
      <h1 class="page-title">{{ t('map.title') }}</h1>
    </div>

    <!-- Map container is ALWAYS rendered so Leaflet can measure it -->
    <div ref="mapContainer" class="map-container">
      <!-- Loading / error overlays sit inside the map container -->
      <div v-if="loading" class="map-overlay">
        <i class="pi pi-spin pi-spinner" style="font-size: 2rem"></i>
        <p>{{ t('map.loadingMap') }}</p>
      </div>
      <div v-else-if="errorMsg" class="map-overlay">
        <i class="pi pi-exclamation-triangle" style="font-size: 2rem; color: var(--kc-orange)"></i>
        <p>{{ errorMsg }}</p>
      </div>
    </div>
  </div>
</template>

<style scoped>
.map-view {
  display: flex;
  flex-direction: column;
  height: calc(100vh - 3rem);
  gap: 1rem;
}

.page-header {
  display: flex;
  align-items: center;
  gap: 1rem;
}

.page-title {
  font-size: 1.5rem;
  font-weight: 600;
}

.map-container {
  flex: 1;
  border-radius: 8px;
  border: 1px solid var(--kc-border);
  overflow: hidden;
  min-height: 400px;
  background: #1a1a2e;
  position: relative;
}

.map-overlay {
  position: absolute;
  inset: 0;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 1rem;
  color: var(--kc-text-secondary);
  z-index: 1000;
  background: #1a1a2e;
}

@media (max-width: 640px) {
  .map-container { min-height: 250px; }
}
</style>

<style>
/* ── Leaflet CSS overrides ──
   Prevent CSS frameworks (PrimeVue, normalize.css, etc.) from
   interfering with Leaflet's internal element sizing. */
.leaflet-container img {
  max-width: none !important;
  max-height: none !important;
}

.leaflet-tile-pane img {
  max-width: none !important;
  max-height: none !important;
  width: 256px;
  height: 256px;
}

/* Leaflet popup styling for dark theme */
.leaflet-popup-content-wrapper {
  background: var(--kc-bg-card, #1e1e2e);
  color: var(--kc-text-primary, #e0e0e0);
  border: 1px solid var(--kc-border, #333);
  border-radius: 6px;
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.3);
}

.leaflet-popup-tip {
  background: var(--kc-bg-card, #1e1e2e);
}

.leaflet-container {
  background: #0d1117;
  font-size: 12px;
}

/* Ensure Leaflet zoom controls are visible on dark bg */
.leaflet-control-zoom a {
  background-color: var(--kc-bg-card, #1e1e2e) !important;
  color: var(--kc-text-primary, #e0e0e0) !important;
  border-color: var(--kc-border, #333) !important;
}

.leaflet-control-zoom a:hover {
  background-color: var(--kc-bg-secondary, #1a2332) !important;
}

.leaflet-control-layers {
  background: var(--kc-bg-card, #1e1e2e);
  color: var(--kc-text-primary, #e0e0e0);
  border: 1px solid var(--kc-border, #333) !important;
}
</style>
