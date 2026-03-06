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
}

export interface ConfigFieldGroup {
  key: string
  fields: ConfigFieldDef[]
}

export interface ConfigResponse {
  properties: Record<string, string>
  groups: ConfigFieldGroup[]
  configPath: string
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
