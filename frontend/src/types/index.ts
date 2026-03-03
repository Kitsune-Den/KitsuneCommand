export interface PlayerInfo {
  playerId: string
  entityId: number
  playerName: string
  platformId: string
  positionX: number
  positionY: number
  positionZ: number
  level: number
  health: number
  stamina: number
  zombieKills: number
  playerKills: number
  deaths: number
  totalPlayTime: number
  ip: string
  isOnline: boolean
  score: number
  lastLogin: number
  isAdmin: boolean
}

export interface InventorySlot {
  slotIndex: number
  itemName: string
  count: number
  quality: number
  durability: number
  maxDurability: number
  iconName: string
}

export interface PlayerSkillInfo {
  name: string
  level: number
  maxLevel: number
  isLocked: boolean
}

export interface PlayerDetailInfo extends PlayerInfo {
  bagItems: InventorySlot[]
  beltItems: InventorySlot[]
  skills: PlayerSkillInfo[]
}

export interface MapInfo {
  worldSize: number
  maxZoom: number
  tileSize: number
  isAvailable: boolean
  bounds: {
    minX: number
    minZ: number
    maxX: number
    maxZ: number
  }
}

export interface MapMarker {
  entityId: number
  name: string
  x: number
  y: number
  z: number
  type: string
}

export interface PlayerPositionData {
  entityId: number
  playerName: string
  x: number
  y: number
  z: number
}

export interface ChatRecord {
  id: number
  createdAt: string
  playerId: string
  entityId: number
  senderName: string
  chatType: number
  message: string
}

export interface PointsInfo {
  id: string
  createdAt: string
  playerName: string
  points: number
  lastSignInAt: string | null
}

export interface PointsUpdateEvent {
  playerId: string
  playerName: string
  points: number
  change: number
  reason: string
}

export interface GoodsItem {
  id: number
  createdAt: string
  name: string
  price: number
  description: string
}

export interface GoodsDetail extends GoodsItem {
  items: ItemDefinition[]
  commands: CommandDefinition[]
}

export interface ItemDefinition {
  id: number
  createdAt: string
  itemName: string
  count: number
  quality: number
  durability: number
  description: string
}

export interface CommandDefinition {
  id: number
  createdAt: string
  command: string
  runInMainThread: boolean
  description: string
}

export interface PurchaseRecord {
  id: number
  createdAt: string
  playerId: string
  playerName: string
  goodsId: number
  goodsName: string
  price: number
}

// ─── Teleport Types ──────────────────────────────────

export interface CityLocation {
  id: number
  createdAt: string
  cityName: string
  pointsRequired: number
  position: string
  viewDirection: string | null
}

export interface HomeLocation {
  id: number
  createdAt: string
  playerId: string
  playerName: string | null
  homeName: string
  position: string
}

export interface TeleRecord {
  id: number
  createdAt: string
  playerId: string
  playerName: string | null
  targetType: number
  targetName: string | null
  originPosition: string | null
  targetPosition: string | null
}

// ─── Chat Command Settings ──────────────────────────

export interface ChatCommandSettings {
  enabled: boolean
  prefix: string
  defaultCooldownSeconds: number
  homeEnabled: boolean
  maxHomesPerPlayer: number
  homeCooldownSeconds: number
  teleportEnabled: boolean
  teleportCooldownSeconds: number
  pointsEnabled: boolean
  storeEnabled: boolean
}

// ─── CD Key Types ────────────────────────────────────

export interface CdKey {
  id: number
  createdAt: string
  key: string
  maxRedeemCount: number
  expiryAt: string | null
  description: string | null
}

export interface CdKeyDetail extends CdKey {
  currentRedeemCount: number
  items: ItemDefinition[]
  commands: CommandDefinition[]
}

export interface CdKeyRedeemRecord {
  id: number
  createdAt: string
  cdKeyId: number
  playerId: string
  playerName: string | null
}
