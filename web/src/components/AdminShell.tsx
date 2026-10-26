import type { ReactNode } from 'react'

/**
 * 管理台外壳：参考 linkseek 管理台的信息结构，用 React + Tailwind 重写。
 * 布局规则：桌面宽度显示左侧固定导航；窄屏按固定规则收起为顶部简化导航，不使用抽屉交互。
 */
export function AdminShell({ children }: { children: ReactNode }) {
  return (
    <div className="flex min-h-screen flex-col bg-slate-100 text-slate-900 md:flex-row">
      <aside className="hidden w-56 shrink-0 flex-col border-r border-slate-200 bg-white md:flex">
        <div className="flex h-14 items-center border-b border-slate-200 px-5">
          <span className="text-sm font-semibold tracking-wide">现场巡检问题标注</span>
        </div>
        <nav className="flex-1 space-y-1 p-3">
          <span className="flex items-center gap-2 rounded-md bg-slate-900 px-3 py-2 text-sm font-medium text-white">
            问题列表
          </span>
        </nav>
        <div className="border-t border-slate-200 px-5 py-3 text-xs text-slate-400">管理端</div>
      </aside>

      <div className="flex min-w-0 flex-1 flex-col">
        <header className="flex h-14 items-center justify-between gap-3 border-b border-slate-200 bg-white px-4 md:px-6">
          {/* 窄屏：侧栏收起为顶部简化导航，仅保留产品名与唯一入口 */}
          <div className="flex min-w-0 items-center gap-2 md:hidden">
            <span className="truncate text-sm font-semibold">现场巡检问题标注</span>
            <span className="shrink-0 rounded-md bg-slate-900 px-2 py-0.5 text-xs font-medium text-white">
              问题列表
            </span>
          </div>
          <div className="hidden text-sm text-slate-500 md:block">园区巡检问题管理端</div>
          <span className="shrink-0 rounded-full bg-brand-50 px-2.5 py-1 text-xs text-brand-700">
            骨架阶段 · 未接数据
          </span>
        </header>
        <main className="mx-auto w-full max-w-5xl flex-1 px-4 py-6 md:px-6">{children}</main>
      </div>
    </div>
  )
}
