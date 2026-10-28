import { useCallback, useEffect, useRef, useState } from 'react'
import type { CurrentUser } from './api/auth'
import { fetchCurrentUser, logout } from './api/auth'
import { isRequestCancelled } from './api/issues'
import type { RealtimeStatus } from './lib/realtime'
import { LoginPage } from './components/LoginPage'
import { AdminShell, type AdminPage } from './components/AdminShell'
import { IssueBoard } from './components/IssueBoard'
import { PersonnelBoard } from './components/PersonnelBoard'

/** 会话门控状态：检查中 → 登录页 / 管理台 */
type SessionPhase = 'checking' | 'login' | 'ready'

/**
 * 应用入口：先恢复会话（GET /api/auth/me），有效且为管理员进入管理台；
 * 巡检员账号在网页端提示角色不适用；会话过期时回到登录页。
 * 计划 008：管理台为「问题列表 / 人员管理」双入口；外壳与导航状态由本组件持有，
 * 问题工作台切换时保持挂载（实时连接与草稿不丢），仅显示/隐藏。
 */
export default function App() {
  const [phase, setPhase] = useState<SessionPhase>('checking')
  const [user, setUser] = useState<CurrentUser | null>(null)
  const [loggingOut, setLoggingOut] = useState(false)
  const [roleNotice, setRoleNotice] = useState<string | null>(null)
  const [page, setPage] = useState<AdminPage>('issues')
  const [realtime, setRealtime] = useState<RealtimeStatus>('closed')
  const requestRef = useRef<AbortController | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    requestRef.current = controller
    fetchCurrentUser(controller.signal)
      .then(currentUser => {
        if (controller.signal.aborted) return
        if (currentUser.role !== 'admin') {
          // 巡检员账号不进入网页管理台：回登录页并提示角色不适用
          setRoleNotice('巡检员账号请使用 Unity 巡检端，网页管理端请使用管理员账号登录')
          setPhase('login')
          return
        }
        setUser(currentUser)
        setPhase('ready')
      })
      .catch(error => {
        if (controller.signal.aborted || isRequestCancelled(error)) return
        // 未登录/过期/后端不可用统一进入登录页，由登录动作给出后续反馈
        setPhase('login')
      })
    return () => {
      controller.abort()
    }
  }, [])

  const handleLoggedIn = useCallback((loggedIn: CurrentUser) => {
    if (loggedIn.role !== 'admin') {
      // 登录接口本身允许巡检员（Unity 同一接口），但网页端只服务管理员
      setRoleNotice('巡检员账号请使用 Unity 巡检端，网页管理端请使用管理员账号登录')
      return
    }
    setRoleNotice(null)
    setUser(loggedIn)
    setPage('issues')
    setRealtime('closed')
    setPhase('ready')
  }, [])

  const handleLogout = useCallback(() => {
    if (loggingOut) return
    setLoggingOut(true)
    const controller = new AbortController()
    requestRef.current = controller
    logout(controller.signal)
      .catch(() => {
        // 退出请求失败也回到登录页：本地 Cookie 已尽力清除，会话以服务端 12 小时上限兜底
      })
      .finally(() => {
        if (controller.signal.aborted) return
        setLoggingOut(false)
        setUser(null)
        setPage('issues')
        setPhase('login')
      })
  }, [loggingOut])

  const handleSessionExpired = useCallback(() => {
    setUser(null)
    setPage('issues')
    setPhase('login')
  }, [])

  if (phase === 'checking') {
    return (
      <div className="flex min-h-screen items-center justify-center bg-slate-100">
        <p className="text-sm text-slate-500">正在检查登录状态…</p>
      </div>
    )
  }

  if (phase === 'login' || user === null) {
    return (
      <>
        {roleNotice && (
          <div className="flex justify-center bg-amber-50 px-4 py-2" role="alert">
            <p className="text-sm text-amber-700">{roleNotice}</p>
          </div>
        )}
        <LoginPage onLoggedIn={handleLoggedIn} />
      </>
    )
  }

  return (
    <AdminShell
      user={user}
      realtime={realtime}
      loggingOut={loggingOut}
      onLogout={handleLogout}
      page={page}
      onPageChange={setPage}
    >
      {/* 双面板同时挂载：问题工作台切走时保持挂载（实时连接、草稿与筛选不丢） */}
      <div className={page === 'issues' ? '' : 'hidden'}>
        <IssueBoard
          hidden={page !== 'issues'}
          onSessionExpired={handleSessionExpired}
          onRealtimeChange={setRealtime}
        />
      </div>
      <div className={page === 'personnel' ? '' : 'hidden'}>
        <PersonnelBoard onSessionExpired={handleSessionExpired} />
      </div>
    </AdminShell>
  )
}
