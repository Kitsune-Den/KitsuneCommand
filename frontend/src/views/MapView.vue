<script setup lang="ts">
import { ref, onMounted, onUnmounted } from 'vue'
import { getMapInfo, getMapMarkers } from '@/api/map'
import type { MapInfo } from '@/types'
import L from 'leaflet'
import 'leaflet/dist/leaflet.css'

const mapContainer = ref<HTMLElement | null>(null)
const mapInfo = ref<MapInfo | null>(null)
const loading = ref(true)
const errorMsg = ref('')
let leafletMap: L.Map | null = null
let markersLayer: L.LayerGroup | null = null
let markerRefreshInterval: ReturnType<typeof setInterval> | null = null
let resizeObserver: ResizeObserver | null = null

// ── Coordinate conversion ──
// Standard L.CRS.Simple tile grid at zoom 0: one 256×256 tile
//   lng: 0 → 256 (left to right)
//   lat: 0 → −256 (top to bottom, since py = −lat)
//
// Game coordinates (Navezgane 3072×3072, centered at 0,0):
//   X: −1536 (west) → 1536 (east)  →  lng 0 → 256
//   Z: +1536 (north/top) → −1536 (south/bottom)  →  lat 0 → −256
let _worldSize = 3072
let _halfWorld = 1536

function gameToLatLng(gameX: number, gameZ: number): L.LatLng {
  const lng = (gameX + _halfWorld) / _worldSize * 256
  const lat = (gameZ - _halfWorld) / _worldSize * 256 // Z=1536→0, Z=-1536→−256
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
      errorMsg.value = 'Map renderer not available on the server.'
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

    // Tile layer — Leaflet's CRS.Simple tile grid matches our backend:
    // at zoom z, 2^z × 2^z tiles of 256px each
    const tileUrl = `${window.location.origin}/api/map/tile/{z}/{x}/{y}`
    L.tileLayer(tileUrl, {
      minZoom: 0,
      maxZoom: info.maxZoom,
      tileSize: 256,
      noWrap: true,
    }).addTo(leafletMap)

    // World bounds in CRS.Simple coordinates
    const worldBounds = L.latLngBounds(
      [-256, 0],  // SW: bottom-left
      [0, 256],   // NE: top-right
    )

    // Initial view: center of the world, zoom 1 (good default for most screens)
    // At zoom 1 the world is 512×512 px — fits well in most containers
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

    // Player markers layer
    markersLayer = L.layerGroup().addTo(leafletMap)
    await refreshMarkers()

    // Refresh markers every 5 seconds
    markerRefreshInterval = setInterval(refreshMarkers, 5000)

    // Show game coordinates on click
    leafletMap.on('click', (e: L.LeafletMouseEvent) => {
      const game = latLngToGame(e.latlng)
      L.popup()
        .setLatLng(e.latlng)
        .setContent(`<b>Position</b><br>X: ${Math.round(game.x)}, Z: ${Math.round(game.z)}`)
        .openOn(leafletMap!)
    })

    loading.value = false
  } catch (err) {
    errorMsg.value = 'Failed to load map data.'
    loading.value = false
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

      marker.bindPopup(
        `<b>${m.name}</b><br>` +
        `Position: ${Math.round(m.x)}, ${Math.round(m.y)}, ${Math.round(m.z)}`,
      )

      marker.bindTooltip(m.name, { permanent: false, direction: 'top', offset: [0, -8] })
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
      <h1 class="page-title">Map</h1>
    </div>

    <!-- Map container is ALWAYS rendered so Leaflet can measure it -->
    <div ref="mapContainer" class="map-container">
      <!-- Loading / error overlays sit inside the map container -->
      <div v-if="loading" class="map-overlay">
        <i class="pi pi-spin pi-spinner" style="font-size: 2rem"></i>
        <p>Loading map...</p>
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
</style>
