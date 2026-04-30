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
  adminLevel: number
}

export interface InventorySlot {
  slotIndex: number
  itemName: string
  displayName: string
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

// ─── Points Settings ────────────────────────────────

export interface PointsSettings {
  zombieKillPoints: number
  playerKillPoints: number
  signInBonus: number
  playtimePointsPerHour: number
  playtimeIntervalMinutes: number
}

// ─── Teleport Settings ─────────────────────────────

export interface TeleportSettings {
  teleportDelaySeconds: number
  defaultPointsCost: number
  allowTeleportDuringBloodMoon: boolean
}

// ─── Store Settings ────────────────────────────────

export interface StoreSettings {
  purchaseCooldownSeconds: number
  maxDailyPurchases: number
  priceMultiplier: number
}

// ─── Dashboard Stats ───────────────────────────────

export interface DashboardStats {
  totalPlayers: number
  totalPointsInCirculation: number
  totalPurchases: number
  totalPointsSpent: number
  totalStoreItems: number
  totalTeleports: number
  totalCities: number
  totalCdKeys: number
  totalRedemptions: number
  totalVipGifts: number
  totalSchedules: number
  activeSchedules: number
  totalChatMessages: number
}

// ─── Blood Moon Vote Types ──────────────────────────

export interface BloodMoonVoteStatus {
  isActive: boolean
  currentVotes: number
  requiredVotes: number
  totalOnline: number
  voters: string[]
  bloodMoonDay: number
  isEnabled: boolean
}

export interface BloodMoonVoteSettings {
  enabled: boolean
  thresholdType: string
  thresholdValue: number
  cooldownMinutes: number
  allowVoteHoursBefore: number
  allowVoteDuringBloodMoon: boolean
  commandName: string
  voteRegisteredMessage: string
  alreadyVotedMessage: string
  voteNotActiveMessage: string
  voteSuccessMessage: string
  featureDisabledMessage: string
  onCooldownMessage: string
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
  vipEnabled: boolean
  ticketEnabled: boolean
  ticketCooldownSeconds: number
  voteEnabled: boolean
  voteCooldownSeconds: number
}

// ─── Vote Rewards Types ──────────────────────────────

/** Reward delivery shape — mirrors the C# VoteRewardType static class. */
export const VOTE_REWARD_TYPE = {
  POINTS: 'points',
  VIP_GIFT: 'vip_gift',
  CD_KEY: 'cd_key',
} as const

export type VoteRewardType = typeof VOTE_REWARD_TYPE[keyof typeof VOTE_REWARD_TYPE]

export interface VoteProviderSettings {
  key: string
  enabled: boolean
  apiKey: string
  serverId: string
  pollIntervalMinutes: number
  rewardType: VoteRewardType
  pointsAmount: number
  vipGiftTemplateName: string
  cdKeyTemplateId: number
  broadcastTemplate: string
}

export interface VoteRewardsSettings {
  enabled: boolean
  providers: VoteProviderSettings[]
}

export interface VoteGrant {
  id: number
  provider: string
  steamId: string
  playerName: string | null
  voteDate: string
  rewardType: string
  rewardValue: string
  grantedAt: string
  notes: string | null
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

// ─── VIP Gift Types ─────────────────────────────────

export interface VipGift {
  id: number
  createdAt: string
  playerId: string
  playerName: string | null
  name: string
  claimed: number
  totalClaimCount: number
  lastClaimedAt: string | null
  description: string | null
  claimPeriod: string | null
  isClaimable: boolean
}

export interface VipGiftDetail extends VipGift {
  items: ItemDefinition[]
  commands: CommandDefinition[]
}

// ─── Task Schedule Types ────────────────────────────

export interface TaskSchedule {
  id: number
  createdAt: string
  name: string
  cronExpression: string
  isEnabled: number
  lastRunAt: string | null
  description: string | null
  intervalMinutes: number
}

export interface TaskScheduleDetail extends TaskSchedule {
  nextRunAt: string | null
  commands: CommandDefinition[]
}

// ─── Game Item Catalog ─────────────────────────────

export interface GameItemInfo {
  id: number
  itemName: string
  displayName: string
  iconName: string
  hasQuality: boolean
  maxStack: number
  groups: string[]
}

// ─── Ticket Types ────────────────────────────────────

export interface Ticket {
  id: number
  createdAt: string
  updatedAt: string
  playerId: string
  playerName: string | null
  subject: string
  status: 'open' | 'in_progress' | 'closed'
  priority: number
  assignedTo: string | null
}

export interface TicketMessage {
  id: number
  createdAt: string
  ticketId: number
  senderType: 'player' | 'admin'
  senderId: string | null
  senderName: string | null
  message: string
  delivered: number
}

export interface TicketDetail extends Ticket {
  messages: TicketMessage[]
}

export interface TicketSettings {
  enabled: boolean
  maxOpenTicketsPerPlayer: number
  cooldownSeconds: number
  discordWebhookUrl: string
  discordNotifyOnCreate: boolean
  discordNotifyOnReply: boolean
  discordNotifyOnClose: boolean
}

export interface TicketStats {
  openCount: number
  inProgressCount: number
  closedCount: number
}

// ─── Discord Bot ─────────────────────────────────────
export interface DiscordSettings {
  enabled: boolean
  botToken: string
  chatBridgeEnabled: boolean
  chatBridgeChannelId: string
  eventNotificationsEnabled: boolean
  eventChannelId: string
  notifyPlayerJoin: boolean
  notifyPlayerLeave: boolean
  notifyServerStart: boolean
  notifyServerStop: boolean
  notifyBloodMoon: boolean
  slashCommandsEnabled: boolean
  serverName: string
  showPlayerCountInStatus: boolean
}

export interface DiscordStatus {
  isConnected: boolean
  botUsername: string
  latencyMs: number
}

// ─── Player Metadata ─────────────────────────────────
export interface PlayerMetadata {
  playerId: string
  nameColor: string | null
  customTag: string | null
  notes: string | null
  updatedAt: string
}
