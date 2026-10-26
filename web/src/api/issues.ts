// API 基础模块：统一基础地址与响应解析。
// 本期为骨架阶段，不发起真实请求、不接入模拟数据；
// 列表获取与状态修改在后续业务计划中基于此模块实现。

/** 后端 API 基础地址；浏览器与后端同机时默认 http://localhost:8080 */
export const API_BASE_URL: string = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:8080'

/** 统一错误结构对应的客户端错误 */
export class ApiError extends Error {
  /** 服务端错误码，如 invalid_argument、service_unavailable */
  readonly code: string
  /** HTTP 状态码；网络断开等无响应场景为 0 */
  readonly status: number

  constructor(code: string, message: string, status: number) {
    super(message)
    this.name = 'ApiError'
    this.code = code
    this.status = status
  }
}

/**
 * 解析统一响应结构：先判断 HTTP 状态，再读取 `{ data }` 或 `{ error: { code, message } }`。
 * 网络断开、连接失败和超时没有 HTTP 响应，调用方需转换为明确错误提示，不能当作成功或空数据。
 */
export async function parseEnvelope<T>(response: Response): Promise<T> {
  if (!response.ok) {
    let code = 'unknown_error'
    let message = `请求失败（HTTP ${response.status}）`
    try {
      const body = (await response.json()) as { error?: { code?: string; message?: string } }
      if (body.error?.code) code = body.error.code
      if (body.error?.message) message = body.error.message
    } catch {
      // 响应体不是 JSON 时保留默认错误信息
    }
    throw new ApiError(code, message, response.status)
  }
  const body = (await response.json()) as { data?: T }
  return body.data as T
}

/** 把 fetch 网络异常（断开、超时）转换为统一的 ApiError，status 为 0 */
export function toNetworkError(error: unknown): ApiError {
  if (error instanceof ApiError) return error
  const message = error instanceof Error ? error.message : String(error)
  return new ApiError('network_error', `无法连接后端：${message}`, 0)
}
