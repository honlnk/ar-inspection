import { AdminShell } from './components/AdminShell'
import { IssueList } from './components/IssueList'

/** 骨架阶段的静态管理台外壳：组合侧栏、顶栏、标题行与问题列表占位结构 */
export default function App() {
  return (
    <AdminShell>
      <div className="space-y-4">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div>
            <h1 className="text-xl font-semibold">问题列表</h1>
            <p className="mt-1 text-sm text-slate-500">查看巡检人员上报的问题并更新处理状态</p>
          </div>
          <button
            type="button"
            disabled
            title="刷新功能尚未接入后端"
            className="cursor-not-allowed rounded-md bg-brand-600 px-3.5 py-2 text-sm font-medium text-white opacity-50"
          >
            刷新
          </button>
        </div>
        <IssueList />
      </div>
    </AdminShell>
  )
}
