<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { useToast } from 'primevue/usetoast'
import { useConfirm } from 'primevue/useconfirm'
import Button from 'primevue/button'
import Dialog from 'primevue/dialog'
import InputText from 'primevue/inputtext'
import InputNumber from 'primevue/inputnumber'
import ToggleSwitch from 'primevue/toggleswitch'
import { getMapInfo, getMapMarkers, getMapPois, getLandClaims } from '@/api/map'
import { getSafeZones, saveSafeZones } from '@/api/safeZones'
import type { MapInfo, MapPoi, SafeZone, SafeZoneSettings } from '@/types'
import { escapeHtml } from '@/lib/escapeHtml'
import { claimRect, zoneRect, cornersToZone, ownerColor, type BlockRect } from '@/lib/mapAreas'
import { usePermissions } from '@/composables/usePermissions'
import L from 'leaflet'
import 'leaflet/dist/leaflet.css'

const { t } = useI18n()
const toast = useToast()
const confirm = useConfirm()
const { isAdmin, isModerator } = usePermissions()
// Land claims show where bases are, so only staff get them
const canSeeClaims = computed(() => isAdmin.value || isModerator.value)

const mapContainer = ref<HTMLElement | null>(null)
const mapInfo = ref<MapInfo | null>(null)
const loading = ref(true)
const errorMsg = ref('')
let leafletMap: L.Map | null = null
let markersLayer: L.LayerGroup | null = null
let zonesLayer: L.LayerGroup | null = null
let claimsLayer: L.LayerGroup | null = null
let drawPreview: L.Rectangle | null = null
let markerRefreshInterval: ReturnType<typeof setInterval> | null = null
let resizeObserver: ResizeObserver | null = null

// ── Safe zone state ──
const safeZones = ref<SafeZoneSettings | null>(null)
const showZonePanel = ref(false)
const savingZones = ref(false)
// 'first' waits for the first corner click, 'second' for the opposite corner
const drawStep = ref<'off' | 'first' | 'second'>('off')
let firstCorner: { x: number, z: number } | null = null
const editDialogVisible = ref(false)
const editZone = ref<SafeZone | null>(null)
const editIsNew = ref(false)

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

