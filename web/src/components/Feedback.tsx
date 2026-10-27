import type { ReactNode } from 'react'

export type FeedbackKind = 'loading' | 'info' | 'error' | 'success'

/** 动态状态样式通过完整静态类名映射表达，不在运行时拼接 Tailwind 类名 */
const KIND_STYLES: Record<FeedbackKind, string> = {
  loading: 'border-slate-200 bg-slate-50 text-slate-600',
  info: 'border-brand-100 bg-brand-50 text-brand-700',
  error: 'border-red-200 bg-red-50 text-red-700',
  success: 'border-emerald-200 bg-emerald-50 text-emerald-700',
}

/** 反馈区使用的按钮样式：带明确文字，可键盘操作 */
const ACTION_BUTTON_CLASSES: Record<FeedbackKind, string> = {
  loading: 'border-slate-300 bg-white text-slate-700 hover:bg-slate-50',
  info: 'border-brand-500 bg-white text-brand-700 hover:bg-brand-50',
  error: 'border-red-300 bg-white text-red-700 hover:bg-red-50',
  success: 'border-emerald-300 bg-white text-emerald-700 hover:bg-emerald-50',
}

export interface FeedbackAction {
  label: string
  onClick: () => void
}

/**
 * 统一反馈条：加载、空态之外的提示信息、错误与成功反馈。
 * 错误使用警报语义，其余使用状态语义；操作按钮由调用方以声明式配置传入。
 */
export function Feedback({ kind, children, action }: { kind: FeedbackKind; children: ReactNode; action?: FeedbackAction }) {
  return (
    <div
      role={kind === 'error' ? 'alert' : 'status'}
      className={`flex flex-wrap items-center gap-3 rounded-md border px-4 py-3 text-sm ${KIND_STYLES[kind]}`}
    >
      <span className="min-w-0 flex-1">{children}</span>
      {action ? (
        <button
          type="button"
          onClick={action.onClick}
          className={`shrink-0 rounded-md border px-3 py-1.5 text-sm font-medium ${ACTION_BUTTON_CLASSES[kind]}`}
        >
          {action.label}
        </button>
      ) : null}
    </div>
  )
}
