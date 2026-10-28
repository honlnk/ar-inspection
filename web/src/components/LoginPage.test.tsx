// 开发快捷登录测试：开发态展示并一键登录管理员、失败提示中文、生产态隐藏入口。
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { quickLogin } from '../api/auth'
import { LoginPage } from './LoginPage'

vi.mock('../api/auth', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../api/auth')>()
  return {
    ...actual,
    quickLogin: vi.fn<typeof actual.quickLogin>(),
  }
})

const quickLoginMock = vi.mocked(quickLogin)
const admin = { username: 'admin', displayName: '管理员', role: 'admin' as const }

beforeEach(() => {
  quickLoginMock.mockReset()
})

afterEach(() => {
  vi.unstubAllEnvs()
})

describe('开发快捷登录', () => {
  it('开发态展示按钮，一键登录管理员', async () => {
    const user = userEvent.setup()
    const onLoggedIn = vi.fn()
    quickLoginMock.mockResolvedValue(admin)
    render(<LoginPage onLoggedIn={onLoggedIn} />)

    await user.click(screen.getByRole('button', { name: '开发快捷登录（admin）' }))

    expect(quickLoginMock).toHaveBeenCalledExactlyOnceWith('admin', expect.any(AbortSignal))
    expect(onLoggedIn).toHaveBeenCalledWith(admin)
  })

  it('后端未启用时显示中文错误，不进入管理台', async () => {
    const user = userEvent.setup()
    const onLoggedIn = vi.fn()
    quickLoginMock.mockRejectedValue(new Error('快捷登录未启用：请在后端设置 QUICK_LOGIN_ENABLED=1'))
    render(<LoginPage onLoggedIn={onLoggedIn} />)

    await user.click(screen.getByRole('button', { name: '开发快捷登录（admin）' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('QUICK_LOGIN_ENABLED=1')
    expect(onLoggedIn).not.toHaveBeenCalled()
  })

  it('生产态不渲染快捷登录入口', () => {
    vi.stubEnv('DEV', false)
    render(<LoginPage onLoggedIn={vi.fn()} />)

    expect(screen.queryByRole('button', { name: /开发快捷登录/ })).not.toBeInTheDocument()
  })
})
