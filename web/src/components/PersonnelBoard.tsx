import { useCallback, useEffect, useRef, useState } from 'react'
import type { ManagedAccount } from '../api/users'
import { createUser, deleteUser, fetchUsers, updateUser } from '../api/users'
import { isRequestCancelled } from '../api/issues'
import { isAuthenticationError } from '../api/auth'
import { formatBeijingTime } from '../lib/display'
import { Feedback } from './Feedback'

/** 列表加载结果状态；操作后刷新不改变它，刷新进度单独表达 */
type LoadState = 'loading' | 'success' | 'error'

function errorMessage(error: unknown): string {
  return error instanceof Error ? error.message : '发生未知错误，请重试'
}

/** 用户名输入约束与后端一致：2～32 位，英文字母、数字、下划线、连字符 */
const USERNAME_PATTERN = '[A-Za-z0-9_-]{2,32}'

export interface PersonnelBoardProps {
  /** 会话过期（REST 401）时通知 App 回登录页 */
  onSessionExpired: () => void
}

/**
 * 人员管理页（计划 008）：管理员维护巡检员账号。
 * 列表 + 新增表单 + 行内编辑 + 行内删除确认；管理员账号以只读形式分区展示。
 * 人员变更不做实时广播：所有操作成功后刷新列表，保持页面与服务端一致。
 */
