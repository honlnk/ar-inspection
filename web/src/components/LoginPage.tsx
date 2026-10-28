import { useCallback, useRef, useState } from 'react'
import { login, quickLogin } from '../api/auth'
import { isRequestCancelled } from '../api/issues'
import type { CurrentUser } from '../api/auth'

/** 登录页：本地演示账号登录；凭据错误与服务端错误均给出中文提示，不白屏 */
export function LoginPage({ onLoggedIn }: { onLoggedIn: (user: CurrentUser) => void }) {
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [submitting, setSubmitting] = useState(false)
  const [quickSubmitting, setQuickSubmitting] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const requestRef = useRef<AbortController | null>(null)
  const busy = submitting || quickSubmitting

  const handleSubmit = useCallback(
    (event: React.FormEvent) => {
      event.preventDefault()
      if (busy) return
      setError(null)
      if (username.trim() === '' || password === '') {
        setError('请输入用户名和密码')
        return
      }
      const controller = new AbortController()
      requestRef.current?.abort()
      requestRef.current = controller
      setSubmitting(true)
      login(username.trim(), password, controller.signal)
        .then(user => onLoggedIn(user))
        .catch(err => {
          if (controller.signal.aborted || isRequestCancelled(err)) return
          setError(err instanceof Error ? err.message : '登录失败，请稍后重试')
        })
        .finally(() => {
          if (!controller.signal.aborted) setSubmitting(false)
        })
    },
    [username, password, busy, onLoggedIn],
  )

  /** 开发期一键登录管理员：不涉及密码；后端未启用时展示后端中文提示 */
  const handleQuickLogin = useCallback(() => {
    if (busy) return
    setError(null)
    const controller = new AbortController()
    requestRef.current?.abort()
    requestRef.current = controller
    setQuickSubmitting(true)
    quickLogin('admin', controller.signal)
      .then(user => onLoggedIn(user))
      .catch(err => {
        if (controller.signal.aborted || isRequestCancelled(err)) return
        setError(err instanceof Error ? err.message : '快捷登录失败，请稍后重试')
      })
      .finally(() => {
        if (!controller.signal.aborted) setQuickSubmitting(false)
      })
  }, [busy, onLoggedIn])

  return (
    <div className="flex min-h-screen items-center justify-center bg-slate-100 px-4">
      <div className="w-full max-w-sm rounded-lg border border-slate-200 bg-white p-6 shadow-sm">
        <h1 className="text-lg font-semibold text-slate-900">现场巡检问题标注 · 管理端</h1>
        <p className="mt-1 text-sm text-slate-500">请使用管理员账号登录</p>

        <form className="mt-6 space-y-4" onSubmit={handleSubmit}>
          <label className="block">
            <span className="mb-1 block text-sm font-medium text-slate-700">用户名</span>
            <input
              type="text"
              autoComplete="username"
              value={username}
              disabled={busy}
              onChange={e => setUsername(e.target.value)}
              className="w-full rounded-md border border-slate-300 px-3 py-2 text-sm text-slate-900 disabled:cursor-not-allowed disabled:opacity-50"
              placeholder="admin"
            />
          </label>
          <label className="block">
            <span className="mb-1 block text-sm font-medium text-slate-700">密码</span>
            <input
              type="password"
              autoComplete="current-password"
              value={password}
              disabled={busy}
              onChange={e => setPassword(e.target.value)}
              className="w-full rounded-md border border-slate-300 px-3 py-2 text-sm text-slate-900 disabled:cursor-not-allowed disabled:opacity-50"
              placeholder="请输入密码"
            />
          </label>

          {error && (
            <p role="alert" className="rounded-md bg-red-50 px-3 py-2 text-sm text-red-700">
              {error}
            </p>
          )}

          <button
            type="submit"
            disabled={busy}
            className="w-full rounded-md bg-brand-600 px-3.5 py-2 text-sm font-medium text-white hover:bg-brand-700 disabled:cursor-not-allowed disabled:opacity-50"
          >
            {submitting ? '登录中…' : '登录'}
          </button>
        </form>

        {import.meta.env.DEV && (
          <div className="mt-3">
            <button
              type="button"
              onClick={handleQuickLogin}
              disabled={busy}
              className="w-full rounded-md border border-slate-300 bg-white px-3.5 py-2 text-sm font-medium text-slate-700 hover:bg-slate-50 disabled:cursor-not-allowed disabled:opacity-50"
            >
              {quickSubmitting ? '快捷登录中…' : '开发快捷登录（admin）'}
            </button>
            <p className="mt-1 text-xs leading-5 text-slate-400">
              仅开发环境显示；后端需设置 QUICK_LOGIN_ENABLED=1。
            </p>
          </div>
        )}

        <p className="mt-4 text-xs leading-5 text-slate-400">
          本地演示账号：管理员 admin；巡检员账号请在 Unity 端使用 inspector 登录。
        </p>
      </div>
    </div>
  )
}
