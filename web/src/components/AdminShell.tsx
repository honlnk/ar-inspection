import type { ReactNode } from 'react'
import type { CurrentUser } from '../api/auth'
import type { RealtimeStatus } from '../lib/realtime'
import { FOCUS_RING, SECONDARY_BUTTON } from '../lib/styles'

/** 实时连接状态：圆点 + 中文标签，独立于业务错误，不叠加成多层警告 */
const REALTIME_BADGES: Record<RealtimeStatus, { label: string; dot: string; className: string }> = {
  connected: { label: '实时已连接', dot: 'bg-emerald-500', className: 'bg-emerald-50 text-emerald-700' },
  connecting: { label: '正在连接实时通道…', dot: 'bg-slate-400', className: 'bg-slate-100 text-slate-600' },
  reconnecting: {
    label: '正在重连，数据可能不是最新',
    dot: 'bg-amber-500',
    className: 'bg-amber-50 text-amber-700',
  },
  closed: { label: '实时未连接，可手动刷新', dot: 'bg-slate-400', className: 'bg-slate-100 text-slate-600' },
}

/** 管理台页面入口（计划 008）：问题工作台 / 人员管理 */
export type AdminPage = 'issues' | 'personnel'

const PAGE_LABELS: Record<AdminPage, string> = {
  issues: '问题列表',
  personnel: '人员管理',
}

const NAV_ENTRIES: readonly AdminPage[] = ['issues', 'personnel']

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
 * 管理台外壳：产品区 + 当前页面入口 + 实时连接状态 + 账户与退出。
 * 布局规则：桌面宽度显示左侧固定导航；窄屏收起为顶部产品名、入口切换与账户菜单，
 * 不使用抽屉交互，账户菜单用原生 details 提供键盘可达的展开/收起。
 */
export function AdminShell({ user, realtime, loggingOut, onLogout, page, onPageChange, children }: AdminShellProps) {
  const badge = REALTIME_BADGES[realtime]
  return (
    <div className="flex min-h-screen flex-col bg-canvas text-slate-900 md:flex-row">
      <aside className="hidden w-60 shrink-0 flex-col border-r border-slate-200 bg-white md:flex">
        <div className="flex h-16 items-center gap-2.5 border-b border-slate-200 px-5">
          <span aria-hidden="true" className="flex h-8 w-8 items-center justify-center rounded-control bg-brand-600">
            <span className="h-3.5 w-3.5 rounded-[2px] border-2 border-white" />
          </span>
          <span className="min-w-0">
            <span className="block text-sm leading-5 font-semibold">现场巡检问题标注</span>
            <span className="block text-xs leading-4 text-slate-500">管理端</span>
          </span>
        </div>
        <nav className="flex-1 space-y-1 p-3" aria-label="管理台导航">
          {NAV_ENTRIES.map(entry => (
            <button
              key={entry}
              type="button"
              onClick={() => onPageChange(entry)}
              aria-current={page === entry ? 'page' : undefined}
              className={
                page === entry
                  ? `flex w-full items-center rounded-control bg-brand-50 px-3 py-2 text-sm font-medium text-brand-700 ${FOCUS_RING}`
                  : `flex w-full items-center rounded-control px-3 py-2 text-sm font-medium text-slate-600 transition-colors duration-150 hover:bg-slate-100 ${FOCUS_RING}`
              }
            >
              {PAGE_LABELS[entry]}
            </button>
          ))}
        </nav>
        <div className="border-t border-slate-200 px-5 py-4">
          <p className="text-xs leading-5 text-slate-500">
            园区巡检上报与处理
            <br />
            数据由后端实时同步
          </p>
        </div>
      </aside>

      <div className="flex min-w-0 flex-1 flex-col">
        <header className="border-b border-slate-200 bg-white">
          <div className="flex min-h-16 items-center justify-between gap-3 px-4 md:px-6">
            <span className="min-w-0 text-sm font-semibold md:hidden">现场巡检问题标注</span>
            <p className="hidden text-sm text-slate-500 md:block">园区巡检问题管理端</p>

            <div className="flex shrink-0 items-center gap-2 md:gap-3">
              <span
                className={`inline-flex items-center gap-1.5 rounded-full px-2.5 py-1 text-xs font-medium ${badge.className}`}
                role="status"
              >
                <span aria-hidden="true" className={`h-1.5 w-1.5 rounded-full ${badge.dot}`} />
                <span className="hidden sm:inline">{badge.label}</span>
                <span className="sm:hidden">{realtime === 'connected' ? '在线' : '离线'}</span>
              </span>

              {/* 桌面：账户名与退出按钮直接可见 */}
              <div className="hidden items-center gap-3 md:flex">
                <span className="text-right text-xs leading-4">
                  <span className="block font-medium text-slate-700">{user.displayName}</span>
                  <span className="block text-slate-400">{user.username}</span>
                </span>
                <button type="button" disabled={loggingOut} onClick={onLogout} className={SECONDARY_BUTTON}>
                  {loggingOut ? '退出中…' : '退出登录'}
                </button>
              </div>

              {/* 窄屏：账户菜单（原生 details，键盘可达） */}
              <details className="relative md:hidden">
                <summary
                  className={`flex min-h-10 cursor-pointer list-none items-center rounded-control border border-slate-300 px-3 text-xs font-medium text-slate-700 ${FOCUS_RING}`}
                >
                  {user.displayName}
                </summary>
                <div className="absolute right-0 z-20 mt-2 w-52 rounded-card border border-slate-200 bg-white p-3 shadow-raised">
                  <p className="text-sm font-medium text-slate-800">{user.displayName}</p>
                  <p className="mt-0.5 text-xs text-slate-500">账号 {user.username}</p>
                  <button
                    type="button"
                    disabled={loggingOut}
                    onClick={onLogout}
                    className={`${SECONDARY_BUTTON} mt-3 w-full`}
                  >
                    {loggingOut ? '退出中…' : '退出登录'}
                  </button>
                </div>
              </details>
            </div>
          </div>

          {/* 窄屏导航独占一行，避免产品名被挤压省略 */}
          <nav className="flex gap-2 border-t border-slate-100 px-4 py-2 md:hidden" aria-label="管理台导航（窄屏）">
            {NAV_ENTRIES.map(entry => (
              <button
                key={entry}
                type="button"
                onClick={() => onPageChange(entry)}
                aria-current={page === entry ? 'page' : undefined}
                className={
                  page === entry
                    ? `rounded-control bg-brand-600 px-2.5 py-1 text-xs font-medium text-white ${FOCUS_RING}`
                    : `rounded-control bg-slate-100 px-2.5 py-1 text-xs font-medium text-slate-600 ${FOCUS_RING}`
                }
              >
                {PAGE_LABELS[entry]}
              </button>
            ))}
          </nav>
        </header>
        <main className="mx-auto w-full max-w-5xl flex-1 px-4 py-6 md:px-6 md:py-8">{children}</main>
      </div>
    </div>
  )
}