export function PersonnelBoard({ onSessionExpired }: PersonnelBoardProps) {
  const [accounts, setAccounts] = useState<ManagedAccount[]>([])
  const [loadState, setLoadState] = useState<LoadState>('loading')
  const [loadError, setLoadError] = useState<string | null>(null)
  const [refreshing, setRefreshing] = useState(false)
  const [refreshError, setRefreshError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)

  const requestRef = useRef<AbortController | null>(null)

  const load = useCallback(
    async (mode: 'initial' | 'refresh') => {
      const controller = new AbortController()
      requestRef.current?.abort()
      requestRef.current = controller
      try {
        const fresh = await fetchUsers(controller.signal)
        if (controller.signal.aborted) return
        setAccounts(fresh)
        setLoadState('success')
        setLoadError(null)
        setRefreshError(null)
      } catch (error) {
        if (controller.signal.aborted || isRequestCancelled(error)) return
        if (isAuthenticationError(error)) {
          onSessionExpired()
          return
        }
        if (mode === 'refresh') {
          setRefreshError(errorMessage(error))
        } else {
          setLoadState('error')
          setLoadError(errorMessage(error))
        }
      } finally {
        if (mode === 'refresh' && !controller.signal.aborted) setRefreshing(false)
      }
    },
    [onSessionExpired],
  )

  useEffect(() => {
    // oxlint 误报：load 的 setState 均在 await 之后异步执行，非 effect 同步路径
    // eslint-disable-next-line react/set-state-in-effect
    void load('initial')
    return () => {
      requestRef.current?.abort()
    }
  }, [load])

  const handleRefresh = useCallback(() => {
    if (loadState === 'loading' || refreshing) return
    setNotice(null)
    setRefreshing(true)
    setRefreshError(null)
    void load('refresh')
  }, [loadState, refreshing, load])

  /** 操作成功后的统一收口：提示 + 刷新列表 */
  const afterAction = useCallback(
    (message: string) => {
      setNotice(message)
      void load('refresh')
    },
    [load],
  )

  const inspectors = accounts.filter(account => account.role === 'inspector')
  const admins = accounts.filter(account => account.role === 'admin')

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold">人员管理</h1>
          <p className="mt-1 text-sm text-slate-500">维护巡检员账号：新增后立即可登录 Unity 巡检端上报问题</p>
        </div>
        <button
          type="button"
          disabled={loadState === 'loading' || refreshing}
          onClick={handleRefresh}
          className="rounded-md bg-brand-600 px-3.5 py-2 text-sm font-medium text-white hover:bg-brand-700 disabled:cursor-not-allowed disabled:opacity-50"
        >
          {refreshing ? '刷新中…' : '刷新'}
        </button>
      </div>

      {notice && <Feedback kind="success">{notice}</Feedback>}
      {loadState === 'error' && (
        <Feedback kind="error" action={{ label: '重试', onClick: () => void load('initial') }}>
          无法获取账号列表：{loadError}。请确认后端服务可用后重试。
        </Feedback>
      )}
      {loadState === 'loading' && <Feedback kind="loading">正在从后端加载账号列表…</Feedback>}
      {refreshing && <Feedback kind="loading">正在刷新，当前显示的仍是已有数据…</Feedback>}
      {refreshError && (
        <Feedback kind="error" action={{ label: '重试', onClick: handleRefresh }}>
          刷新失败，当前数据可能不是最新数据：{refreshError}
        </Feedback>
      )}

      <CreateInspectorForm
        disabled={loadState !== 'success'}
        onCreated={name => afterAction(`巡检员「${name}」已创建，立即可登录`)}
        onSessionExpired={onSessionExpired}
      />

      <section className="rounded-lg border border-slate-200 bg-white shadow-sm">
        <div className="flex items-center justify-between border-b border-slate-200 px-4 py-3 md:px-5">
          <h2 className="text-sm font-semibold text-slate-700">巡检员账号</h2>
          <span className="text-xs text-slate-400">共 {loadState === 'success' ? inspectors.length : '—'} 个</span>
        </div>
        <div className="overflow-x-auto">
          <table className="w-full min-w-[720px] text-left text-sm">
            <thead>
              <tr className="border-b border-slate-100 text-xs text-slate-500">
                <th className="px-4 py-2.5 font-medium md:px-5">显示名</th>
                <th className="px-4 py-2.5 font-medium">用户名</th>
                <th className="px-4 py-2.5 font-medium">角色</th>
                <th className="px-4 py-2.5 font-medium">创建时间</th>
                <th className="px-4 py-2.5 font-medium">操作</th>
              </tr>
            </thead>
            <tbody>
              {loadState === 'loading' ? (
                <tr>
                  <td colSpan={5} className="px-4 py-10 text-center text-sm text-slate-400 md:px-5">
                    正在加载账号…
                  </td>
                </tr>
              ) : loadState === 'error' ? (
                <tr>
                  <td colSpan={5} className="px-4 py-10 text-center text-sm text-slate-400 md:px-5">
                    暂时无法显示账号列表，请稍后重试
                  </td>
                </tr>
              ) : inspectors.length === 0 ? (
                <tr>
                  <td colSpan={5} className="px-4 py-10 text-center text-sm text-slate-400 md:px-5">
                    还没有巡检员账号
                  </td>
                </tr>
              ) : (
                inspectors.map(account => (
                  <InspectorRow
                    key={account.username}
                    account={account}
                    onChanged={message => afterAction(message)}
                    onSessionExpired={onSessionExpired}
                  />
                ))
              )}
            </tbody>
          </table>
        </div>
      </section>

      <section className="rounded-lg border border-slate-200 bg-white shadow-sm">
        <div className="flex items-center justify-between border-b border-slate-200 px-4 py-3 md:px-5">
          <h2 className="text-sm font-semibold text-slate-700">管理员账号</h2>
        </div>
        <div className="overflow-x-auto">
          <table className="w-full min-w-[720px] text-left text-sm">
            <tbody>
              {admins.map(account => (
                <tr key={account.username} className="border-b border-slate-100 last:border-b-0">
                  <td className="px-4 py-3 font-medium text-slate-900 md:px-5">{account.displayName}</td>
                  <td className="px-4 py-3 text-slate-500">{account.username}</td>
                  <td className="px-4 py-3">
                    <span className="inline-flex rounded-full bg-brand-50 px-2 py-0.5 text-xs font-medium text-brand-700">
                      管理员
                    </span>
                  </td>
                  <td className="px-4 py-3 whitespace-nowrap text-slate-500">{formatBeijingTime(account.createdAt)}</td>
                  <td className="px-4 py-3 text-xs text-slate-400">系统账号：由环境变量维护，页面只读</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>
    </div>
  )
}

/** 新增巡检员表单：三个输入 + 提交；提交中禁用，成功后清空并通知上层刷新 */
function CreateInspectorForm({
  disabled,
  onCreated,
  onSessionExpired,
}: {
  disabled: boolean
  onCreated: (displayName: string) => void
  onSessionExpired: () => void
}) {
  const [username, setUsername] = useState('')
  const [displayName, setDisplayName] = useState('')
  const [password, setPassword] = useState('')
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const handleSubmit = useCallback(
    async (event: React.FormEvent) => {
      event.preventDefault()
      if (submitting || disabled) return
      setSubmitting(true)
      setError(null)
      try {
        const created = await createUser({ username: username.trim(), displayName, password })
        setUsername('')
        setDisplayName('')
        setPassword('')
        onCreated(created.displayName)
      } catch (err) {
        if (isRequestCancelled(err)) return
        if (isAuthenticationError(err)) {
          onSessionExpired()
          return
        }
        setError(errorMessage(err))
      } finally {
        setSubmitting(false)
      }
    },
    [username, displayName, password, submitting, disabled, onCreated, onSessionExpired],
  )

  const inputClasses =
    'rounded-md border border-slate-300 px-3 py-2 text-sm text-slate-700 placeholder:text-slate-400 focus:border-brand-500 focus:outline-none disabled:cursor-not-allowed disabled:bg-slate-50'

  return (
    <section className="rounded-lg border border-slate-200 bg-white p-4 shadow-sm md:p-5">
      <h2 className="text-sm font-semibold text-slate-700">新增巡检员</h2>
      <p className="mt-1 text-xs text-slate-500">
        用户名创建后不可修改；初始密码请直接告知巡检员，可在列表中随时重置
      </p>
      {error && (
        <div role="alert" className="mt-3 rounded-md border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700">
          新增失败：{error}
        </div>
      )}
      <form className="mt-3 flex flex-wrap items-end gap-3" onSubmit={handleSubmit}>
        <label className="flex flex-col gap-1">
          <span className="text-xs font-medium text-slate-600">用户名</span>
          <input
            className={`${inputClasses} w-44`}
            placeholder="如 zhangsan"
            value={username}
            onChange={event => setUsername(event.target.value)}
            pattern={USERNAME_PATTERN}
            title="2～32 位英文字母、数字、下划线或连字符"
            disabled={disabled || submitting}
            autoComplete="off"
          />
        </label>
        <label className="flex flex-col gap-1">
          <span className="text-xs font-medium text-slate-600">显示名</span>
          <input
            className={`${inputClasses} w-44`}
            placeholder="如 张三"
            value={displayName}
            onChange={event => setDisplayName(event.target.value)}
            maxLength={32}
            disabled={disabled || submitting}
          />
        </label>
        <label className="flex flex-col gap-1">
          <span className="text-xs font-medium text-slate-600">初始密码</span>
          <input
            className={`${inputClasses} w-52`}
            type="password"
            placeholder="8～72 位"
            value={password}
            onChange={event => setPassword(event.target.value)}
            minLength={8}
            maxLength={72}
            disabled={disabled || submitting}
            autoComplete="new-password"
          />
        </label>
        <button
          type="submit"
          disabled={disabled || submitting}
          className="rounded-md bg-brand-600 px-3.5 py-2 text-sm font-medium text-white hover:bg-brand-700 disabled:cursor-not-allowed disabled:opacity-50"
        >
          {submitting ? '创建中…' : '创建'}
        </button>
      </form>
    </section>
  )
}

/** 行内确认状态：默认 idle；点删除先进入 confirming，再点一次才真正删除 */
type ConfirmState = 'idle' | 'confirming' | 'deleting'

function InspectorRow({
  account,
  onChanged,
  onSessionExpired,
}: {
  account: ManagedAccount
  onChanged: (message: string) => void
  onSessionExpired: () => void
}) {
  const [editing, setEditing] = useState(false)
  const [displayName, setDisplayName] = useState(account.displayName)
  const [password, setPassword] = useState('')
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [confirm, setConfirm] = useState<ConfirmState>('idle')

  const handleStartEdit = () => {
    setEditing(true)
    setDisplayName(account.displayName)
    setPassword('')
    setError(null)
    setConfirm('idle')
  }

  const handleCancelEdit = () => {
    setEditing(false)
    setError(null)
  }

  const handleSave = async () => {
    if (saving) return
    const trimmed = displayName.trim()
    if (trimmed === '' && password === '') {
      setError('请填写新的显示名或新密码')
      return
    }
    setSaving(true)
    setError(null)
    try {
      // 只发送发生变化的字段：显示名未改时不携带，避免覆盖其他管理员的并发修改
      const updated = await updateUser(account.username, {
        ...(trimmed !== '' && trimmed !== account.displayName ? { displayName: trimmed } : {}),
        ...(password !== '' ? { password } : {}),
      })
      setEditing(false)
      setPassword('')
      onChanged(`巡检员「${updated.username}」已更新：显示名「${updated.displayName}」${password !== '' ? '，密码已重置（旧会话已全部失效）' : ''}`)
    } catch (err) {
      if (isRequestCancelled(err)) return
      if (isAuthenticationError(err)) {
        onSessionExpired()
        return
      }
      setError(errorMessage(err))
    } finally {
      setSaving(false)
    }
  }

  const handleDelete = async () => {
    if (confirm === 'idle') {
      setConfirm('confirming')
      return
    }
    if (confirm === 'deleting') return
    setConfirm('deleting')
    setError(null)
    try {
      await deleteUser(account.username)
      onChanged(`巡检员「${account.username}」已删除，其历史问题将保留（归属转为未知，仅管理员可见）`)
    } catch (err) {
      if (isRequestCancelled(err)) return
      if (isAuthenticationError(err)) {
        onSessionExpired()
        return
      }
      setError(errorMessage(err))
      setConfirm('idle')
    }
  }

  const inputClasses =
    'rounded-md border border-slate-300 px-2.5 py-1.5 text-sm text-slate-700 placeholder:text-slate-400 focus:border-brand-500 focus:outline-none disabled:cursor-not-allowed disabled:bg-slate-50'

  return (
    <>
      <tr className="border-b border-slate-100 align-top">
        <td className="px-4 py-3 font-medium text-slate-900 md:px-5">
          {editing ? (
            <input
              className={`${inputClasses} w-36`}
              value={displayName}
              onChange={event => setDisplayName(event.target.value)}
              maxLength={32}
              disabled={saving}
              aria-label={`修改「${account.username}」的显示名`}
            />
          ) : (
            account.displayName
          )}
        </td>
        <td className="px-4 py-3 text-slate-500">{account.username}</td>
        <td className="px-4 py-3">
          <span className="inline-flex rounded-full bg-slate-100 px-2 py-0.5 text-xs font-medium text-slate-600">
            巡检员
          </span>
        </td>
        <td className="px-4 py-3 whitespace-nowrap text-slate-500">{formatBeijingTime(account.createdAt)}</td>
        <td className="px-4 py-3">
          {editing ? (
            <div className="flex flex-wrap items-center gap-2">
              <input
                className={`${inputClasses} w-40`}
                type="password"
                placeholder="新密码（留空表示只改显示名）"
                value={password}
                onChange={event => setPassword(event.target.value)}
                minLength={8}
                maxLength={72}
                disabled={saving}
                autoComplete="new-password"
                aria-label={`重置「${account.username}」的密码`}
              />
              <button
                type="button"
                disabled={saving}
                onClick={() => void handleSave()}
                className="rounded-md bg-brand-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-brand-700 disabled:cursor-not-allowed disabled:opacity-50"
              >
                {saving ? '保存中…' : '保存'}
              </button>
              <button
                type="button"
                disabled={saving}
                onClick={handleCancelEdit}
                className="rounded-md border border-slate-300 px-3 py-1.5 text-sm text-slate-600 hover:bg-slate-50 disabled:cursor-not-allowed disabled:opacity-50"
              >
                取消
              </button>
            </div>
          ) : (
            <div className="flex flex-wrap items-center gap-2">
              <button
                type="button"
                disabled={confirm !== 'idle'}
                onClick={handleStartEdit}
                className="rounded-md border border-slate-300 px-3 py-1.5 text-sm text-slate-600 hover:bg-slate-50 disabled:cursor-not-allowed disabled:opacity-50"
              >
                修改
              </button>
              <button
                type="button"
                disabled={confirm === 'deleting'}
                onClick={() => void handleDelete()}
                className={
                  confirm === 'confirming'
                    ? 'rounded-md bg-red-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-red-700 disabled:cursor-not-allowed disabled:opacity-50'
                    : 'rounded-md border border-red-300 px-3 py-1.5 text-sm text-red-600 hover:bg-red-50 disabled:cursor-not-allowed disabled:opacity-50'
                }
              >
                {confirm === 'deleting' ? '删除中…' : confirm === 'confirming' ? '确认删除' : '删除'}
              </button>
            </div>
          )}
        </td>
      </tr>
      {confirm === 'confirming' && (
        <tr className="border-b border-slate-100 bg-red-50/60">
          <td colSpan={5} className="px-4 py-2.5 text-xs text-red-700 md:px-5">
            确认删除「{account.username}」？其历史问题将保留，归属转为未知，仅管理员可见；同名重建是新账号，不继承历史问题。
          </td>
        </tr>
      )}
      {error && (
        <tr className="border-b border-slate-100">
          <td colSpan={5} className="px-4 py-2.5 md:px-5">
            <div role="alert" className="rounded-md border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700">
              操作失败：{error}
            </div>
          </td>
        </tr>
      )}
    </>
  )
}
