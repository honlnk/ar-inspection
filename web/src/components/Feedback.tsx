import type { ReactNode } from 'react'
import { COMPACT_BUTTON } from '../lib/styles'

export type FeedbackKind = 'loading' | 'info' | 'error' | 'success'

/** 动态状态样式通过完整静态类名映射表达，不在运行时拼接 Tailwind 类名 */
const KIND_STYLES: Record<FeedbackKind, string> = {
  loading: 'border-slate-200 bg-slate-50 text-slate-600',
  info: 'border-brand-200 bg-brand-50 text-brand-700',
  error: 'border-red-200 bg-red-50 text-red-700',
  success: 'border-emerald-200 bg-emerald-50 text-emerald-700',
}

/** 反馈条左侧语义色条：颜色只是辅助，正文始终带明确文字 */
const KIND_ACCENTS: Record<FeedbackKind, string> = {
  loading: 'bg-slate-400',
  info: 'bg-brand-500',
  error: 'bg-red-500',
  success: 'bg-emerald-500',
}

export interface FeedbackAction {
  label: string
  onClick: () => void
}

/**
 * 统一反馈条：加载、提示、错误与成功反馈。页面同一时刻只渲染一条，
 * 错误使用警报语义，其余使用状态语义；操作按钮由调用方以声明式配置传入。
 */
export function Feedback({ kind, children, action }: { kind: FeedbackKind; children: ReactNode; action?: FeedbackAction }) {
  return (
    <div
      role={kind === 'error' ? 'alert' : 'status'}
      className={`flex flex-wrap items-center gap-3 rounded-control border py-3 pr-4 pl-3 text-sm ${KIND_STYLES[kind]}`}
    >
      <span aria-hidden="true" className={`h-5 w-1 shrink-0 self-stretch rounded-full ${KIND_ACCENTS[kind]}`} />
      <span className="min-w-0 flex-1">{children}</span>
      {action ? (
        <button type="button" onClick={action.onClick} className={`${COMPACT_BUTTON} shrink-0`}>
          {action.label}
        </button>
      ) : null}
    </div>
  )
}
