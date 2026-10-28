import { useCallback, useRef, useState } from 'react'
import { login } from '../api/auth'
import { isRequestCancelled } from '../api/issues'
import type { CurrentUser } from '../api/auth'

/** 登录页：本地演示账号登录；凭据错误与服务端错误均给出中文提示，不白屏 */
export function LoginPage({ onLoggedIn }: { onLoggedIn: (user: CurrentUser) => void }) {
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const requestRef = useRef<AbortController | null>(null)

  const handleSubmit = useCallback(
    (event: React.FormEvent) => {
      event.preventDefault()
      if (submitting) return
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
    [username, password, submitting, onLoggedIn],
  )

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
              disabled={submitting}
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
              disabled={submitting}
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
            disabled={submitting}
            className="w-full rounded-md bg-brand-600 px-3.5 py-2 text-sm font-medium text-white hover:bg-brand-700 disabled:cursor-not-allowed disabled:opacity-50"
          >
            {submitting ? '登录中…' : '登录'}
          </button>
        </form>

        <p className="mt-4 text-xs leading-5 text-slate-400">
          本地演示账号：管理员 admin；巡检员账号请在 Unity 端使用 inspector 登录。
        </p>
      </div>
    </div>
  )
}
