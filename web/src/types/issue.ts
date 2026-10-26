// 与后端、Unity DTO 完全一致的数据契约；字段名不得增删或改名。

/** 问题优先级，三端统一枚举 */
export type Priority = 'low' | 'medium' | 'high'

/** 处理状态，三端统一枚举；新建默认 open */
export type Status = 'open' | 'in_progress' | 'resolved'

/** AR 标记三维坐标 */
export interface Position {
  x: number
  y: number
  z: number
}

/** 问题实体；createdAt/updatedAt 为带 +08:00 偏移的 RFC 3339 字符串，由后端生成 */
export interface Issue {
  id: string
  title: string
  description: string
  priority: Priority
  status: Status
  position: Position
  createdAt: string
  updatedAt: string
}
