import type { Priority, Status } from '../types/issue'
import { priorityLabel, statusLabel } from '../lib/display'
import { EMPTY_FILTERS, type IssueFilterValues } from '../lib/filters'
import { COMPACT_BUTTON, FIELD_LABEL, SELECT_INPUT, SURFACE_CARD, TEXT_INPUT } from '../lib/styles'

const PRIORITY_OPTIONS: readonly Priority[] = ['low', 'medium', 'high']
const STATUS_OPTIONS: readonly Status[] = ['open', 'in_progress', 'resolved']

export interface IssueFiltersProps {
  values: IssueFilterValues
  disabled: boolean
  onChange: (next: IssueFilterValues) => void
}

/**
 * 筛选工具栏：标题/描述搜索框、优先级、状态三个条件组合，一键清空恢复全部。
 * 筛选只作用于当前展示，不发请求；结果计数靠近列表，不在此处重复。
 * 窄屏下条件自然换行，搜索框占满一行，不产生页面横向滚动。
 */
export function IssueFilters({ values, disabled, onChange }: IssueFiltersProps) {
  const hasActive = values.text.trim() !== '' || values.priority !== 'all' || values.status !== 'all'

  return (
    <section className={`${SURFACE_CARD} flex flex-wrap items-end gap-3 px-4 py-3`} aria-label="问题筛选">
      <label className="block min-w-[180px] flex-1">
        <span className={FIELD_LABEL}>搜索标题与描述</span>
        <input
          type="search"
          value={values.text}
          disabled={disabled}
          placeholder="输入关键词"
          onChange={e => onChange({ ...values, text: e.target.value })}
          className={TEXT_INPUT}
        />
      </label>

      <label className="block w-[calc(50%-0.375rem)] sm:w-auto">
        <span className={FIELD_LABEL}>优先级</span>
        <select
          aria-label="按优先级筛选"
          value={values.priority}
          disabled={disabled}
          onChange={e => onChange({ ...values, priority: e.target.value as IssueFilterValues['priority'] })}
          className={`${SELECT_INPUT} w-full`}
        >
          <option value="all">全部优先级</option>
          {PRIORITY_OPTIONS.map(option => (
            <option key={option} value={option}>
              {priorityLabel(option)}
            </option>
          ))}
        </select>
      </label>

      <label className="block w-[calc(50%-0.375rem)] sm:w-auto">
        <span className={FIELD_LABEL}>状态</span>
        <select
          aria-label="按状态筛选"
          value={values.status}
          disabled={disabled}
          onChange={e => onChange({ ...values, status: e.target.value as IssueFilterValues['status'] })}
          className={`${SELECT_INPUT} w-full`}
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
        className={`${COMPACT_BUTTON} w-[calc(50%-0.375rem)] sm:w-auto`}
      >
        清空筛选
      </button>
    </section>
  )
}
