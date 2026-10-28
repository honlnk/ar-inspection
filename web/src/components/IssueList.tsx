import { useState } from 'react'
import type { Issue, Status } from '../types/issue'
import {
  formatBeijingTime,
  priorityAccentClasses,
  priorityBadgeClasses,
  priorityLabel,
  statusBadgeClasses,
  statusLabel,
} from '../lib/display'
import {
  COMPACT_BUTTON,
  COMPACT_PRIMARY_BUTTON,
  FOCUS_RING,
  SELECT_INPUT,
  SURFACE_CARD,
} from '../lib/styles'

export type ListState = 'loading' | 'error' | 'success'

const STATUS_OPTIONS: readonly Status[] = ['open', 'in_progress', 'resolved']

export interface IssueListProps {
  /** 已应用筛选后的问题，顺序以服务端返回为准 */
  issues: Issue[]
  /** 未保存的状态草稿（问题 ID → 目标状态）；无草稿的行展示服务端状态 */
  drafts: Record<string, Status>
  /** 首次加载结果；刷新失败不改变该状态，旧数据继续展示 */
  state: ListState
  /** 列表请求或状态保存进行中：禁用全部下拉框与保存按钮 */
  busy: boolean
  /** 正在保存的问题 ID，用于该行按钮显示保存中 */
  savingId: string | null
  /** 存在未保存草稿且服务端状态已被其他客户端更新的行：显示“远端已更新”提示 */
  remoteUpdatedIds: ReadonlySet<string>
  /** 空列表文案：区分“暂无问题”与“没有符合当前条件的问题” */
  emptyText: string
  /** 当前可见条数（筛选后） */
  visibleCount: number
  /** 当前授权全量条数（筛选前） */
  totalCount: number
  /** 空态动作：无数据时重新加载，筛选无结果时清除筛选 */
  emptyAction?: { label: string; onClick: () => void }
  onDraftChange: (id: string, status: Status) => void
  onSave: (issue: Issue) => void
}

/**
 * 问题列表：桌面为白底卡片表格，窄屏同一份数据切换为卡片列表（不依赖横向滚动）。
 * 标题与描述是第一信息列；描述最多两行，完整内容通过行内展开查看。
 * 未保存草稿用浅色底与“未保存”文字提示；远端更新时服务端状态与用户草稿分开展示。
 */
