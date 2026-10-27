// 展示映射测试：枚举中文文案、未知枚举兜底、徽标类名与北京时间格式化。

import { describe, expect, it } from 'vitest'
import { formatBeijingTime, priorityBadgeClasses, priorityLabel, statusBadgeClasses, statusLabel } from './display'

describe('枚举文案', () => {
  it('三种优先级映射为低/中/高', () => {
    expect(priorityLabel('low')).toBe('低')
    expect(priorityLabel('medium')).toBe('中')
    expect(priorityLabel('high')).toBe('高')
  })

  it('三种状态映射为待处理/处理中/已解决', () => {
    expect(statusLabel('open')).toBe('待处理')
    expect(statusLabel('in_progress')).toBe('处理中')
    expect(statusLabel('resolved')).toBe('已解决')
  })

  it('未知枚举原样显示，不抛异常', () => {
    expect(priorityLabel('urgent')).toBe('urgent')
    expect(statusLabel('done')).toBe('done')
  })
})

describe('徽标类名', () => {
  it('已知枚举返回对应的完整静态类名', () => {
    expect(priorityBadgeClasses('high')).toContain('bg-red-50')
    expect(statusBadgeClasses('resolved')).toContain('bg-emerald-50')
  })

  it('未知枚举回退到中性类名', () => {
    expect(priorityBadgeClasses('urgent')).toBe('bg-slate-100 text-slate-500')
    expect(statusBadgeClasses('done')).toBe('bg-slate-100 text-slate-500')
  })
})

describe('北京时间格式化', () => {
  it('带 +08:00 偏移的服务端时间按北京时间展示', () => {
    expect(formatBeijingTime('2026-10-27T10:00:05+08:00')).toBe('2026/10/27 10:00:05')
  })

  it('其他时区时间换算为北京时间，不跟随浏览器本地时区', () => {
    expect(formatBeijingTime('2026-10-27T22:30:05Z')).toBe('2026/10/28 06:30:05')
  })

  it('非法时间原样返回，不抛异常', () => {
    expect(formatBeijingTime('not-a-date')).toBe('not-a-date')
  })
})
