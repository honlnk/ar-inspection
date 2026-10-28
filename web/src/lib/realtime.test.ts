// 事件序测试：全部使用可控假 socket/假定时器，不依赖真实网络时序。
// 覆盖计划 006 第 4.2 节第 7 条：创建后更新、更新先于创建到达、重复事件、
// 乱序响应、断线重连；过期会话在 App 层测试覆盖。

import { describe, expect, it, vi } from 'vitest'
import type { Issue } from '../types/issue'
import {
  decideEventAction,
  mergeIssueEvent,
  parseRealtimeEvent,
  RealtimeClient,
  websocketUrl,
  type RealtimeEvent,
  type SocketHandle,
  type SocketHandlers,
} from './realtime'

function makeIssue(id: string, overrides: Partial<Issue> = {}): Issue {
  return {
    id,
    title: `问题 ${id}`,
    description: '',
    priority: 'medium',
    status: 'open',
    position: { x: 1, y: 2, z: 3 },
    createdAt: `2026-10-28T10:00:0${id.length % 10}+08:00`,
    updatedAt: `2026-10-28T10:00:0${id.length % 10}+08:00`,
    ...overrides,
  }
}

function makeEvent(type: RealtimeEvent['type'], seq: number, issue: Issue): RealtimeEvent {
  return { type, seq, issue }
}

describe('decideEventAction 事件序判定', () => {
  it('首条事件（lastSeq 为 null）接受', () => {
    expect(decideEventAction(null, makeEvent('issue.created', 5, makeIssue('a')))).toBe('apply')
  })

  it('恰好下一条接受', () => {
    expect(decideEventAction(3, makeEvent('issue.updated', 4, makeIssue('a')))).toBe('apply')
  })

  it('重复事件（seq 等于已接受值）忽略', () => {
    expect(decideEventAction(3, makeEvent('issue.created', 3, makeIssue('a')))).toBe('duplicate')
  })

  it('过期事件（seq 小于已接受值）忽略', () => {
    expect(decideEventAction(10, makeEvent('issue.updated', 7, makeIssue('a')))).toBe('duplicate')
  })

  it('跳号（seq 超过 lastSeq+1）触发全量校准', () => {
    expect(decideEventAction(3, makeEvent('issue.updated', 6, makeIssue('a')))).toBe('resync')
  })

  it('非正序号视为异常触发全量校准', () => {
    expect(decideEventAction(null, makeEvent('issue.updated', 0, makeIssue('a')))).toBe('resync')
    expect(decideEventAction(2, makeEvent('issue.updated', -1, makeIssue('a')))).toBe('resync')
  })
})

describe('mergeIssueEvent 列表合并', () => {
  it('创建事件按 createdAt 从新到旧插入正确位置', () => {
    const old = makeIssue('old', { createdAt: '2026-10-28T09:00:00+08:00' })
    const newer = makeIssue('newer', { createdAt: '2026-10-28T11:00:00+08:00' })
    const list = [old]
    const merged = mergeIssueEvent(list, makeEvent('issue.created', 1, newer))
    expect(merged).toEqual([newer, old])
  })

  it('创建事件晚于全部已有数据时追加到末尾', () => {
    const existing = makeIssue('a', { createdAt: '2026-10-28T11:00:00+08:00' })
    const older = makeIssue('b', { createdAt: '2026-10-28T08:00:00+08:00' })
    const merged = mergeIssueEvent([existing], makeEvent('issue.created', 1, older))
    expect(merged).toEqual([existing, older])
  })

  it('更新事件替换目标行并保留其他行', () => {
    const a = makeIssue('a')
    const b = makeIssue('b')
    const updated = makeIssue('a', { status: 'resolved', updatedAt: '2026-10-28T12:00:00+08:00' })
    const merged = mergeIssueEvent([a, b], makeEvent('issue.updated', 2, updated))
    expect(merged).toEqual([updated, b])
  })

  it('更新先于创建到达（未知 ID 的更新）返回 null 触发全量校准，不伪造行', () => {
    const a = makeIssue('a')
    const unknown = makeIssue('ghost')
    expect(mergeIssueEvent([a], makeEvent('issue.updated', 1, unknown))).toBeNull()
  })

  it('创建事件命中已存在 ID 时按更新替换（校准后迟到创建）', () => {
    const a = makeIssue('a', { status: 'open' })
    const refreshed = makeIssue('a', { status: 'in_progress' })
    const merged = mergeIssueEvent([a], makeEvent('issue.created', 1, refreshed))
    expect(merged).toEqual([refreshed])
  })

  it('合并不修改原数组（不可变性）', () => {
    const a = makeIssue('a')
    const list = [a]
    mergeIssueEvent(list, makeEvent('issue.updated', 1, makeIssue('a', { status: 'resolved' })))
    expect(list[0].status).toBe('open')
  })
})

describe('parseRealtimeEvent 结构校验', () => {
  it('接受完整事件', () => {
    const event = makeEvent('issue.created', 1, makeIssue('a'))
    expect(parseRealtimeEvent(event)).toEqual(event)
  })

  it('拒绝未知类型、非整数序号、结构不完整问题', () => {
    expect(parseRealtimeEvent({ type: 'issue.deleted', seq: 1, issue: makeIssue('a') })).toBeNull()
    expect(parseRealtimeEvent({ type: 'issue.created', seq: 1.5, issue: makeIssue('a') })).toBeNull()
    expect(parseRealtimeEvent({ type: 'issue.created', seq: 1, issue: { id: 'a' } })).toBeNull()
    expect(parseRealtimeEvent({ type: 'issue.created', seq: 1 })).toBeNull()
    expect(parseRealtimeEvent(null)).toBeNull()
  })
})

