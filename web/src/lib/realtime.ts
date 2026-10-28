// 实时同步核心：事件判定、列表合并与 WebSocket 连接封装。
// 判定与合并为纯函数，供事件序单元测试直接覆盖；连接类接受注入的 socket 工厂，
// 测试使用假 socket，不依赖真实网络时序。

import type { Issue } from '../types/issue'

/** 后端推送的实时事件结构 */
export interface RealtimeEvent {
  type: 'issue.created' | 'issue.updated'
  seq: number
  issue: Issue
}

/** 实时连接状态 */
export type RealtimeStatus = 'connecting' | 'connected' | 'reconnecting' | 'closed'

/**
 * 事件判定：结合上一条已接受序号决定处理方式。
 * - apply：接受并应用（首条、或恰好是下一条）
 * - duplicate：重复/过期事件（seq 不大于已接受值），忽略
 * - resync：跳号（seq 超过 lastSeq+1），本地有缺口，需要全量校准
 * 进程重启导致的序号回退由「重连后先全量校准并重置 lastSeq」覆盖，
 * 校准后 lastSeq 为 null，第一条事件总是 apply。
 */
export function decideEventAction(lastSeq: number | null, event: RealtimeEvent): 'apply' | 'duplicate' | 'resync' {
  if (event.seq <= 0) return 'resync'
  if (lastSeq === null) return 'apply'
  if (event.seq === lastSeq + 1) return 'apply'
  if (event.seq <= lastSeq) return 'duplicate'
  return 'resync'
}

/** 后端稳定排序比较：createdAt 从新到旧，同时间按 id，与列表接口一致 */
function isNewerThan(a: Issue, b: Issue): boolean {
  if (a.createdAt !== b.createdAt) return a.createdAt > b.createdAt
  return a.id > b.id
}

/**
 * 把事件中的问题对象合并进列表，返回新列表；
 * updated 事件目标行不存在时返回 null（调用方应触发全量校准，不伪造行）。
 */
export function mergeIssueEvent(list: Issue[], event: RealtimeEvent): Issue[] | null {
  const incoming = event.issue
  const index = list.findIndex(item => item.id === incoming.id)
  if (event.type === 'issue.created') {
    if (index >= 0) {
      // 已存在：按 updated 处理（可能创建事件迟到但行已由校准获得）
      const next = list.slice()
      next[index] = incoming
      return next
    }
    // 按 createdAt 从新到旧插入，保持后端稳定顺序：插在第一个更旧的行之前
    const insertAt = list.findIndex(item => isNewerThan(incoming, item))
    if (insertAt === -1) return [...list, incoming]
    return [...list.slice(0, insertAt), incoming, ...list.slice(insertAt)]
  }
  // issue.updated
  if (index < 0) return null
  const next = list.slice()
  next[index] = incoming
  return next
}

/** 运行时结构校验：事件必须是合法类型、正整数序号与完整问题对象 */
export function parseRealtimeEvent(raw: unknown): RealtimeEvent | null {
  if (typeof raw !== 'object' || raw === null) return null
  const { type, seq, issue } = raw as Record<string, unknown>
  if (type !== 'issue.created' && type !== 'issue.updated') return null
  if (typeof seq !== 'number' || !Number.isInteger(seq) || seq <= 0) return null
  if (!isValidIssue(issue)) return null
  return { type, seq, issue }
}

function isValidIssue(value: unknown): value is Issue {
  if (typeof value !== 'object' || value === null) return false
  const v = value as Record<string, unknown>
  const position = v.position as Record<string, unknown> | undefined
  return (
    typeof v.id === 'string' &&
    v.id !== '' &&
    typeof v.title === 'string' &&
    typeof v.description === 'string' &&
    (v.priority === 'low' || v.priority === 'medium' || v.priority === 'high') &&
    (v.status === 'open' || v.status === 'in_progress' || v.status === 'resolved') &&
    typeof position === 'object' &&
    position !== null &&
    typeof position.x === 'number' &&
    Number.isFinite(position.x) &&
    typeof position.y === 'number' &&
    Number.isFinite(position.y) &&
    typeof position.z === 'number' &&
    Number.isFinite(position.z) &&
    typeof v.createdAt === 'string' &&
    typeof v.updatedAt === 'string'
  )
}

/** 连接句柄：stop 时用于主动关闭；具体 socket 类型由工厂封装 */
export interface SocketHandle {
  close(): void
}

