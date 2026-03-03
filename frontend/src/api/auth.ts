import apiClient from './client'

export interface TokenResponse {
  access_token: string
  token_type: string
  expires_in: number
  refresh_token?: string
  username: string
  role: string
  display_name: string
}

export async function login(username: string, password: string): Promise<TokenResponse> {
  const params = new URLSearchParams()
  params.append('grant_type', 'password')
  params.append('username', username)
  params.append('password', password)

  const response = await apiClient.post<TokenResponse>('/token', params, {
    headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
  })

  return response.data
}

export async function refreshToken(token: string): Promise<TokenResponse> {
  const params = new URLSearchParams()
  params.append('grant_type', 'refresh_token')
  params.append('refresh_token', token)

  const response = await apiClient.post<TokenResponse>('/token', params, {
    headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
  })

  return response.data
}

export interface UserInfo {
  username: string
  role: string
  userId: string
  displayName: string
}

export async function getCurrentUser(): Promise<UserInfo> {
  const response = await apiClient.get('/api/auth/me')
  return response.data.data
}

export async function changePassword(currentPassword: string, newPassword: string): Promise<void> {
  await apiClient.post('/api/auth/change-password', { currentPassword, newPassword })
}
