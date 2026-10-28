import { useCallback, useRef, useState } from 'react'
import { login } from '../api/auth'
import { isRequestCancelled } from '../api/issues'
import type { CurrentUser } from '../api/auth'
import { PRIMARY_BUTTON, SURFACE_CARD, TEXT_INPUT } from '../lib/styles'

/**
 * 登录页：浅中性底 + 居中窄卡片。用户名、密码、登录按钮纵向排列；
 * 失败在表单内提示并保留输入，提交中禁用并显示“登录中…”，不跳空白页。
 * 页面只说明账号由后端环境配置，不展示任何真实密码。
 */
export function LoginPage({
  onLoggedIn,
  notice,
}: {
  onLoggedIn: (user: CurrentUser) => void
  /** 角色提示（如巡检员误入管理端）：显示在登录卡片内，不额外占用页面高度 */
  notice?: string | null
}) {
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
    <div className="flex min-h-screen flex-col items-center justify-center bg-canvas px-4 py-10">
      <main className={`w-full max-w-sm ${SURFACE_CARD} p-6 sm:p-7`}>
        <div className="flex items-center gap-2.5">
          <span aria-hidden="true" className="flex h-9 w-9 items-center justify-center rounded-control bg-brand-600">
            <span className="h-4 w-4 rounded-[3px] border-2 border-white" />
          </span>
          <div className="min-w-0">
            <h1 className="text-base leading-6 font-semibold text-slate-900">现场巡检问题标注</h1>
            <p className="text-xs leading-5 text-slate-500">现场问题上报与处理</p>
          </div>
        </div>

        <p className="mt-5 text-sm text-slate-600">请使用管理员账号登录管理端</p>

        {notice && (
          <p role="alert" className="mt-3 rounded-control bg-amber-50 px-3 py-2 text-sm leading-5 text-amber-700 ring-1 ring-amber-200">
            {notice}
          </p>
        )}

        <form className="mt-5 space-y-4" onSubmit={handleSubmit} noValidate>
          <label className="block">
            <span className="mb-1 block text-xs font-medium text-slate-600">用户名</span>
            <input
              type="text"
              autoComplete="username"
              value={username}
              disabled={submitting}
              onChange={e => setUsername(e.target.value)}
              className={TEXT_INPUT}
              placeholder="admin"
            />
          </label>
          <label className="block">
            <span className="mb-1 block text-xs font-medium text-slate-600">密码</span>
            <input
              type="password"
              autoComplete="current-password"
              value={password}
              disabled={submitting}
              onChange={e => setPassword(e.target.value)}
              className={TEXT_INPUT}
              placeholder="请输入密码"
            />
          </label>

          {error && (
            <p role="alert" className="rounded-control bg-red-50 px-3 py-2 text-sm leading-5 text-red-700 ring-1 ring-red-200">
              {error}
            </p>
          )}

          <button type="submit" disabled={submitting} className={`${PRIMARY_BUTTON} w-full`}>
            {submitting ? '登录中…' : '登录'}
          </button>
        </form>

        <p className="mt-4 border-t border-slate-100 pt-4 text-xs leading-5 text-slate-400">
          本地演示账号由后端环境配置，不在此页展示密码。巡检员账号请在 Unity 巡检端登录。
        </p>
      </main>
      <p className="mt-6 text-xs text-slate-400">园区巡检 · 问题管理端</p>
    </div>
  )
}
