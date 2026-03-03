import apiClient from './client'
import type { MapInfo, MapMarker } from '@/types'

export async function getMapInfo(): Promise<MapInfo> {
  const response = await apiClient.get('/api/map/info')
  return response.data.data
}

export async function getMapMarkers(): Promise<MapMarker[]> {
  const response = await apiClient.get('/api/map/markers')
  return response.data.data
}

export function getTileUrl(z: number, x: number, y: number): string {
  const base = window.location.origin
  return `${base}/api/map/tile/${z}/${x}/${y}`
}