/** 可控假连接：记录回调，手动触发 open/close/message */
class FakeConnection implements SocketHandle {
  handlers: SocketHandlers | null = null
  closed = false

  close(): void {
    this.closed = true
  }

  fireOpen(): void {
    this.handlers?.onOpen()
  }

  fireClose(): void {
    this.handlers?.onClose()
  }

  fireMessage(data: unknown): void {
    this.handlers?.onMessage({ data: typeof data === 'string' ? data : JSON.stringify(data) })
  }
}

/** 假定时器：手动推进 */
class FakeScheduler {
  timers = new Map<number, () => void>()
  nextId = 1
  now = 0

  schedule = (fn: () => void, ms: number): number => {
    const id = this.nextId++
    this.timers.set(id, fn)
    void ms
    return id
  }

  cancel = (id: number): void => {
    this.timers.delete(id)
  }

  /** 执行当前排队的全部重连定时器 */
  flush(): void {
    const pending = [...this.timers.values()]
    this.timers.clear()
    for (const fn of pending) fn()
  }
}

function makeClient(overrides: Partial<ConstructorParameters<typeof RealtimeClient>[0]> = {}) {
  const sockets: FakeConnection[] = []
  const statuses: string[] = []
  const scheduler = new FakeScheduler()
  const client = new RealtimeClient({
    connect: handlers => {
      const socket = new FakeConnection()
      socket.handlers = handlers
      sockets.push(socket)
      return socket
    },
    onEvent: () => undefined,
    onStatus: status => statuses.push(status),
    schedule: scheduler.schedule,
    cancelSchedule: scheduler.cancel,
    ...overrides,
  })
  return { client, sockets, statuses, scheduler }
}

describe('RealtimeClient 连接与重连', () => {
  it('start 建立连接并在成功后报告 connected', () => {
    const { client, sockets, statuses } = makeClient()
    client.start()
    expect(statuses).toEqual(['connecting'])
    sockets[0].fireOpen()
    expect(statuses).toEqual(['connecting', 'connected'])
    client.stop()
    expect(statuses[statuses.length - 1]).toBe('closed')
  })

  it('断线后进入 reconnecting，退避定时器触发后重连', () => {
    const { client, sockets, statuses, scheduler } = makeClient()
    client.start()
    sockets[0].fireOpen()
    sockets[0].fireClose()
    expect(statuses).toContain('reconnecting')
    expect(sockets.length).toBe(1)

    scheduler.flush() // 推进退避定时器
    expect(sockets.length).toBe(2)
    sockets[1].fireOpen()
    expect(statuses[statuses.length - 1]).toBe('connected')
    client.stop()
  })

  it('连续失败按指数退避多次排队，不立即风暴重连', () => {
    const { client, sockets, scheduler } = makeClient({ baseDelayMs: 100, maxDelayMs: 400 })
    client.start()
    sockets[0].fireClose()
    scheduler.flush()
    sockets[1].fireClose()
    scheduler.flush()
    sockets[2].fireClose()
    // 三次失败后处于第三次退避排队，连接尝试共 3 次
    expect(sockets.length).toBe(3)
    client.stop()
  })

  it('stop 后不再重连', () => {
    const { client, sockets, scheduler } = makeClient()
    client.start()
    sockets[0].fireClose()
    client.stop()
    scheduler.flush()
    expect(sockets.length).toBe(1)
  })

  it('收到合法事件转发给 onEvent，非法 JSON 与结构忽略', () => {
    const events: RealtimeEvent[] = []
    const { client, sockets } = makeClient({ onEvent: e => { events.push(e) } })
    client.start()
    sockets[0].fireOpen()
    sockets[0].fireMessage('not json')
    sockets[0].fireMessage({ type: 'issue.created', seq: 1, issue: makeIssue('a') })
    sockets[0].fireMessage({ type: 'bogus', seq: 2, issue: makeIssue('b') })
    expect(events).toHaveLength(1)
    expect(events[0].issue.id).toBe('a')
    client.stop()
  })

  it('connect 抛错视同连接失败进入退避', () => {
    let failFirst = true
    const statuses: string[] = []
    const scheduler = new FakeScheduler()
    const sockets: FakeConnection[] = []
    const client = new RealtimeClient({
      connect: handlers => {
        if (failFirst) {
          failFirst = false
          throw new Error('connection refused')
        }
        const socket = new FakeConnection()
        socket.handlers = handlers
        sockets.push(socket)
        return socket
      },
      onEvent: () => undefined,
      onStatus: s => statuses.push(s),
      schedule: scheduler.schedule,
      cancelSchedule: scheduler.cancel,
    })
    client.start()
    expect(statuses).toEqual(['connecting', 'reconnecting'])
    scheduler.flush()
    expect(sockets.length).toBe(1)
    client.stop()
  })
})

describe('websocketUrl 地址推导', () => {
  it('http 基础地址转为 ws', () => {
    expect(websocketUrl('http://localhost:8080')).toBe('ws://localhost:8080/api/ws')
  })

  it('https 基础地址转为 wss', () => {
    expect(websocketUrl('https://demo.example.com')).toBe('wss://demo.example.com/api/ws')
  })
})

// 消除 vi 未使用告警（保留导入以便未来断言扩展）
void vi
