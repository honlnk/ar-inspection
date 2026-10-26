import type { ReactNode } from 'react'

export type FeedbackKind = 'loading' | 'empty' | 'error' | 'success'

/** 动态状态样式通过完整静态类名映射表达，不在运行时拼接 Tailwind 类名 */
const KIND_STYLES: Record<FeedbackKind, string> = {
  loading: 'border-slate-200 bg-slate-50 text-slate-500',
  empty: 'border-slate-200 bg-slate-50 text-slate-500',
  error: 'border-red-200 bg-red-50 text-red-700',
  success: 'border-emerald-200 bg-emerald-50 text-emerald-700',
}

const KIND_LABELS: Record<FeedbackKind, string> = {
  loading: '加载中',
  empty: '暂无数据',
  error: '出错了',
  success: '操作成功',
}

/**
 * 加载、空列表、错误、成功提示的结构预留。
 * 本期不渲染任何虚构反馈状态，待真实业务接入后使用。
 */
export function Feedback({ kind, children }: { kind: FeedbackKind; children?: ReactNode }) {
  return (
    <div
      role={kind === 'error' ? 'alert' : 'status'}
      className={`rounded-md border px-4 py-3 text-sm ${KIND_STYLES[kind]}`}
    >
      <span className="font-medium">{KIND_LABELS[kind]}</span>
      {children ? <span className="ml-2">{children}</span> : null}
    </div>
  )
}
