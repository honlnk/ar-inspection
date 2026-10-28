// 人员管理 API（计划 008）：账号列表、新增巡检员、修改显示名/重置密码、删除。
// 复用 issues.ts 的统一信封解析、超时与错误转换；会话过期（401）由调用方处理。

import { API_BASE_URL, ApiError, isRequestCancelled, parseEnvelope, REQUEST_TIMEOUT_MS, toNetworkError } from './issues'

/** 人员管理页的账号视图；不含密码或哈希 */
export interface ManagedAccount {
  username: string
  displayName: string
  role: 'inspector' | 'admin'
  createdAt: string
  updatedAt: string
}

/** 新增巡检员请求体 */
export interface CreateUserRequest {
  username: string
  displayName: string
  password: string
}

/** 修改巡检员请求体：显示名与新密码均可选，至少一项 */
export interface UpdateUserRequest {
  displayName?: string
  password?: string
}

const ROLE_VALUES: readonly ManagedAccount['role'][] = ['inspector', 'admin']

function badResponseError(): ApiError {
  return new ApiError('bad_response', '后端返回的数据格式不正确', 0)
}

/** 统一超时请求：与 issues.ts 同一风格；DELETE 204 无响应体，需先判断 */
async function requestEnvelope(path: string, init: RequestInit, signal?: AbortSignal): Promise<unknown> {
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
    const response = await fetch(`${API_BASE_URL}/api/users${path}`, { ...init, signal: controller.signal, credentials: 'include' })
    if (response.status === 204) return null
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

/** 运行时结构检查：一条合法账号必须有五个字段与合法角色枚举 */
function parseManagedAccount(value: unknown): ManagedAccount {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) throw badResponseError()
  const { username, displayName, role, createdAt, updatedAt } = value as Record<string, unknown>
  if (
    typeof username !== 'string' ||
    username === '' ||
    typeof displayName !== 'string' ||
    typeof createdAt !== 'string' ||
    typeof updatedAt !== 'string' ||
    !ROLE_VALUES.includes(role as ManagedAccount['role'])
  ) {
    throw badResponseError()
  }
  return { username, displayName, role: role as ManagedAccount['role'], createdAt, updatedAt }
}

/** 获取全部账号列表（含管理员；页面按角色只读展示） */
export async function fetchUsers(signal?: AbortSignal): Promise<ManagedAccount[]> {
  const data = await requestEnvelope('', { method: 'GET' }, signal)
  if (!Array.isArray(data)) throw badResponseError()
  return data.map(parseManagedAccount)
}

/** 新增巡检员；用户名重复或参数非法抛 400 ApiError（中文 message 可直接展示） */
export async function createUser(req: CreateUserRequest, signal?: AbortSignal): Promise<ManagedAccount> {
  const data = await requestEnvelope('', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(req),
  }, signal)
  return parseManagedAccount(data)
}

/**
 * 修改巡检员：传 displayName 只改名；传 password 重置密码（该账号全部会话失效）。
 * 两者均可选但至少一项，由后端校验。
 */
export async function updateUser(username: string, req: UpdateUserRequest, signal?: AbortSignal): Promise<ManagedAccount> {
  const data = await requestEnvelope(`/${encodeURIComponent(username)}`, {
    method: 'PATCH',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(req),
  }, signal)
  return parseManagedAccount(data)
}

/** 删除巡检员；目标不存在抛 404、管理员账号抛 403，均为 ApiError */
export async function deleteUser(username: string, signal?: AbortSignal): Promise<void> {
  await requestEnvelope(`/${encodeURIComponent(username)}`, { method: 'DELETE' }, signal)
}
