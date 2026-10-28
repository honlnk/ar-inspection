import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import type { ReactNode } from 'react'
import type { Issue, Status } from '../types/issue'
import { fetchIssues, isRequestCancelled, updateIssueStatus } from '../api/issues'
import { isAuthenticationError } from '../api/auth'
import { API_BASE_URL } from '../api/issues'
import { statusLabel } from '../lib/display'
import {
  RealtimeClient,
  decideEventAction,
  mergeIssueEvent,
  websocketUrl,
  type RealtimeClientOptions,
  type RealtimeEvent,
  type RealtimeStatus,
} from '../lib/realtime'
import { IssueList } from './IssueList'
import { IssueFilters } from './IssueFilters'
import { applyIssueFilters, EMPTY_FILTERS, type IssueFilterValues } from '../lib/filters'
import { Feedback } from './Feedback'

/** 首次加载结果状态；刷新不改变它，刷新进度与失败单独表达 */
type LoadState = 'loading' | 'success' | 'error'

function errorMessage(error: unknown): string {
  return error instanceof Error ? error.message : '发生未知错误，请重试'
}

export interface IssueBoardProps {
  /** 会话过期（REST 401）时通知 App 回登录页 */
  onSessionExpired: () => void
  /**
   * 计划 008 双入口导航：切到人员管理页时置 true。组件保持挂载
   * （实时连接、未保存草稿与筛选状态不丢），仅显示/隐藏。
   */
  hidden: boolean
  /** 实时连接状态变化时上抛，由 App 侧外壳徽标展示 */
  onRealtimeChange?: (status: RealtimeStatus) => void
  /** 测试注入实时客户端工厂；默认按后端地址建立 WebSocket 连接 */
  createRealtimeClient?: (options: RealtimeClientOptions) => RealtimeClient
}

/**
 * 问题工作台面板：列表加载、状态保存、实时同步与筛选共享同一份列表状态。
 * 实时事件与保存结果通过函数式 setState 串行合并：事件不取消进行中的保存，
 * 保存成功以服务端响应为准；存在草稿的行被远端更新时保留草稿并提示。
 * 计划 008：外壳（导航/账号/退出）上移至 App，本组件为可切换面板之一。
 */
