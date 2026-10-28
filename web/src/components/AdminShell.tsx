import type { ReactNode } from 'react'
import type { CurrentUser } from '../api/auth'
import type { RealtimeStatus } from '../lib/realtime'

/** 实时连接状态的中文标签与样式 */
const REALTIME_BADGES: Record<RealtimeStatus, { label: string; className: string }> = {
  connected: { label: '实时已连接', className: 'bg-emerald-50 text-emerald-700' },
  connecting: { label: '正在连接实时通道…', className: 'bg-slate-100 text-slate-500' },
  reconnecting: { label: '正在重连，数据可能不是最新', className: 'bg-amber-50 text-amber-700' },
  closed: { label: '实时未连接，可手动刷新', className: 'bg-slate-100 text-slate-500' },
}

/** 管理台页面入口（计划 008）：问题工作台 / 人员管理 */
export type AdminPage = 'issues' | 'personnel'

const PAGE_LABELS: Record<AdminPage, string> = {
  issues: '问题列表',
  personnel: '人员管理',
}

export interface AdminShellProps {
  user: CurrentUser
  realtime: RealtimeStatus
  loggingOut: boolean
  onLogout: () => void
  /** 当前页面；双入口切换由 App 状态控制，不引入路由库 */
  page: AdminPage
  onPageChange: (page: AdminPage) => void
  children: ReactNode
}

/**
 * 管理台外壳：参考 linkseek 管理台的信息结构，用 React + Tailwind 重写。
 * 布局规则：桌面宽度显示左侧固定导航；窄屏按固定规则收起为顶部简化导航，不使用抽屉交互。
 * 计划 006：侧栏展示当前账号与退出入口，头部展示实时连接状态。
 * 计划 008：侧栏与窄屏导航均为「问题列表 / 人员管理」双入口。
 */
export function AdminShell({ user, realtime, loggingOut, onLogout, page, onPageChange, children }: AdminShellProps) {
  const badge = REALTIME_BADGES[realtime]
  return (
    <div className="flex min-h-screen flex-col bg-slate-100 text-slate-900 md:flex-row">
      <aside className="hidden w-56 shrink-0 flex-col border-r border-slate-200 bg-white md:flex">
        <div className="flex h-14 items-center border-b border-slate-200 px-5">
          <span className="text-sm font-semibold tracking-wide">现场巡检问题标注</span>
        </div>
        <nav className="flex-1 space-y-1 p-3" aria-label="管理台导航">
          {(['issues', 'personnel'] as const).map(entry => (
            <button
              key={entry}
              type="button"
              onClick={() => onPageChange(entry)}
              aria-current={page === entry ? 'page' : undefined}
              className={
                page === entry
                  ? 'flex w-full items-center gap-2 rounded-md bg-slate-900 px-3 py-2 text-sm font-medium text-white'
                  : 'flex w-full items-center gap-2 rounded-md px-3 py-2 text-sm font-medium text-slate-600 hover:bg-slate-100'
              }
            >
              {PAGE_LABELS[entry]}
            </button>
          ))}
        </nav>
        <div className="space-y-2 border-t border-slate-200 px-5 py-3">
          <div className="text-xs text-slate-500">
            <span className="font-medium text-slate-700">{user.displayName}</span>
            <span className="ml-1 text-slate-400">（{user.username}）</span>
          </div>
          <button
            type="button"
            disabled={loggingOut}
            onClick={onLogout}
            className="w-full rounded-md border border-slate-300 px-3 py-1.5 text-xs text-slate-600 hover:bg-slate-50 disabled:cursor-not-allowed disabled:opacity-50"
          >
            {loggingOut ? '退出中…' : '退出登录'}
          </button>
        </div>
      </aside>

      <div className="flex min-w-0 flex-1 flex-col">
        <header className="flex h-14 items-center justify-between gap-3 border-b border-slate-200 bg-white px-4 md:px-6">
          {/* 窄屏：侧栏收起为顶部简化导航，仅保留产品名与双入口切换 */}
          <div className="flex min-w-0 items-center gap-2 md:hidden">
            <span className="truncate text-sm font-semibold">现场巡检问题标注</span>
            {(['issues', 'personnel'] as const).map(entry => (
              <button
                key={entry}
                type="button"
                onClick={() => onPageChange(entry)}
                aria-current={page === entry ? 'page' : undefined}
                className={
                  page === entry
                    ? 'shrink-0 rounded-md bg-slate-900 px-2 py-0.5 text-xs font-medium text-white'
                    : 'shrink-0 rounded-md bg-slate-100 px-2 py-0.5 text-xs font-medium text-slate-600'
                }
              >
                {PAGE_LABELS[entry]}
              </button>
            ))}
          </div>
          <div className="hidden text-sm text-slate-500 md:block">园区巡检问题管理端</div>
          <div className="flex shrink-0 items-center gap-2">
            <span className={`rounded-full px-2.5 py-1 text-xs font-medium ${badge.className}`} role="status">
              {badge.label}
            </span>
            <span className="hidden rounded-full bg-brand-50 px-2.5 py-1 text-xs text-brand-700 md:inline">
              {PAGE_LABELS[page]}
            </span>
          </div>
        </header>
        <main className="mx-auto w-full max-w-5xl flex-1 px-4 py-6 md:px-6">{children}</main>
      </div>
    </div>
  )
}
