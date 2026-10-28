// 筛选纯逻辑：类型、空值常量与过滤函数（供组件与测试共用，不依赖 React）。

import type { Issue, Priority, Status } from '../types/issue'

export interface IssueFilterValues {
  text: string
  priority: Priority | 'all'
  status: Status | 'all'
}

export const EMPTY_FILTERS: IssueFilterValues = { text: '', priority: 'all', status: 'all' }

/** 应用筛选条件：标题/描述关键词（去首尾空白、忽略大小写）+ 优先级 + 状态，不改行顺序 */
export function applyIssueFilters(issues: Issue[], filters: IssueFilterValues): Issue[] {
  const keyword = filters.text.trim().toLowerCase()
  return issues.filter(issue => {
    if (filters.priority !== 'all' && issue.priority !== filters.priority) return false
    if (filters.status !== 'all' && issue.status !== filters.status) return false
    if (keyword !== '') {
      const title = issue.title.toLowerCase()
      const description = issue.description.toLowerCase()
      if (!title.includes(keyword) && !description.includes(keyword)) return false
    }
    return true
  })
}
