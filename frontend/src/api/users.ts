import apiClient from './client'

export interface UserResponse {
  id: number
  username: string
  displayName: string
  role: string
  lastLoginAt: string | null
  isActive: boolean
  createdAt: string
}

export interface CreateUserRequest {
  username: string
  password: string
  displayName?: string
  role?: string
}

export interface UpdateUserRequest {
  displayName?: string
  role?: string
  isActive?: boolean
}

export async function getUsers(): Promise<UserResponse[]> {
  const response = await apiClient.get('/api/users')
  return response.data.data
}

export async function getUser(id: number): Promise<UserResponse> {
  const response = await apiClient.get(`/api/users/${id}`)
  return response.data.data
}

export async function createUser(request: CreateUserRequest): Promise<UserResponse> {
  const response = await apiClient.post('/api/users', request)
  return response.data.data
}

export async function updateUser(id: number, request: UpdateUserRequest): Promise<UserResponse> {
  const response = await apiClient.put(`/api/users/${id}`, request)
  return response.data.data
}

export async function resetUserPassword(id: number, newPassword: string): Promise<void> {
  await apiClient.post(`/api/users/${id}/reset-password`, { newPassword })
}

export async function deleteUser(id: number): Promise<void> {
  await apiClient.delete(`/api/users/${id}`)
}
