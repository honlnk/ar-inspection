import { useCallback, useEffect, useRef, useState } from 'react'
import type { CurrentUser } from './api/auth'
import { fetchCurrentUser, logout } from './api/auth'
import { isRequestCancelled } from './api/issues'
import { LoginPage } from './components/LoginPage'
import { IssueBoard } from './components/IssueBoard'

/** 会话门控状态：检查中 → 登录页 / 管理台 */
type SessionPhase = 'checking' | 'login' | 'ready'

/**
 * 应用入口：先恢复会话（GET /api/auth/me），有效且为管理员进入管理台；
 * 巡检员账号在网页端提示角色不适用；会话过期时回到登录页。
 */
export default function App() {
  const [phase, setPhase] = useState<SessionPhase>('checking')
  const [user, setUser] = useState<CurrentUser | null>(null)
  const [loggingOut, setLoggingOut] = useState(false)
  const [roleNotice, setRoleNotice] = useState<string | null>(null)
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
        setPhase('login')
      })
  }, [loggingOut])

  const handleSessionExpired = useCallback(() => {
    setUser(null)
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
    <IssueBoard
      user={user}
      loggingOut={loggingOut}
      onLogout={handleLogout}
      onSessionExpired={handleSessionExpired}
    />
  )
}
