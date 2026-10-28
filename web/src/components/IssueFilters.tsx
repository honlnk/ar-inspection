import type { Priority, Status } from '../types/issue'
import { priorityLabel, statusLabel } from '../lib/display'
import { EMPTY_FILTERS, type IssueFilterValues } from '../lib/filters'

const PRIORITY_OPTIONS: readonly Priority[] = ['low', 'medium', 'high']
const STATUS_OPTIONS: readonly Status[] = ['open', 'in_progress', 'resolved']

export interface IssueFiltersProps {
  values: IssueFilterValues
  visibleCount: number
  totalCount: number
  disabled: boolean
  onChange: (next: IssueFilterValues) => void
}

/**
 * 筛选条：标题关键词（匹配标题与描述）、优先级、状态三个条件组合；
 * 显示“显示 X / 共 Y 条”，一键清空恢复全部。筛选只作用于当前展示，不发请求。
 */
export function IssueFilters({ values, visibleCount, totalCount, disabled, onChange }: IssueFiltersProps) {
  const hasActive =
    values.text.trim() !== '' || values.priority !== 'all' || values.status !== 'all'

  return (
    <section className="flex flex-wrap items-end gap-3 rounded-lg border border-slate-200 bg-white px-4 py-3 shadow-sm">
      <label className="block min-w-[200px] flex-1">
        <span className="mb-1 block text-xs font-medium text-slate-500">搜索标题与描述</span>
        <input
          type="search"
          value={values.text}
          disabled={disabled}
          placeholder="输入关键词"
          onChange={e => onChange({ ...values, text: e.target.value })}
          className="w-full rounded-md border border-slate-300 px-3 py-2 text-sm text-slate-900 disabled:cursor-not-allowed disabled:opacity-50"
        />
      </label>

      <label className="block">
        <span className="mb-1 block text-xs font-medium text-slate-500">优先级</span>
        <select
          aria-label="按优先级筛选"
          value={values.priority}
          disabled={disabled}
          onChange={e => onChange({ ...values, priority: e.target.value as IssueFilterValues['priority'] })}
          className="rounded-md border border-slate-300 bg-white px-2.5 py-2 text-sm text-slate-700 disabled:cursor-not-allowed disabled:opacity-50"
        >
          <option value="all">全部优先级</option>
          {PRIORITY_OPTIONS.map(option => (
            <option key={option} value={option}>
              {priorityLabel(option)}
            </option>
          ))}
        </select>
      </label>

      <label className="block">
        <span className="mb-1 block text-xs font-medium text-slate-500">状态</span>
        <select
          aria-label="按状态筛选"
          value={values.status}
          disabled={disabled}
          onChange={e => onChange({ ...values, status: e.target.value as IssueFilterValues['status'] })}
          className="rounded-md border border-slate-300 bg-white px-2.5 py-2 text-sm text-slate-700 disabled:cursor-not-allowed disabled:opacity-50"
        >
          <option value="all">全部状态</option>
          {STATUS_OPTIONS.map(option => (
            <option key={option} value={option}>
              {statusLabel(option)}
            </option>
          ))}
        </select>
      </label>

      <button
        type="button"
        disabled={disabled || !hasActive}
        onClick={() => onChange({ ...EMPTY_FILTERS })}
        className="rounded-md border border-slate-300 px-3 py-2 text-sm text-slate-600 hover:bg-slate-50 disabled:cursor-not-allowed disabled:opacity-50"
      >
        清空筛选
      </button>

      <span className="ml-auto text-xs text-slate-400">显示 {visibleCount} / 共 {totalCount} 条</span>
    </section>
  )
}