/** socket 事件回调：工厂负责把回调挂到底层连接上 */
export interface SocketHandlers {
  onOpen: () => void
  onClose: () => void
  onError: () => void
  onMessage: (event: { data: unknown }) => void
}

export interface RealtimeClientOptions {
  /** 建立连接并挂载回调；抛错视为一次连接失败 */
  connect: (handlers: SocketHandlers) => SocketHandle
  /** 事件到达（已通过结构校验）；序号判定与合并由回调内部完成 */
  onEvent: (event: RealtimeEvent) => void
  /** 连接状态变化 */
  onStatus: (status: RealtimeStatus) => void
  /** 重连基础延迟与上限（毫秒），指数退避 */
  baseDelayMs?: number
  maxDelayMs?: number
  /** 定时器来源，测试注入假时钟；默认 setTimeout */
  schedule?: (fn: () => void, ms: number) => number
  cancelSchedule?: (id: number) => void
}

/**
 * 实时连接封装：有限退避自动重连；每次连接成功后调用方应全量校准并重置序号。
 * 生命周期由 start()/stop() 控制；stop 后不再重连。
 */
export class RealtimeClient {
  private handle: SocketHandle | null = null
  private stopped = true
  private attempts = 0
  private retryTimer: number | null = null
  private readonly options: RealtimeClientOptions

  constructor(options: RealtimeClientOptions) {
    this.options = options
  }

  start(): void {
    if (!this.stopped) return
    this.stopped = false
    this.attempts = 0
    this.open('connecting')
  }

  stop(): void {
    this.stopped = true
    this.clearRetryTimer()
    if (this.handle) {
      this.handle.close()
      this.handle = null
    }
    this.options.onStatus('closed')
  }

  /** 立即重连（供“会话刷新后重试”），不等待退避 */
  reconnectNow(): void {
    if (this.stopped) return
    this.clearRetryTimer()
    if (this.handle) {
      this.handle.close()
      this.handle = null
    }
    this.open('reconnecting')
  }

  private open(initialStatus: RealtimeStatus): void {
    this.options.onStatus(initialStatus)
    let handle: SocketHandle
    try {
      handle = this.options.connect({
        onOpen: () => {
          this.attempts = 0
          this.options.onStatus('connected')
        },
        onMessage: event => this.handleMessage(event),
        onError: () => {
          // 浏览器只在连接失败时给 error；close 随后触发，统一在 onClose 处理重连
        },
        onClose: () => {
          if (this.stopped) return
          this.handle = null
          this.scheduleReconnect()
        },
      })
    } catch {
      this.scheduleReconnect()
      return
    }
    this.handle = handle
  }

  private handleMessage(event: { data: unknown }): void {
    let parsed: unknown
    try {
      parsed = JSON.parse(typeof event.data === 'string' ? event.data : '')
    } catch {
      return // 非 JSON 帧直接忽略，不破坏连接
    }
    const realtimeEvent = parseRealtimeEvent(parsed)
    if (!realtimeEvent) return
    this.options.onEvent(realtimeEvent)
  }

  private scheduleReconnect(): void {
    this.options.onStatus('reconnecting')
    const base = this.options.baseDelayMs ?? 1000
    const max = this.options.maxDelayMs ?? 30000
    const delay = Math.min(base * 2 ** this.attempts, max)
    this.attempts = Math.min(this.attempts + 1, 20)
    this.clearRetryTimer()
    const schedule = this.options.schedule ?? ((fn: () => void, ms: number) => setTimeout(fn, ms) as unknown as number)
    this.retryTimer = schedule(() => {
      this.retryTimer = null
      if (!this.stopped) this.open('reconnecting')
    }, delay)
  }

  private clearRetryTimer(): void {
    if (this.retryTimer !== null) {
      const cancel = this.options.cancelSchedule ?? clearTimeout
      cancel(this.retryTimer)
      this.retryTimer = null
    }
  }

}

/** 由后端基础地址推导 WebSocket 地址（http→ws / https→wss；空地址=按当前页面同源推导） */
export function websocketUrl(apiBaseUrl: string): string {
  const base = apiBaseUrl.trim()
  if (base.startsWith('https://')) return `wss://${base.slice('https://'.length)}/api/ws`
  if (base.startsWith('http://')) return `ws://${base.slice('http://'.length)}/api/ws`
  // WebSocket 构造器只接受绝对 URL：空基础地址（同源部署）按当前页面推导
  const proto = window.location.protocol === 'https:' ? 'wss:' : 'ws:'
  return `${proto}//${window.location.host}/api/ws`
}
