// 展示映射：枚举中文文案、徽标颜色与北京时间格式化。
// 业务时间固定按 Asia/Shanghai 展示，不跟随浏览器本地时区；
// 非法时间与未知枚举原样呈现或使用中性样式，不抛异常、不伪装成已知值。

import type { Priority, Status } from '../types/issue'

const PRIORITY_LABELS: Record<Priority, string> = {
  low: '低',
  medium: '中',
  high: '高',
}

const STATUS_LABELS: Record<Status, string> = {
  open: '待处理',
  in_progress: '处理中',
  resolved: '已解决',
}

/** 动态颜色通过完整静态类名映射表达，不在运行时拼接 Tailwind 类名 */
const PRIORITY_BADGE_CLASSES: Record<Priority, string> = {
  low: 'bg-slate-100 text-slate-600',
  medium: 'bg-amber-50 text-amber-700',
  high: 'bg-red-50 text-red-700',
}

const STATUS_BADGE_CLASSES: Record<Status, string> = {
  open: 'bg-red-50 text-red-700',
  in_progress: 'bg-amber-50 text-amber-700',
  resolved: 'bg-emerald-50 text-emerald-700',
}

const UNKNOWN_BADGE_CLASSES = 'bg-slate-100 text-slate-500'

/** 优先级中文文案；未知枚举原样显示，不猜测含义 */
export function priorityLabel(value: string): string {
  return value in PRIORITY_LABELS ? PRIORITY_LABELS[value as Priority] : value
}

/** 处理状态中文文案；未知枚举原样显示 */
export function statusLabel(value: string): string {
  return value in STATUS_LABELS ? STATUS_LABELS[value as Status] : value
}

export function priorityBadgeClasses(value: string): string {
  return value in PRIORITY_BADGE_CLASSES ? PRIORITY_BADGE_CLASSES[value as Priority] : UNKNOWN_BADGE_CLASSES
}

export function statusBadgeClasses(value: string): string {
  return value in STATUS_BADGE_CLASSES ? STATUS_BADGE_CLASSES[value as Status] : UNKNOWN_BADGE_CLASSES
}

const beijingFormatter = new Intl.DateTimeFormat('zh-CN', {
  timeZone: 'Asia/Shanghai',
  year: 'numeric',
  month: '2-digit',
  day: '2-digit',
  hour: '2-digit',
  minute: '2-digit',
  second: '2-digit',
  hour12: false,
})

/** 按北京时间展示服务端时间；无法解析时原样返回字符串，不抛异常 */
export function formatBeijingTime(value: string): string {
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return value
  return beijingFormatter.format(date)
}
