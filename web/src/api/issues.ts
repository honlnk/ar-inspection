// API 基础模块：统一基础地址、请求超时、信封解析、结构检查与网络异常转换。
// 列表获取与状态保存共用本模块；组件层不得绕开这里直接 fetch。

import type { Issue, Priority, Status, StatusUpdateRequest } from '../types/issue'

/** 后端 API 基础地址；浏览器与后端同机时默认 http://localhost:8080 */
export const API_BASE_URL: string = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:8080'

/** 列表与状态保存的统一超时（毫秒）；超时按失败处理，由用户手动重试，不自动反复请求 */
export const REQUEST_TIMEOUT_MS = 10_000

/** 统一错误结构及本地网络/超时/结构错误对应的客户端错误 */
export class ApiError extends Error {
  /** 服务端错误码（如 invalid_argument），或本地错误类别：network_error / timeout / bad_response */
  readonly code: string
  /** HTTP 状态码；网络断开、超时、响应结构错误等无有效响应场景为 0 */
  readonly status: number

  constructor(code: string, message: string, status: number) {
    super(message)
    this.name = 'ApiError'
    this.code = code
    this.status = status
  }
}

const PRIORITY_VALUES: readonly Priority[] = ['low', 'medium', 'high']
const STATUS_VALUES: readonly Status[] = ['open', 'in_progress', 'resolved']

function isPlainObject(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

/** HTTP 成功但响应不符合统一结构或问题契约时使用的错误；不暴露响应正文 */
function badResponseError(): ApiError {
  return new ApiError('bad_response', '后端返回的数据格式不正确', 0)
}

/** 判断是否为调用方主动取消（组件卸载等）；这类异常由调用方决定忽略，不算失败 */
export function isRequestCancelled(error: unknown): boolean {
  return error instanceof DOMException && error.name === 'AbortError'
}

/**
 * 解析统一响应结构：先判断 HTTP 状态，再读取 `{ data }` 或 `{ error: { code, message } }`。
 * 成功响应必须是 JSON 对象且带 data 字段，返回 data 的原始值交由上层做结构检查；
 * 错误响应优先透出服务端中文 message，响应体不是 JSON 时保留含状态码的默认提示。
 */
export async function parseEnvelope(response: Response): Promise<unknown> {
  if (!response.ok) {
    let code = 'unknown_error'
    let message = `请求失败（HTTP ${response.status}）`
    try {
      const body = (await response.json()) as { error?: { code?: string; message?: string } }
      if (body.error?.code) code = body.error.code
      if (body.error?.message) message = body.error.message
    } catch {
      // 错误响应体不是 JSON 时保留默认错误信息
    }
    throw new ApiError(code, message, response.status)
  }
  let body: unknown
  try {
    body = await response.json()
  } catch {
    throw badResponseError()
  }
  if (!isPlainObject(body) || !('data' in body)) throw badResponseError()
  return body.data
}

/**
 * 发起带超时的 JSON 请求并解析统一信封。
 * 超时后中止请求并抛出 timeout 类别的 ApiError；外部 signal 中止时原样抛出
 * AbortError，由调用方按取消处理；其余网络异常转换为 network_error。
 */
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
    const response = await fetch(`${API_BASE_URL}${path}`, { ...init, signal: controller.signal })
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

/** 轻量运行时结构检查：一条合法问题必须有 8 个契约字段、合法枚举与有限数值坐标 */
function parseIssue(value: unknown): Issue {
  if (!isPlainObject(value)) throw badResponseError()
  const { id, title, description, priority, status, position, createdAt, updatedAt } = value
  if (
    typeof id !== 'string' ||
    id === '' ||
    typeof title !== 'string' ||
    typeof description !== 'string' ||
    typeof createdAt !== 'string' ||
    typeof updatedAt !== 'string' ||
    !isPlainObject(position)
  ) {
    throw badResponseError()
  }
  const { x, y, z } = position
  if (
    typeof x !== 'number' ||
    !Number.isFinite(x) ||
    typeof y !== 'number' ||
    !Number.isFinite(y) ||
    typeof z !== 'number' ||
    !Number.isFinite(z)
  ) {
    throw badResponseError()
  }
  if (!PRIORITY_VALUES.includes(priority as Priority) || !STATUS_VALUES.includes(status as Status)) {
    throw badResponseError()
  }
  return {
    id,
    title,
    description,
    priority: priority as Priority,
    status: status as Status,
    position: { x, y, z },
    createdAt,
    updatedAt,
  }
}

/** 获取问题列表；data 必须是数组（空数组合法），逐条做结构检查 */
export async function fetchIssues(signal?: AbortSignal): Promise<Issue[]> {
  const data = await requestEnvelope('/api/issues', { method: 'GET' }, signal)
  if (!Array.isArray(data)) throw badResponseError()
  return data.map(parseIssue)
}

/** 修改问题处理状态；只发送 { status }，成功返回服务端完整问题 */
export async function updateIssueStatus(id: string, status: Status, signal?: AbortSignal): Promise<Issue> {
  const body: StatusUpdateRequest = { status }
  const data = await requestEnvelope(
    `/api/issues/${encodeURIComponent(id)}/status`,
    {
      method: 'PATCH',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    },
    signal,
  )
  return parseIssue(data)
}

/** 把 fetch 网络异常（断开、DNS 失败等）转换为统一的 ApiError，status 为 0 */
export function toNetworkError(error: unknown): ApiError {
  if (error instanceof ApiError) return error
  const message = error instanceof Error ? error.message : String(error)
  return new ApiError('network_error', `无法连接后端：${message}`, 0)
}
