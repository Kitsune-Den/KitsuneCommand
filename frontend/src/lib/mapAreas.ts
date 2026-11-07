// Geometry for the land claim and safe zone map layers, in world block
// coordinates (X east, Z north). Edges are block edges: an area covering
// blocks 0..10 runs from 0 to 11.

export interface BlockRect {
  minX: number
  minZ: number
  /** Exclusive: the east edge of the last block. */
  maxX: number
  /** Exclusive: the north edge of the last block. */
  maxZ: number
}

/**
 * The area a land claim block protects. The game centres a LandClaimSize-wide
 * square on the block, so a 41-wide claim reaches 20 blocks out each way.
 */
export function claimRect(x: number, z: number, claimSize: number): BlockRect {
  const half = Math.floor(Math.max(1, claimSize) / 2)
  return { minX: x - half, minZ: z - half, maxX: x + half + 1, maxZ: z + half + 1 }
}

/** A safe zone's inclusive block corners as drawable edges. */
export function zoneRect(zone: { minX: number, minZ: number, maxX: number, maxZ: number }): BlockRect {
  return { minX: zone.minX, minZ: zone.minZ, maxX: zone.maxX + 1, maxZ: zone.maxZ + 1 }
}

/** Two clicked map points (any order) as the inclusive block corners of a zone. */
export function cornersToZone(a: { x: number, z: number }, b: { x: number, z: number }) {
  const x1 = Math.floor(a.x), x2 = Math.floor(b.x)
  const z1 = Math.floor(a.z), z2 = Math.floor(b.z)
  return {
    minX: Math.min(x1, x2),
    minZ: Math.min(z1, z2),
    maxX: Math.max(x1, x2),
    maxZ: Math.max(z1, z2),
  }
}

/** A steady colour per claim owner, so one player's claims match each other. */
export function ownerColor(key: string): string {
  let hash = 0
  for (let i = 0; i < key.length; i++) {
    hash = (hash * 31 + key.charCodeAt(i)) | 0
  }
  const hue = Math.abs(hash) % 360
  return `hsl(${hue}, 85%, 60%)`
}