export function IssueList({
  issues,
  drafts,
  state,
  busy,
  savingId,
  remoteUpdatedIds,
  emptyText,
  visibleCount,
  totalCount,
  emptyAction,
  onDraftChange,
  onSave,
}: IssueListProps) {
  const countText = state === 'success' ? `显示 ${visibleCount} / 共 ${totalCount} 条` : '显示 — 条'

  return (
    <section className={SURFACE_CARD} aria-label="问题列表">
      <div className="flex items-center justify-between gap-3 border-b border-slate-200 px-4 py-3 md:px-5">
        <h2 className="text-sm font-semibold text-slate-700">问题列表</h2>
        <span className="text-xs text-slate-400">{countText}</span>
      </div>

      {/* 桌面列头：窄屏隐藏，改由卡片自身表达字段 */}
      <div
        aria-hidden="true"
        className="hidden grid-cols-[minmax(0,1fr)_4.5rem_7.5rem_10.5rem_13rem] gap-x-6 border-b border-slate-100 px-5 py-2.5 text-xs font-medium text-slate-500 md:grid"
      >
        <span>标题与描述</span>
        <span>优先级</span>
        <span>当前状态</span>
        <span>更新时间</span>
        <span>处理操作</span>
      </div>

      {state === 'loading' ? (
        <LoadingSkeleton />
      ) : state === 'error' ? (
        <Placeholder>暂时无法显示问题列表，请稍后重试</Placeholder>
      ) : issues.length === 0 ? (
        <div className="px-4 py-12 text-center md:px-5">
          <p className="text-sm text-slate-500">{emptyText}</p>
          {emptyAction && (
            <button type="button" onClick={emptyAction.onClick} className={`${COMPACT_BUTTON} mt-3`}>
              {emptyAction.label}
            </button>
          )}
        </div>
      ) : (
        <ul className="divide-y divide-slate-100">
          {issues.map(issue => (
            <li key={issue.id}>
              <IssueCard
                issue={issue}
                draft={drafts[issue.id]}
                remoteUpdated={remoteUpdatedIds.has(issue.id)}
                busy={busy}
                saving={savingId === issue.id}
                onDraftChange={onDraftChange}
                onSave={onSave}
              />
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}

/** 加载骨架：结构稳定、静态色块，不做循环闪烁，避免内容跳动 */
function LoadingSkeleton() {
  return (
    <div aria-busy="true" aria-live="polite">
      <span className="sr-only">正在加载问题列表</span>
      {[0, 1, 2, 3].map(index => (
        <div key={index} className="border-b border-slate-100 px-4 py-4 last:border-b-0 md:px-5">
          <div className="h-4 w-2/5 rounded bg-slate-100" />
          <div className="mt-2 h-3 w-3/4 rounded bg-slate-100" />
          <div className="mt-3 flex gap-2">
            <div className="h-5 w-12 rounded-full bg-slate-100" />
            <div className="h-5 w-16 rounded-full bg-slate-100" />
          </div>
        </div>
      ))}
    </div>
  )
}

function Placeholder({ children }: { children: string }) {
  return <p className="px-4 py-12 text-center text-sm text-slate-400 md:px-5">{children}</p>
}

function IssueCard({
  issue,
  draft,
  remoteUpdated,
  busy,
  saving,
  onDraftChange,
  onSave,
}: {
  issue: Issue
  draft?: Status
  remoteUpdated: boolean
  busy: boolean
  saving: boolean
  onDraftChange: (id: string, status: Status) => void
  onSave: (issue: Issue) => void
}) {
  const [expanded, setExpanded] = useState(false)
  const selected = draft ?? issue.status
  const hasDraft = draft !== undefined

  return (
    <article className={`relative ${hasDraft ? 'bg-amber-50/40' : 'bg-white'}`}>
      <span aria-hidden="true" className={`absolute inset-y-0 left-0 w-1 ${priorityAccentClasses(issue.priority)}`} />
      <div className="grid grid-cols-2 gap-x-4 gap-y-2.5 py-4 pr-4 pl-5 md:grid-cols-[minmax(0,1fr)_4.5rem_7.5rem_10.5rem_13rem] md:items-start md:gap-x-6 md:pr-5 md:pl-6">
        <div className="col-span-2 min-w-0 md:col-span-1">
          <h3 className="text-sm font-medium break-words text-slate-900">{issue.title}</h3>
          <p className="mt-1 line-clamp-2 text-xs leading-5 break-words text-slate-500">
            {issue.description || '无描述'}
          </p>
          <button
            type="button"
            onClick={() => setExpanded(value => !value)}
            aria-expanded={expanded}
            aria-label={`${expanded ? '收起' : '展开'}问题「${issue.title}」详情`}
            className={`mt-1.5 rounded text-xs font-medium text-brand-600 transition-colors duration-150 hover:text-brand-700 ${FOCUS_RING}`}
          >
            {expanded ? '收起详情' : '展开详情'}
          </button>
          {expanded && (
            <div className="mt-2 rounded-control border border-slate-200 bg-slate-50 px-3 py-2.5 text-xs leading-5 text-slate-600">
              <p className="break-words">
                <span className="font-medium text-slate-500">完整描述：</span>
                {issue.description || '无描述'}
              </p>
              <p className="mt-1 tabular-nums">
                <span className="font-medium text-slate-500">坐标：</span>
                x {issue.position.x} · y {issue.position.y} · z {issue.position.z}
              </p>
              <p className="mt-1">
                <span className="font-medium text-slate-500">创建时间：</span>
                {formatBeijingTime(issue.createdAt)}
              </p>
            </div>
          )}
        </div>

        <div className="flex items-center gap-2 md:block">
          <span
            className={`inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium ring-1 ring-inset ${priorityBadgeClasses(issue.priority)}`}
          >
            {priorityLabel(issue.priority)}
          </span>
        </div>

        <div>
          <span
            className={`inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium ring-1 ring-inset ${statusBadgeClasses(issue.status)}`}
          >
            {statusLabel(issue.status)}
          </span>
          {hasDraft && (
            <p className="mt-1 text-xs leading-5 text-amber-700">
              未保存 → {statusLabel(selected)}
            </p>
          )}
          {remoteUpdated && <p className="mt-1 text-xs leading-5 text-amber-600">远端已更新，草稿未保存</p>}
        </div>

        <div className="col-span-2 text-xs whitespace-nowrap text-slate-500 md:col-span-1 md:pt-1">
          <span className="md:hidden">更新：</span>
          {formatBeijingTime(issue.updatedAt)}
        </div>

        <div className="col-span-2 flex flex-col gap-2 sm:flex-row md:col-span-1 md:justify-end">
          <select
            aria-label={`修改问题「${issue.title}」的处理状态`}
            value={selected}
            disabled={busy}
            onChange={event => onDraftChange(issue.id, event.target.value as Status)}
            className={`${SELECT_INPUT} w-full md:w-auto`}
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
            className={`${COMPACT_PRIMARY_BUTTON} w-full md:w-auto`}
          >
            {saving ? '保存中…' : '保存'}
          </button>
        </div>
      </div>
    </article>
  )
}
