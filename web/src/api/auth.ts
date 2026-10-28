// 账号与会话 API：登录、退出、会话恢复。
// 会话通过 HttpOnly Cookie 维持，所有请求携带凭据（credentials: 'include'）。

import { ApiError, API_BASE_URL, isRequestCancelled, parseEnvelope, REQUEST_TIMEOUT_MS, toNetworkError } from './issues'

/** 当前用户信息（不含任何凭据） */
export interface CurrentUser {
  username: string
  displayName: string
  role: 'inspector' | 'admin'
}

/** 会话类错误：401 时调用方应回到登录页 */
export function isAuthenticationError(error: unknown): boolean {
  return error instanceof ApiError && error.status === 401
}

async function authRequest(path: string, init: RequestInit, signal?: AbortSignal): Promise<unknown> {
  const controller = new AbortController()
  let timedOut = false
  const timer = setTimeout(() => {
    timedOut = true
    controller.abort()
  }, REQUEST_TIMEOUT_MS)
  const onExternalAbort = () => controller.abort()
  signal?.addEventListener('abort', onExternalAbort)
  if (signal?.aborted) controller.abort()
  try {
    const response = await fetch(`${API_BASE_URL}${path}`, { ...init, signal: controller.signal, credentials: 'include' })
    return await parseEnvelope(response)
  } catch (error) {
    if (timedOut) {
      throw new ApiError('timeout', `请求超时（${REQUEST_TIMEOUT_MS / 1000} 秒），请检查网络后重试`, 0)
    }
    if (isRequestCancelled(error) || signal?.aborted) throw error
    throw toNetworkError(error)
  } finally {
    clearTimeout(timer)
    signal?.removeEventListener('abort', onExternalAbort)
  }
}

/** 登录：成功返回当前用户；凭据错误抛 401 ApiError（message 可直接展示） */
export async function login(username: string, password: string, signal?: AbortSignal): Promise<CurrentUser> {
  const data = await authRequest('/api/auth/login', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ username, password }),
  }, signal)
  return parseCurrentUser(data)
}

/** 退出：幂等成功，同时清除浏览器会话 Cookie */
export async function logout(signal?: AbortSignal): Promise<void> {
  await authRequest('/api/auth/logout', { method: 'POST' }, signal)
}

/**
 * 开发期快捷登录：按角色为内置演示账号签发会话，不传密码。
 * 仅当后端显式启用 QUICK_LOGIN_ENABLED=1 时可用；生产构建不展示入口。
 */
export async function quickLogin(role: 'inspector' | 'admin', signal?: AbortSignal): Promise<CurrentUser> {
  const data = await authRequest('/api/auth/quick-login', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ role }),
  }, signal)
  return parseCurrentUser(data)
}

/** 恢复登录状态：有效返回当前用户，无会话/过期抛 401 */
export async function fetchCurrentUser(signal?: AbortSignal): Promise<CurrentUser> {
  const data = await authRequest('/api/auth/me', { method: 'GET' }, signal)
  return parseCurrentUser(data)
}

function parseCurrentUser(value: unknown): CurrentUser {
  if (
    typeof value !== 'object' ||
    value === null ||
    Array.isArray(value)
  ) {
    throw new ApiError('bad_response', '后端返回的数据格式不正确', 0)
  }
  const { username, displayName, role } = value as Record<string, unknown>
  if (
    typeof username !== 'string' ||
    typeof displayName !== 'string' ||
    (role !== 'inspector' && role !== 'admin')
  ) {
    throw new ApiError('bad_response', '后端返回的数据格式不正确', 0)
  }
  return { username, displayName, role }
}
