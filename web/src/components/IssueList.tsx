import type { Issue, Status } from '../types/issue'
import { formatBeijingTime, priorityBadgeClasses, priorityLabel, statusBadgeClasses, statusLabel } from '../lib/display'

export type ListState = 'loading' | 'error' | 'success'

const STATUS_OPTIONS: readonly Status[] = ['open', 'in_progress', 'resolved']

export interface IssueListProps {
  /** 服务端当前问题列表，顺序以服务端返回为准 */
  issues: Issue[]
  /** 未保存的状态草稿（问题 ID → 目标状态）；无草稿的行展示服务端状态 */
  drafts: Record<string, Status>
  /** 首次加载结果；刷新失败不改变该状态，旧数据继续展示 */
  state: ListState
  /** 列表请求或状态保存进行中：禁用全部下拉框与保存按钮 */
  busy: boolean
  /** 正在保存的问题 ID，用于该行按钮显示保存中 */
  savingId: string | null
  onDraftChange: (id: string, status: Status) => void
  onSave: (issue: Issue) => void
}

/**
 * 问题列表：卡片容器 + 真实数据表格。
 * 表格内部只保留加载、空列表或首次失败的行内说明；其余反馈统一由页面上方反馈区表达。
 */
export function IssueList({ issues, drafts, state, busy, savingId, onDraftChange, onSave }: IssueListProps) {
  const count = state === 'success' ? issues.length : '—'
  return (
    <section className="rounded-lg border border-slate-200 bg-white shadow-sm">
      <div className="flex items-center justify-between border-b border-slate-200 px-4 py-3 md:px-5">
        <h2 className="text-sm font-semibold text-slate-700">问题列表</h2>
        <span className="text-xs text-slate-400">共 {count} 条</span>
      </div>
      <div className="overflow-x-auto">
        <table className="w-full min-w-[720px] text-left text-sm">
          <thead>
            <tr className="border-b border-slate-100 text-xs text-slate-500">
              <th className="px-4 py-2.5 font-medium md:px-5">标题</th>
              <th className="px-4 py-2.5 font-medium">优先级</th>
              <th className="px-4 py-2.5 font-medium">当前状态</th>
              <th className="px-4 py-2.5 font-medium">更新时间</th>
              <th className="px-4 py-2.5 font-medium">操作</th>
            </tr>
          </thead>
          <tbody>
            {state === 'loading' ? (
              <PlaceholderRow>正在加载问题…</PlaceholderRow>
            ) : state === 'error' ? (
              <PlaceholderRow>暂时无法显示问题列表，请稍后重试</PlaceholderRow>
            ) : issues.length === 0 ? (
              <PlaceholderRow>暂无问题</PlaceholderRow>
            ) : (
              issues.map(issue => (
                <IssueRow
                  key={issue.id}
                  issue={issue}
                  draft={drafts[issue.id]}
                  busy={busy}
                  saving={savingId === issue.id}
                  onDraftChange={onDraftChange}
                  onSave={onSave}
                />
              ))
            )}
          </tbody>
        </table>
      </div>
    </section>
  )
}

function PlaceholderRow({ children }: { children: string }) {
  return (
    <tr>
      <td colSpan={5} className="px-4 py-10 text-center text-sm text-slate-400 md:px-5">
        {children}
      </td>
    </tr>
  )
}

function IssueRow({
  issue,
  draft,
  busy,
  saving,
  onDraftChange,
  onSave,
}: {
  issue: Issue
  draft?: Status
  busy: boolean
  saving: boolean
  onDraftChange: (id: string, status: Status) => void
  onSave: (issue: Issue) => void
}) {
  const selected = draft ?? issue.status
  return (
    <tr className="border-b border-slate-100 align-top last:border-b-0">
      <td className="px-4 py-3 md:px-5">
        <div className="max-w-[280px] font-medium break-words text-slate-900">{issue.title}</div>
        <div className="mt-1 max-w-[280px] text-xs leading-5 break-words text-slate-500">
          {issue.description || '无描述'}
        </div>
      </td>
      <td className="px-4 py-3">
        <span
          className={`inline-flex rounded-full px-2 py-0.5 text-xs font-medium ${priorityBadgeClasses(issue.priority)}`}
        >
          {priorityLabel(issue.priority)}
        </span>
      </td>
      <td className="px-4 py-3">
        <span className={`inline-flex rounded-full px-2 py-0.5 text-xs font-medium ${statusBadgeClasses(issue.status)}`}>
          {statusLabel(issue.status)}
        </span>
      </td>
      <td className="px-4 py-3 whitespace-nowrap text-slate-500">{formatBeijingTime(issue.updatedAt)}</td>
      <td className="px-4 py-3">
        <div className="flex items-center gap-2">
          <select
            aria-label={`修改问题「${issue.title}」的处理状态`}
            value={selected}
            disabled={busy}
            onChange={event => onDraftChange(issue.id, event.target.value as Status)}
            className="rounded-md border border-slate-300 bg-white px-2 py-1.5 text-sm text-slate-700 disabled:cursor-not-allowed disabled:opacity-50"
          >
            {STATUS_OPTIONS.map(option => (
              <option key={option} value={option}>
                {statusLabel(option)}
              </option>
            ))}
          </select>
          <button
            type="button"
            disabled={busy || selected === issue.status}
            onClick={() => onSave(issue)}
            className="rounded-md bg-brand-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-brand-700 disabled:cursor-not-allowed disabled:opacity-50"
          >
            {saving ? '保存中…' : '保存'}
          </button>
        </div>
      </td>
    </tr>
  )
}
