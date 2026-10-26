/**
 * 问题列表静态结构：卡片容器 + 表头 + 占位说明。
 * 本期为骨架阶段：不请求后端、不展示虚构问题数据；
 * 真实列表加载、状态修改在后续业务计划中接入。
 */
export function IssueList() {
  return (
    <section className="rounded-lg border border-slate-200 bg-white shadow-sm">
      <div className="flex items-center justify-between border-b border-slate-200 px-4 py-3 md:px-5">
        <h2 className="text-sm font-semibold text-slate-700">问题列表</h2>
        <span className="text-xs text-slate-400">共 0 条</span>
      </div>
      <div className="overflow-x-auto">
        <table className="w-full min-w-[560px] text-left text-sm">
          <thead>
            <tr className="border-b border-slate-100 text-xs text-slate-500">
              <th className="px-4 py-2.5 font-medium md:px-5">标题</th>
              <th className="px-4 py-2.5 font-medium">优先级</th>
              <th className="px-4 py-2.5 font-medium">状态</th>
              <th className="px-4 py-2.5 font-medium">更新时间</th>
              <th className="px-4 py-2.5 font-medium">操作</th>
            </tr>
          </thead>
          <tbody>
            <tr>
              <td colSpan={5} className="px-4 py-10 text-center text-sm text-slate-400 md:px-5">
                问题数据功能将在后续迭代接入
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </section>
  )
}
