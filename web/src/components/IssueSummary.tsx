import type { Issue } from '../types/issue'
import { SURFACE_CARD } from '../lib/styles'

export interface IssueSummaryProps {
  /** 当前授权的全量问题，不受筛选影响 */
  issues: Issue[]
  /** 首次加载未完成时数值显示占位，不伪装为 0 */
  ready: boolean
}

/**
 * 三项紧凑摘要：全部问题、待处理、处理进度（处理中 + 已解决合并）。
 * 数值全部从当前授权全量列表计算，不做图表与趋势；窄屏保持三列不横向滚动。
 */
export function IssueSummary({ issues, ready }: IssueSummaryProps) {
  const total = issues.length
  const open = issues.filter(issue => issue.status === 'open').length
  const inProgress = issues.filter(issue => issue.status === 'in_progress').length
  const resolved = issues.filter(issue => issue.status === 'resolved').length
  const handled = inProgress + resolved

  return (
    <dl className="grid grid-cols-3 gap-3" aria-label="问题摘要">
      <SummaryItem label="全部问题" value={ready ? total : null} detail="当前授权范围" tone="text-slate-900" />
      <SummaryItem label="待处理" value={ready ? open : null} detail="需要跟进" tone="text-orange-700" />
      <SummaryItem
        label="处理进度"
        value={ready ? handled : null}
        detail={ready ? `处理中 ${inProgress} · 已解决 ${resolved}` : '处理中与已解决'}
        tone="text-brand-700"
      />
    </dl>
  )
}

function SummaryItem({
  label,
  value,
  detail,
  tone,
}: {
  label: string
  value: number | null
  detail: string
  tone: string
}) {
  return (
    <div className={`${SURFACE_CARD} px-3 py-3 sm:px-4`}>
      <dt className="truncate text-xs font-medium text-slate-500">{label}</dt>
      <dd className={`mt-1 text-xl leading-7 font-semibold tabular-nums sm:text-2xl sm:leading-8 ${tone}`}>
        {value === null ? '—' : value}
      </dd>
      <dd className="mt-0.5 text-xs leading-4 text-slate-400">{detail}</dd>
    </div>
  )
}
