// 共享工具类常量：集中按钮、输入、卡片与焦点样式，避免各组件各写一套。
// 只使用 Tailwind 工具类；动态状态色仍通过 display.ts 的完整静态类名映射表达。

/** 内容面板：白色近白表面 + 细分隔线 + 轻阴影 */
export const SURFACE_CARD = 'rounded-card border border-slate-200 bg-white shadow-panel'

/** 主操作：同一视图只保留一个实色主按钮 */
export const PRIMARY_BUTTON =
  'inline-flex min-h-10 items-center justify-center gap-1.5 rounded-control bg-brand-600 px-4 py-2 text-sm font-medium text-white transition-colors duration-150 hover:bg-brand-700 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-brand-500 focus-visible:ring-offset-2 disabled:cursor-not-allowed disabled:opacity-50'

/** 次要操作：白底边框，不与主操作争夺注意力 */
export const SECONDARY_BUTTON =
  'inline-flex min-h-10 items-center justify-center gap-1.5 rounded-control border border-slate-300 bg-white px-3.5 py-2 text-sm font-medium text-slate-700 transition-colors duration-150 hover:bg-slate-50 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-brand-500 focus-visible:ring-offset-2 disabled:cursor-not-allowed disabled:opacity-50'

/** 行内小号次要按钮（列表操作） */
export const COMPACT_BUTTON =
  'inline-flex min-h-9 items-center justify-center rounded-control border border-slate-300 bg-white px-3 py-1.5 text-sm text-slate-600 transition-colors duration-150 hover:bg-slate-50 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-brand-500 focus-visible:ring-offset-2 disabled:cursor-not-allowed disabled:opacity-50'

/** 行内小号主按钮（列表保存） */
export const COMPACT_PRIMARY_BUTTON =
  'inline-flex min-h-9 items-center justify-center rounded-control bg-brand-600 px-3 py-1.5 text-sm font-medium text-white transition-colors duration-150 hover:bg-brand-700 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-brand-500 focus-visible:ring-offset-2 disabled:cursor-not-allowed disabled:opacity-50'

export const FIELD_LABEL = 'mb-1 block text-xs font-medium text-slate-500'

/** 文本输入：焦点环清楚可见，禁用态仍可辨识 */
export const TEXT_INPUT =
  'w-full rounded-control border border-slate-300 bg-white px-3 py-2 text-sm text-slate-900 placeholder:text-slate-400 transition-colors duration-150 focus:border-brand-500 focus:outline-none focus:ring-2 focus:ring-brand-200 disabled:cursor-not-allowed disabled:bg-slate-50 disabled:text-slate-500'

/** 下拉选择：与文本输入同一视觉等级 */
export const SELECT_INPUT =
  'rounded-control border border-slate-300 bg-white px-2.5 py-2 text-sm text-slate-700 transition-colors duration-150 focus:border-brand-500 focus:outline-none focus:ring-2 focus:ring-brand-200 disabled:cursor-not-allowed disabled:bg-slate-50 disabled:text-slate-500'

/** 键盘可聚焦控件的统一焦点环（用于额外按钮/链接） */
export const FOCUS_RING =
  'focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-brand-500 focus-visible:ring-offset-2'
