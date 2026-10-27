import { useCallback, useEffect, useRef, useState } from 'react'
import type { ReactNode } from 'react'
import type { Issue, Status } from './types/issue'
import { fetchIssues, isRequestCancelled, updateIssueStatus } from './api/issues'
import { statusLabel } from './lib/display'
import { AdminShell } from './components/AdminShell'
import { IssueList } from './components/IssueList'
import { Feedback } from './components/Feedback'

/** 首次加载结果状态；刷新不改变它，刷新进度与失败单独表达 */
type LoadState = 'loading' | 'success' | 'error'

/** 把异常转换为可读中文；请求层已保证错误 message 可直接展示 */
function errorMessage(error: unknown): string {
  return error instanceof Error ? error.message : '发生未知错误，请重试'
}

export default function App() {
  const [issues, setIssues] = useState<Issue[]>([])
  const [loadState, setLoadState] = useState<LoadState>('loading')
  const [initialError, setInitialError] = useState<string | null>(null)
  const [refreshing, setRefreshing] = useState(false)
  const [refreshError, setRefreshError] = useState<string | null>(null)
  const [drafts, setDrafts] = useState<Record<string, Status>>({})
  const [saving, setSaving] = useState<{ id: string; title: string } | null>(null)
  const [saveError, setSaveError] = useState<{ id: string; title: string; message: string } | null>(null)
  const [saveSuccess, setSaveSuccess] = useState<string | null>(null)

  // 当前活动请求（列表或保存）；组件卸载时中止，迟到结果按取消丢弃
  const requestRef = useRef<AbortController | null>(null)

  /** 执行列表请求并应用结果；全部状态更新发生在 await 之后，供首载与手动加载复用 */
  const runLoad = useCallback(async (mode: 'initial' | 'refresh', controller: AbortController) => {
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
      setLoadState('success')
      setInitialError(null)
      setRefreshError(null)
    } catch (error) {
      if (controller.signal.aborted || isRequestCancelled(error)) return
      if (mode === 'refresh') {
        setRefreshError(errorMessage(error))
      } else {
        setLoadState('error')
        setInitialError(errorMessage(error))
      }
    } finally {
      if (mode === 'refresh' && !controller.signal.aborted) setRefreshing(false)
    }
  }, [])

  /** 手动刷新：开始时同步标记刷新中并清除保存反馈，旧数据保持可见 */
  const handleRefresh = useCallback(() => {
    if (loadState === 'loading' || refreshing || saving !== null) return
    const controller = new AbortController()
    requestRef.current?.abort()
    requestRef.current = controller
    setSaveSuccess(null)
    setSaveError(null)
    setRefreshing(true)
    setRefreshError(null)
    void runLoad('refresh', controller)
  }, [loadState, refreshing, saving, runLoad])

  /** 首次失败后的重试：回到加载态并重新执行首载 */
  const handleRetryInitial = useCallback(() => {
    const controller = new AbortController()
    requestRef.current?.abort()
    requestRef.current = controller
    setSaveSuccess(null)
    setSaveError(null)
    setLoadState('loading')
    setInitialError(null)
    void runLoad('initial', controller)
  }, [runLoad])

  /** 状态草稿变化：只记录选择，不发送请求；新的编辑使旧反馈失效 */
  const handleDraftChange = useCallback((id: string, status: Status) => {
    setSaveSuccess(null)
    setSaveError(null)
    setDrafts(prev => ({ ...prev, [id]: status }))
  }, [])

  /** 保存目标状态：同一时间只允许一笔；成功以服务端完整结果替换该行，失败保留原状态与草稿 */
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
        setIssues(prev => prev.map(issue => (issue.id === updated.id ? updated : issue)))
        setDrafts(prev => {
          const next = { ...prev }
          delete next[updated.id]
          return next
        })
        setSaveSuccess(`问题「${updated.title}」状态已更新为「${statusLabel(updated.status)}」`)
      } catch (error) {
        if (controller.signal.aborted || isRequestCancelled(error)) return
        setSaveError({ id: target.id, title: target.title, message: errorMessage(error) })
      } finally {
        if (!controller.signal.aborted) setSaving(null)
      }
    },
    [drafts, loadState, saving],
  )

  useEffect(() => {
    // 首次加载；runLoad 内的状态更新均在请求返回后异步执行
    const controller = new AbortController()
    requestRef.current = controller
    // oxlint 误报：它把 async 函数内 await 之后的 setState 也视作 effect 同步路径，此处显式禁用
    // eslint-disable-next-line react/set-state-in-effect
    void runLoad('initial', controller)
    return () => {
      controller.abort()
    }
  }, [runLoad])

  const listBusy = loadState === 'loading' || refreshing
  const anyBusy = listBusy || saving !== null

  /** 上方反馈区：同一时刻最多渲染一条，加载/错误/保存反馈互斥不矛盾 */
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
        <Feedback
          kind="error"
          action={{ label: '重试', onClick: handleRetryInitial }}
        >
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

  return (
    <AdminShell>
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
        <IssueList
          issues={issues}
          drafts={drafts}
          state={loadState}
          busy={anyBusy}
          savingId={saving?.id ?? null}
          onDraftChange={handleDraftChange}
          onSave={issue => void handleSave(issue)}
        />
      </div>
    </AdminShell>
  )
}