export function IssueBoard({ onSessionExpired, hidden, onRealtimeChange, createRealtimeClient }: IssueBoardProps) {
  const [issues, setIssues] = useState<Issue[]>([])
  const [loadState, setLoadState] = useState<LoadState>('loading')
  const [initialError, setInitialError] = useState<string | null>(null)
  const [refreshing, setRefreshing] = useState(false)
  const [refreshError, setRefreshError] = useState<string | null>(null)
  const [drafts, setDrafts] = useState<Record<string, Status>>({})
  const [saving, setSaving] = useState<{ id: string; title: string } | null>(null)
  const [saveError, setSaveError] = useState<{ id: string; title: string; message: string } | null>(null)
  const [saveSuccess, setSaveSuccess] = useState<string | null>(null)
  const [remoteUpdatedIds, setRemoteUpdatedIds] = useState<Set<string>>(new Set())
  const [filters, setFilters] = useState<IssueFilterValues>(EMPTY_FILTERS)

  const requestRef = useRef<AbortController | null>(null)
  const clientRef = useRef<RealtimeClient | null>(null)
  const lastSeqRef = useRef<number | null>(null)
  // 让事件回调读到最新列表/草稿/校准函数而不频繁重建回调
  const issuesRef = useRef<Issue[]>([])
  const draftsRef = useRef<Record<string, Status>>({})
  const resyncRef = useRef<typeof resync | null>(null)

  /** 全量校准：用服务端列表替换本地状态；保留仍存在行的草稿与远端更新提示 */
  const resync = useCallback(
    async (mode: 'initial' | 'refresh' | 'silent') => {
      const controller = new AbortController()
      if (mode !== 'silent') {
        requestRef.current?.abort()
        requestRef.current = controller
      }
      try {
        const fresh = await fetchIssues(controller.signal)
        if (controller.signal.aborted) return
        setIssues(fresh)
        setDrafts(prev => {
          const next: Record<string, Status> = {}
          const alive = new Set(fresh.map(issue => issue.id))
          for (const [id, status] of Object.entries(prev)) {
            if (alive.has(id)) next[id] = status
          }
          return next
        })
        setRemoteUpdatedIds(prev => {
          const next = new Set<string>()
          for (const id of prev) {
            if (fresh.some(issue => issue.id === id) && draftsRef.current[id] !== undefined) next.add(id)
          }
          return next
        })
        lastSeqRef.current = null
        setLoadState('success')
        setInitialError(null)
        setRefreshError(null)
      } catch (error) {
        if (controller.signal.aborted || isRequestCancelled(error)) return
        if (isAuthenticationError(error)) {
          onSessionExpired()
          return
        }
        if (mode === 'refresh') {
          setRefreshError(errorMessage(error))
        } else if (mode === 'initial') {
          setLoadState('error')
          setInitialError(errorMessage(error))
        }
        // silent：静默校准失败不打扰用户，等待下一次重连校准
      } finally {
        if (mode === 'refresh' && !controller.signal.aborted) setRefreshing(false)
      }
    },
    [onSessionExpired],
  )

  const handleRefresh = useCallback(() => {
    if (loadState === 'loading' || refreshing || saving !== null) return
    setSaveSuccess(null)
    setSaveError(null)
    setRefreshing(true)
    setRefreshError(null)
    void resync('refresh')
  }, [loadState, refreshing, saving, resync])

  const handleRetryInitial = useCallback(() => {
    setSaveSuccess(null)
    setSaveError(null)
    setLoadState('loading')
    setInitialError(null)
    void resync('initial')
  }, [resync])

  const handleDraftChange = useCallback((id: string, status: Status) => {
    setSaveSuccess(null)
    setSaveError(null)
    setDrafts(prev => ({ ...prev, [id]: status }))
  }, [])

  const handleSave = useCallback(
    async (target: Issue) => {
      if (saving !== null || loadState !== 'success') return
      const nextStatus = drafts[target.id] ?? target.status
      if (nextStatus === target.status) return
      const controller = new AbortController()
      requestRef.current = controller
      setSaveSuccess(null)
      setSaveError(null)
      setSaving({ id: target.id, title: target.title })
      try {
        const updated = await updateIssueStatus(target.id, nextStatus, controller.signal)
        if (controller.signal.aborted) return
        // 保存成功以服务端响应替换该行；清除草稿与远端更新提示
        setIssues(prev => prev.map(issue => (issue.id === updated.id ? updated : issue)))
        setDrafts(prev => {
          const next = { ...prev }
          delete next[updated.id]
          return next
        })
        setRemoteUpdatedIds(prev => {
          if (!prev.has(updated.id)) return prev
          const next = new Set(prev)
          next.delete(updated.id)
          return next
        })
        setSaveSuccess(`问题「${updated.title}」状态已更新为「${statusLabel(updated.status)}」`)
      } catch (error) {
        if (controller.signal.aborted || isRequestCancelled(error)) return
        if (isAuthenticationError(error)) {
          onSessionExpired()
          return
        }
        setSaveError({ id: target.id, title: target.title, message: errorMessage(error) })
      } finally {
        if (!controller.signal.aborted) setSaving(null)
      }
    },
    [drafts, loadState, saving, onSessionExpired],
  )

  /** 实时事件应用：与保存/草稿合并规则见组件注释 */
  const applyRealtimeEvent = useCallback((event: RealtimeEvent) => {
    const action = decideEventAction(lastSeqRef.current, event)
    if (action === 'duplicate') return
    if (action === 'resync') {
      void resyncRef.current?.('silent')
      return
    }
    const merged = mergeIssueEvent(issuesRef.current, event)
    if (merged === null) {
      // 未知 ID 的更新：全量校准，不伪造行
      void resyncRef.current?.('silent')
      return
    }
    issuesRef.current = merged
    setIssues(merged)
    lastSeqRef.current = event.seq
    // 草稿保护：该行存在未保存草稿时提示“远端已更新，草稿未保存”，不清除草稿
    if (draftsRef.current[event.issue.id] !== undefined) {
      setRemoteUpdatedIds(prev => new Set(prev).add(event.issue.id))
    }
  }, [])

  useEffect(() => {
    issuesRef.current = issues
  }, [issues])
  useEffect(() => {
    draftsRef.current = drafts
  }, [drafts])
  useEffect(() => {
    resyncRef.current = resync
  }, [resync])

  // 首次加载 → 成功后建立实时连接
  useEffect(() => {
    // oxlint 误报：resync 的 setState 均在 await 之后异步执行，非 effect 同步路径
    // eslint-disable-next-line react/set-state-in-effect
    void resync('initial')
  }, [resync])

  const boardReady = loadState === 'success'
  // 工厂捕获首次渲染值：运行时为 undefined（默认 WebSocket），测试注入假工厂后不变
  const [createClientFactory] = useState(() => createRealtimeClient)
  useEffect(() => {
    if (!boardReady) return
    // 事件与状态回调；连接方式由注入工厂或默认 WebSocket 适配提供
    const callbacks: Pick<RealtimeClientOptions, 'onEvent' | 'onStatus'> = {
      onEvent: applyRealtimeEvent,
      onStatus: status => {
        onRealtimeChange?.(status)
        if (status === 'connected') {
          // 每次连接成功后全量校准并重置序号（覆盖进程重启导致的序号回退）
          lastSeqRef.current = null
          void resyncRef.current?.('silent')
        }
      },
    }
    const connectWebSocket: RealtimeClientOptions['connect'] = handlers => {
      const ws = new WebSocket(websocketUrl(API_BASE_URL))
      ws.onopen = () => handlers.onOpen()
      ws.onclose = () => handlers.onClose()
      ws.onerror = () => handlers.onError()
      ws.onmessage = ev => handlers.onMessage({ data: ev.data })
      return {
        close: () => {
          // 主动关闭：先摘除回调，避免 close 事件再次触发重连
          ws.onopen = null
          ws.onclose = null
          ws.onerror = null
          ws.onmessage = null
          ws.close(1000, 'client closed')
        },
      }
    }
    // 注入工厂时由工厂决定连接方式（connect 占位不会被调用）；默认走浏览器 WebSocket
    const client = createClientFactory
      ? createClientFactory({ ...callbacks, connect: () => { throw new Error('连接应由注入工厂创建') } })
      : new RealtimeClient({ ...callbacks, connect: connectWebSocket })
    clientRef.current = client
    client.start()
    return () => {
      client.stop()
      clientRef.current = null
    }
    // 工厂为惰性 state（稳定），boardReady/applyRealtimeEvent 决定连接生命周期
  }, [boardReady, applyRealtimeEvent, createClientFactory, onRealtimeChange])

  const visibleIssues = useMemo(() => applyIssueFilters(issues, filters), [issues, filters])
  const emptyText =
    issues.length === 0 ? '暂无问题' : '没有符合当前条件的问题'

  const listBusy = loadState === 'loading' || refreshing
  const anyBusy = listBusy || saving !== null

  function renderFeedback(): ReactNode {
    if (saving) {
      return <Feedback kind="loading">正在保存问题「{saving.title}」的处理状态…</Feedback>
    }
    if (saveError) {
      return (
        <Feedback kind="error">
          问题「{saveError.title}」状态保存失败：{saveError.message}。原状态与您的选择均已保留，可在该行重新点击保存。
        </Feedback>
      )
    }
    if (saveSuccess) {
      return <Feedback kind="success">{saveSuccess}</Feedback>
    }
    if (loadState === 'error') {
      return (
        <Feedback kind="error" action={{ label: '重试', onClick: handleRetryInitial }}>
          无法获取问题：{initialError}。请确认后端服务可用后重试。
        </Feedback>
      )
    }
    if (loadState === 'loading') {
      return <Feedback kind="loading">正在从后端加载问题…</Feedback>
    }
    if (refreshing) {
      return <Feedback kind="loading">正在刷新，当前显示的仍是已有数据…</Feedback>
    }
    if (refreshError) {
      return (
        <Feedback kind="error" action={{ label: '重试', onClick: handleRefresh }}>
          刷新失败，当前数据可能不是最新数据：{refreshError}
        </Feedback>
      )
    }
    return null
  }

  // 计划 008：切到人员管理页时保持挂载、仅隐藏；恢复时工作台状态原样
  if (hidden) {
    return (
      <div className="hidden" aria-hidden="true">
        {/* 隐藏时保留全部状态（列表/草稿/筛选/实时连接），不卸载组件 */}
        <div className="space-y-4">
          <IssueFilters
            values={filters}
            visibleCount={issues.length}
            totalCount={issues.length}
            disabled
            onChange={setFilters}
          />
          <IssueList
            issues={visibleIssues}
            drafts={drafts}
            state="success"
            busy
            savingId={null}
            remoteUpdatedIds={remoteUpdatedIds}
            emptyText={emptyText}
            onDraftChange={handleDraftChange}
            onSave={issue => void handleSave(issue)}
          />
        </div>
      </div>
    )
  }

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold">问题列表</h1>
          <p className="mt-1 text-sm text-slate-500">查看巡检人员上报的问题并更新处理状态</p>
        </div>
        <button
          type="button"
          disabled={anyBusy}
          onClick={handleRefresh}
          className="rounded-md bg-brand-600 px-3.5 py-2 text-sm font-medium text-white hover:bg-brand-700 disabled:cursor-not-allowed disabled:opacity-50"
        >
          {refreshing ? '刷新中…' : '刷新'}
        </button>
      </div>
      {renderFeedback()}
      <IssueFilters
        values={filters}
        visibleCount={loadState === 'success' ? visibleIssues.length : 0}
        totalCount={loadState === 'success' ? issues.length : 0}
        disabled={loadState !== 'success'}
        onChange={setFilters}
      />
      <IssueList
        issues={visibleIssues}
        drafts={drafts}
        state={loadState}
        busy={anyBusy}
        savingId={saving?.id ?? null}
        remoteUpdatedIds={remoteUpdatedIds}
        emptyText={emptyText}
        onDraftChange={handleDraftChange}
        onSave={issue => void handleSave(issue)}
      />
    </div>
  )
}
