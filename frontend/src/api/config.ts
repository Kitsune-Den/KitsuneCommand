import apiClient from './client'

export interface ConfigFieldDef {
  key: string
  type: 'text' | 'number' | 'bool' | 'select' | 'password'
  defaultValue: string
  min?: number
  max?: number
  options?: string[]
  labels?: string[]
  description?: string
  /** True when 7D2D 3.0 governs this setting via the SandboxCode; hidden in the editor on 3.0. */
  sandboxGoverned?: boolean
}

export interface ConfigFieldGroup {
  key: string
  fields: ConfigFieldDef[]
}

export interface ConfigResponse {
  properties: Record<string, string>
  groups: ConfigFieldGroup[]
  configPath: string
  /** True when the running game is 7D2D 3.0+ (supports the SandboxCode system). */
  is30?: boolean
  /** True when serverconfig.xml still has 3.0-deprecated sandbox-governed props to clean up. */
  needsMigration?: boolean
}

export interface MigrateResult {
  changed: boolean
  addedSandboxCode: boolean
  neutralized: string[]
  backupPath: string
  message: string
}

export async function getConfig(): Promise<ConfigResponse> {
  const res = await apiClient.get('/api/config')
  return res.data.data
}

export async function getRawXml(): Promise<string> {
  const res = await apiClient.get('/api/config/raw')
  return res.data.data.xml
}

export async function saveConfig(properties: Record<string, string>): Promise<string> {
  const res = await apiClient.put('/api/config', properties)
  return res.data.message
}

export async function saveRawXml(xml: string): Promise<string> {
  const res = await apiClient.put('/api/config/raw', { xml })
  return res.data.message
}

export async function getWorlds(): Promise<string[]> {
  const res = await apiClient.get('/api/config/worlds')
  return res.data.data
}

/** Migrate serverconfig.xml to the 7D2D 3.0 layout (server backs up first; idempotent). */
export async function migrateConfigTo30(): Promise<MigrateResult> {
  const res = await apiClient.post('/api/config/migrate-3.0')
  return res.data.data
}