function rectBounds(r: BlockRect): L.LatLngBounds {
  return L.latLngBounds(gameToLatLng(r.minX, r.minZ), gameToLatLng(r.maxX, r.maxZ))
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
    // tell Leaflet to recalculate and fit the world properly. This also
    // catches the zone panel opening and closing beside the map.
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

    // Safe zones sit under the land claims and the players
    zonesLayer = L.layerGroup().addTo(leafletMap)
    loadSafeZones()

    if (canSeeClaims.value) {
      claimsLayer = L.layerGroup().addTo(leafletMap)
      loadLandClaims()
    }

    // Player markers layer
    markersLayer = L.layerGroup().addTo(leafletMap)
    await refreshMarkers()

    const overlays: Record<string, L.Layer> = {
      [t('map.layerPlayers')]: markersLayer,
      [t('map.layerPois')]: poiLayer,
      [t('map.layerSafeZones')]: zonesLayer,
    }
    if (claimsLayer) overlays[t('map.layerLandClaims')] = claimsLayer
    L.control.layers(undefined, overlays, { position: 'topright' }).addTo(leafletMap)

    // Refresh markers every 5 seconds
    markerRefreshInterval = setInterval(refreshMarkers, 5000)

    // Show game coordinates on click, or place a zone corner while drawing
    leafletMap.on('click', (e: L.LeafletMouseEvent) => {
      const game = latLngToGame(e.latlng)
      if (drawStep.value !== 'off') {
        placeCorner(game)
        return
      }
      L.popup()
        .setLatLng(e.latlng)
        .setContent(`<b>${t('map.positionLabel')}</b><br>X: ${Math.round(game.x)}, Z: ${Math.round(game.z)}`)
        .openOn(leafletMap!)
    })
    leafletMap.on('mousemove', (e: L.LeafletMouseEvent) => {
      if (drawStep.value === 'second' && firstCorner) updatePreview(firstCorner, latLngToGame(e.latlng))
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

// ── Land claims ──

async function loadLandClaims() {
  if (!claimsLayer) return
  let snapshot
  try {
    snapshot = await getLandClaims()
  } catch {
    return // The map is still useful without them
  }

  // A busy server has hundreds of claims; draw them all on one canvas
  const renderer = L.canvas({ padding: 0.2 })
  claimsLayer.clearLayers()
  for (const c of snapshot.claims) {
    const color = ownerColor(c.ownerId ?? c.ownerName)
    const rect = L.rectangle(rectBounds(claimRect(c.x, c.z, snapshot.claimSize)), {
      renderer, color, weight: 2, opacity: 0.9, fillColor: color, fillOpacity: 0.12, dashArray: '4 3',
    })
    // A tooltip rather than a popup, so a click still shows coordinates
    // (or places a zone corner) like anywhere else on the map
    rect.bindTooltip(
      `<b>${t('map.claimOf', { name: escapeHtml(c.ownerName) })}</b><br>` +
      `${t('map.positionLabel')}: ${c.x}, ${c.y}, ${c.z}`,
      { sticky: true, direction: 'top' },
    )
    claimsLayer.addLayer(rect)
    claimsLayer.addLayer(L.circleMarker(gameToLatLng(c.x + 0.5, c.z + 0.5), {
      renderer, radius: 3, color, weight: 1, fillColor: color, fillOpacity: 1, interactive: false,
    }))
  }
}

// ── Safe zones ──

async function loadSafeZones() {
  try {
    safeZones.value = await getSafeZones()
  } catch {
    return // The map works without them
  }
  renderZones()
}

function renderZones() {
  if (!zonesLayer || !safeZones.value) return
  zonesLayer.clearLayers()
  const masterOn = safeZones.value.enabled
  for (const zone of safeZones.value.zones) {
    const active = masterOn && zone.enabled
    const color = active ? '#4ade80' : '#9ca3af'
    const rect = L.rectangle(rectBounds(zoneRect(zone)), {
      color,
      weight: 2,
      fillColor: color,
      fillOpacity: active ? 0.15 : 0.06,
      dashArray: active ? undefined : '6 4',
    })
    const label = escapeHtml(zone.name) + (active ? '' : ` (${t('map.zoneOff')})`)
    rect.bindTooltip(label, { sticky: true, direction: 'top' })
    if (isAdmin.value) {
      rect.on('click', (e: L.LeafletMouseEvent) => {
        if (drawStep.value !== 'off') return // let the map place the corner
        L.DomEvent.stopPropagation(e)
        openEdit(zone)
      })
    }
    zonesLayer.addLayer(rect)
  }
}

function zoneSize(zone: SafeZone): string {
  return `${zone.maxX - zone.minX + 1} × ${zone.maxZ - zone.minZ + 1}`
}

function focusZone(zone: SafeZone) {
  leafletMap?.fitBounds(rectBounds(zoneRect(zone)).pad(0.5))
}

// Leaflet owns the container's class list, so toggle the drawing cursor by
// hand; a Vue :class binding would overwrite Leaflet's classes.
function setDrawingCursor(on: boolean) {
  mapContainer.value?.classList.toggle('drawing', on)
}

function startDrawing() {
  drawStep.value = 'first'
  firstCorner = null
  leafletMap?.closePopup()
  setDrawingCursor(true)
}

function stopDrawing() {
  drawStep.value = 'off'
  firstCorner = null
  if (drawPreview) {
    drawPreview.remove()
    drawPreview = null
  }
  setDrawingCursor(false)
}

function placeCorner(game: { x: number, z: number }) {
  if (drawStep.value === 'first') {
    firstCorner = game
    drawStep.value = 'second'
    updatePreview(game, game)
    return
  }
  if (!firstCorner) return
  const corners = cornersToZone(firstCorner, game)
  stopDrawing()
  editIsNew.value = true
  editZone.value = {
    id: '',
    name: '',
    enabled: true,
    ...corners,
    removeAnimals: false,
    allowBloodMoon: false,
  }
  editDialogVisible.value = true
}

function updatePreview(a: { x: number, z: number }, b: { x: number, z: number }) {
  if (!leafletMap) return
  const bounds = rectBounds(zoneRect(cornersToZone(a, b)))
  if (drawPreview) {
    drawPreview.setBounds(bounds)
  } else {
    drawPreview = L.rectangle(bounds, {
      color: '#4ade80', weight: 2, dashArray: '6 4', fillOpacity: 0.1, interactive: false,
    }).addTo(leafletMap)
  }
}

function openEdit(zone: SafeZone) {
  editIsNew.value = false
  editZone.value = { ...zone }
  editDialogVisible.value = true
}

async function persist(next: SafeZoneSettings, successKey: string): Promise<boolean> {
  savingZones.value = true
  try {
    safeZones.value = await saveSafeZones(next)
    renderZones()
    toast.add({ severity: 'success', summary: t('common.success'), detail: t(successKey), life: 3000 })
    return true
  } catch {
    toast.add({ severity: 'error', summary: t('common.error'), detail: t('map.zoneSaveFailed'), life: 4000 })
    return false
  } finally {
    savingZones.value = false
  }
}

async function saveEditedZone() {
  if (!editZone.value || !safeZones.value) return
  const zone = editZone.value
  // A cleared number box gives null, which the server can't read as a coordinate
  if ([zone.minX, zone.minZ, zone.maxX, zone.maxZ].some(v => typeof v !== 'number')) {
    toast.add({ severity: 'warn', summary: t('common.validation'), detail: t('map.zoneCoordsRequired'), life: 4000 })
    return
  }
  const zones = editIsNew.value
    ? [...safeZones.value.zones, zone]
    : safeZones.value.zones.map(z => (z.id === zone.id ? zone : z))
  if (await persist({ ...safeZones.value, zones }, 'map.zoneSaved')) {
    editDialogVisible.value = false
  }
}

function deleteZone(zone: SafeZone) {
  confirm.require({
    message: t('map.zoneDeleteConfirm', { name: zone.name }),
    header: t('common.confirmDelete'),
    icon: 'pi pi-trash',
    acceptClass: 'p-button-danger',
    accept: async () => {
      if (!safeZones.value) return
      const zones = safeZones.value.zones.filter(z => z.id !== zone.id)
      if (await persist({ ...safeZones.value, zones }, 'map.zoneDeleted')) {
        editDialogVisible.value = false
      }
    },
  })
}

function setZoneEnabled(zone: SafeZone, enabled: boolean) {
  if (!safeZones.value) return
  const zones = safeZones.value.zones.map(z => (z.id === zone.id ? { ...z, enabled } : z))
  persist({ ...safeZones.value, zones }, 'map.zoneSaved')
}

function setZonesEnabled(enabled: boolean) {
  if (!safeZones.value) return
  persist({ ...safeZones.value, enabled }, 'map.zoneSaved')
}

function toggleZonePanel() {
  showZonePanel.value = !showZonePanel.value
  if (showZonePanel.value) loadSafeZones() // pick up fresh removal counts
  else stopDrawing()
}

function onKeydown(e: KeyboardEvent) {
  if (e.key === 'Escape' && drawStep.value !== 'off') stopDrawing()
}

onMounted(() => {
  window.addEventListener('keydown', onKeydown)
  initMap()
})

onUnmounted(() => {
  window.removeEventListener('keydown', onKeydown)
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
      <Button
        v-if="isAdmin && !errorMsg && !loading"
        class="zone-toggle"
        :label="t('map.safeZones')"
        icon="pi pi-shield"
        :severity="showZonePanel ? 'success' : 'secondary'"
        :outlined="!showZonePanel"
        size="small"
        @click="toggleZonePanel"
      />
    </div>

    <div class="map-body">
      <!-- Map container is ALWAYS rendered so Leaflet can measure it -->
      <div ref="mapContainer" class="map-container">
        <!-- Stop clicks here reaching Leaflet, or they'd place a corner -->
        <div
          v-if="drawStep !== 'off'"
          class="draw-hint"
          @click.stop
          @dblclick.stop
          @mousedown.stop
          @pointerdown.stop
        >
          <i class="pi pi-pencil"></i>
          <span>{{ drawStep === 'first' ? t('map.drawFirstCorner') : t('map.drawSecondCorner') }}</span>
          <Button :label="t('common.cancel')" size="small" text @click="stopDrawing" />
        </div>
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

      <aside v-if="isAdmin && showZonePanel" class="zone-panel">
        <div class="zone-panel-header">
          <h2>{{ t('map.safeZones') }}</h2>
          <ToggleSwitch
            v-if="safeZones"
            :model-value="safeZones.enabled"
            :disabled="savingZones"
            :aria-label="t('map.zonesMasterSwitch')"
            @update:model-value="setZonesEnabled"
          />
        </div>
        <p class="zone-help">{{ t('map.safeZonesHelp') }}</p>
        <p v-if="safeZones && !safeZones.enabled" class="zone-warning">
          <i class="pi pi-pause-circle"></i> {{ t('map.zonesPaused') }}
        </p>

        <Button
          v-if="drawStep === 'off'"
          :label="t('map.drawZone')"
          icon="pi pi-plus"
          size="small"
          class="draw-button"
          :disabled="!safeZones"
          @click="startDrawing"
        />
        <Button
          v-else
          :label="t('map.cancelDrawing')"
          icon="pi pi-times"
          size="small"
          severity="secondary"
          class="draw-button"
          @click="stopDrawing"
        />

        <p v-if="safeZones && safeZones.zones.length === 0" class="zone-empty">{{ t('map.noZones') }}</p>
        <ul v-if="safeZones && safeZones.zones.length > 0" class="zone-list">
          <li v-for="zone in safeZones.zones" :key="zone.id" class="zone-item">
            <div class="zone-item-main">
              <button type="button" class="zone-name" :title="t('map.zoneShow')" @click="focusZone(zone)">
                {{ zone.name }}
              </button>
              <div class="zone-meta">
                {{ t('map.zoneBlocks', { size: zoneSize(zone) }) }}
                · {{ t('map.zoneRemoved', { count: zone.removedCount ?? 0 }) }}
              </div>
            </div>
            <ToggleSwitch
              :model-value="zone.enabled"
              :disabled="savingZones"
              :aria-label="t('map.zoneEnabled')"
              @update:model-value="(v: boolean) => setZoneEnabled(zone, v)"
            />
            <Button icon="pi pi-pencil" text rounded size="small" :aria-label="t('common.edit')" @click="openEdit(zone)" />
            <Button
              icon="pi pi-trash"
              text
              rounded
              size="small"
              severity="danger"
              :aria-label="t('common.delete')"
              @click="deleteZone(zone)"
            />
          </li>
        </ul>
      </aside>
    </div>

    <Dialog
      v-model:visible="editDialogVisible"
      :header="editIsNew ? t('map.newZone') : t('map.editZone')"
      modal
      :style="{ width: '28rem', maxWidth: '95vw' }"
    >
      <div v-if="editZone" class="zone-form">
        <label class="field">
          <span>{{ t('common.name') }}</span>
          <InputText v-model="editZone.name" :placeholder="t('map.zoneNamePlaceholder')" maxlength="64" autofocus />
        </label>

        <div class="coord-grid">
          <label class="field">
            <span>{{ t('map.zoneWestX') }}</span>
            <InputNumber v-model="editZone.minX" :use-grouping="false" :min="-20000" :max="20000" />
          </label>
          <label class="field">
            <span>{{ t('map.zoneEastX') }}</span>
            <InputNumber v-model="editZone.maxX" :use-grouping="false" :min="-20000" :max="20000" />
          </label>
          <label class="field">
            <span>{{ t('map.zoneSouthZ') }}</span>
            <InputNumber v-model="editZone.minZ" :use-grouping="false" :min="-20000" :max="20000" />
          </label>
          <label class="field">
            <span>{{ t('map.zoneNorthZ') }}</span>
            <InputNumber v-model="editZone.maxZ" :use-grouping="false" :min="-20000" :max="20000" />
          </label>
        </div>

        <div class="toggle-row">
          <div>{{ t('map.zoneEnabled') }}</div>
          <ToggleSwitch v-model="editZone.enabled" />
        </div>
        <div class="toggle-row">
          <div>
            <div>{{ t('map.zoneRemoveAnimals') }}</div>
            <small>{{ t('map.zoneRemoveAnimalsHelp') }}</small>
          </div>
          <ToggleSwitch v-model="editZone.removeAnimals" />
        </div>
        <div class="toggle-row">
          <div>
            <div>{{ t('map.zoneAllowBloodMoon') }}</div>
            <small>{{ t('map.zoneAllowBloodMoonHelp') }}</small>
          </div>
          <ToggleSwitch v-model="editZone.allowBloodMoon" />
        </div>
      </div>

      <template #footer>
        <Button
          v-if="!editIsNew && editZone"
          :label="t('common.delete')"
          icon="pi pi-trash"
          severity="danger"
          text
          class="dialog-delete"
          @click="deleteZone(editZone)"
        />
        <Button :label="t('common.cancel')" severity="secondary" text @click="editDialogVisible = false" />
        <Button :label="t('common.save')" icon="pi pi-check" :loading="savingZones" @click="saveEditedZone" />
      </template>
    </Dialog>
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

.zone-toggle {
  margin-left: auto;
}

.map-body {
  flex: 1;
  display: flex;
  gap: 1rem;
  min-height: 0;
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

.map-container.drawing,
.map-container.drawing :deep(.leaflet-interactive) {
  cursor: crosshair;
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

.draw-hint {
  position: absolute;
  top: 10px;
  left: 50%;
  transform: translateX(-50%);
  z-index: 1001;
  display: flex;
  align-items: center;
  gap: 0.5rem;
  padding: 0.35rem 0.5rem 0.35rem 0.85rem;
  border-radius: 999px;
  background: var(--kc-bg-card, #1e1e2e);
  border: 1px solid #4ade80;
  color: var(--kc-text-primary, #e0e0e0);
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.35);
  max-width: calc(100% - 20px);
  font-size: 0.875rem;
  cursor: default;
}

.zone-panel {
  width: 300px;
  flex-shrink: 0;
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
  padding: 1rem;
  border-radius: 8px;
  border: 1px solid var(--kc-border);
  background: var(--kc-bg-card, #1e1e2e);
  overflow-y: auto;
}

.zone-panel-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 0.5rem;
}

.zone-panel-header h2 {
  font-size: 1.1rem;
  font-weight: 600;
  margin: 0;
}

.zone-help,
.zone-empty {
  font-size: 0.85rem;
  color: var(--kc-text-secondary);
  margin: 0;
  line-height: 1.4;
}

.zone-warning {
  font-size: 0.85rem;
  color: var(--kc-orange);
  margin: 0;
}

.draw-button {
  align-self: flex-start;
}

.zone-list {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
}

.zone-item {
  display: flex;
  align-items: center;
  gap: 0.25rem;
  padding: 0.4rem 0;
  border-top: 1px solid var(--kc-border);
}

.zone-item-main {
  flex: 1;
  min-width: 0;
}

.zone-name {
  all: unset;
  cursor: pointer;
  font-weight: 600;
  display: block;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.zone-name:hover,
.zone-name:focus-visible {
  color: #4ade80;
  text-decoration: underline;
}

.zone-meta {
  font-size: 0.75rem;
  color: var(--kc-text-secondary);
}

.zone-form {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.field {
  display: flex;
  flex-direction: column;
  gap: 0.35rem;
  font-size: 0.875rem;
}

.field :deep(.p-inputnumber),
.field :deep(.p-inputnumber-input) {
  width: 100%;
}

.coord-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 0.75rem;
}

.toggle-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
}

.toggle-row small {
  color: var(--kc-text-secondary);
  line-height: 1.3;
}

.dialog-delete {
  margin-right: auto;
}

@media (max-width: 900px) {
  .map-body { flex-direction: column; }
  .zone-panel { width: auto; max-height: 45vh; }
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
