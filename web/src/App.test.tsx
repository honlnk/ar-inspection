// App 会话门控测试：检查中、会话恢复、未登录进登录页、巡检员角色拦截、退出，
// 以及计划 008 的双入口导航切换（问题工作台保持挂载，状态不丢）。

import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fetchCurrentUser, login, logout } from './api/auth'
import { fetchIssues } from './api/issues'
import { fetchUsers } from './api/users'
import App from './App'

vi.mock('./api/auth', async (importOriginal) => {
  const actual = await importOriginal<typeof import('./api/auth')>()
  return {
    ...actual,
    fetchCurrentUser: vi.fn<typeof actual.fetchCurrentUser>(),
    login: vi.fn<typeof actual.login>(),
    logout: vi.fn<typeof actual.logout>(),
  }
})

vi.mock('./api/issues', async (importOriginal) => {
  const actual = await importOriginal<typeof import('./api/issues')>()
  return {
    ...actual,
    fetchIssues: vi.fn<typeof actual.fetchIssues>(),
  }
})

vi.mock('./api/users', async (importOriginal) => {
  const actual = await importOriginal<typeof import('./api/users')>()
  return {
    ...actual,
    fetchUsers: vi.fn<typeof actual.fetchUsers>(),
  }
})

const fetchCurrentUserMock = vi.mocked(fetchCurrentUser)
const loginMock = vi.mocked(login)
const logoutMock = vi.mocked(logout)
const fetchIssuesMock = vi.mocked(fetchIssues)
const fetchUsersMock = vi.mocked(fetchUsers)

const admin = { username: 'admin', displayName: '管理员', role: 'admin' as const }
const inspector = { username: 'inspector', displayName: '巡检员', role: 'inspector' as const }

const accounts = [
  { username: 'admin', displayName: '管理员', role: 'admin' as const, createdAt: '2026-10-28T09:00:00+08:00', updatedAt: '2026-10-28T09:00:00+08:00' },
  { username: 'inspector', displayName: '巡检员', role: 'inspector' as const, createdAt: '2026-10-28T09:00:00+08:00', updatedAt: '2026-10-28T09:00:00+08:00' },
]

beforeEach(() => {
  fetchCurrentUserMock.mockReset()
  loginMock.mockReset()
  logoutMock.mockReset()
  fetchIssuesMock.mockReset()
  fetchUsersMock.mockReset()
  // IssueBoard 的实时客户端走真实 WebSocket 构造器；jsdom 下连接会失败进入退避，
  // 但退避定时器不会打断断言。列表为空以避免依赖。
  fetchIssuesMock.mockResolvedValue([])
  fetchUsersMock.mockResolvedValue(accounts)
})

describe('会话门控', () => {
  it('检查完成后未登录显示登录页', async () => {
    fetchCurrentUserMock.mockRejectedValue(new Error('未认证'))
    render(<App />)

    expect(await screen.findByText('登录')).toBeInTheDocument()
    expect(screen.getByPlaceholderText('admin')).toBeInTheDocument()
  })

  it('有效管理员会话直接进入管理台', async () => {
    fetchCurrentUserMock.mockResolvedValue(admin)
    render(<App />)

    await waitFor(() => expect(fetchCurrentUserMock).toHaveBeenCalledTimes(1))
    // 进入管理台：出现退出按钮与实时状态
    expect(await screen.findByRole('button', { name: /退出登录/ })).toBeInTheDocument()
  })

  it('巡检员账号在网页端提示角色不适用，停留在登录页', async () => {
    fetchCurrentUserMock.mockResolvedValue(inspector)
    render(<App />)

    expect(await screen.findByText(/巡检员账号请使用 Unity 巡检端/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: '登录' })).toBeInTheDocument()
  })

  it('登录成功进入管理台；巡检员登录被拦截', async () => {
    const user = userEvent.setup()
    fetchCurrentUserMock.mockRejectedValue(new Error('未认证'))
    render(<App />)
    expect(await screen.findByRole('button', { name: '登录' }))

    // 巡检员通过登录接口登录成功，但网页端只允许管理员
    loginMock.mockResolvedValueOnce(inspector)
    await user.type(screen.getByPlaceholderText('admin'), 'inspector')
    await user.type(screen.getByPlaceholderText('请输入密码'), 'whatever')
    await user.click(screen.getByRole('button', { name: '登录' }))

    expect(await screen.findByText(/巡检员账号请使用 Unity 巡检端/)).toBeInTheDocument()

    // 管理员登录成功进入管理台
    loginMock.mockResolvedValueOnce(admin)
    await user.clear(screen.getByPlaceholderText('admin'))
    await user.type(screen.getByPlaceholderText('admin'), 'admin')
    await user.click(screen.getByRole('button', { name: '登录' }))
    expect(await screen.findByRole('button', { name: /退出登录/ })).toBeInTheDocument()
  })

  it('登录失败显示后端中文错误，不白屏', async () => {
    const user = userEvent.setup()
    fetchCurrentUserMock.mockRejectedValue(new Error('未认证'))
    loginMock.mockRejectedValueOnce(new Error('用户名或密码错误'))
    render(<App />)
    expect(await screen.findByRole('button', { name: '登录' }))

    await user.type(screen.getByPlaceholderText('admin'), 'admin')
    await user.type(screen.getByPlaceholderText('请输入密码'), 'bad')
    await user.click(screen.getByRole('button', { name: '登录' }))

    expect(await screen.findByText('用户名或密码错误')).toBeInTheDocument()
  })

  it('退出后回到登录页', async () => {
    const user = userEvent.setup()
    fetchCurrentUserMock.mockResolvedValue(admin)
    render(<App />)
    const logoutButton = await screen.findByRole('button', { name: /退出登录/ })

    logoutMock.mockResolvedValueOnce(undefined)
    await user.click(logoutButton)

    await waitFor(() => expect(logoutMock).toHaveBeenCalledTimes(1))
    expect(await screen.findByRole('button', { name: '登录' })).toBeInTheDocument()
  })
})

describe('双入口导航（计划 008）', () => {
  // 桌面侧栏与窄屏顶部导航同时渲染（CSS 按宽度显隐），按钮取第一个即可
  function navButton(name: string): HTMLElement {
    return screen.getAllByRole('button', { name })[0]
  }

  it('侧栏提供「问题列表 / 人员管理」两个入口；切换到人员管理页显示账号列表', async () => {
    const user = userEvent.setup()
    fetchCurrentUserMock.mockResolvedValue(admin)
    render(<App />)
    expect(await screen.findByRole('button', { name: /退出登录/ })).toBeInTheDocument()

    await user.click(navButton('人员管理'))
    expect(await screen.findByText('新增巡检员')).toBeInTheDocument()
    expect(screen.getByText('inspector')).toBeInTheDocument()
    expect(screen.getByText('系统账号：由环境变量维护，页面只读')).toBeInTheDocument()
  })

  it('切到人员管理再切回：问题工作台保持挂载，不重新请求列表', async () => {
    const user = userEvent.setup()
    fetchCurrentUserMock.mockResolvedValue(admin)
    fetchIssuesMock.mockResolvedValue([])
    render(<App />)
    expect(await screen.findByRole('button', { name: /退出登录/ })).toBeInTheDocument()
    await waitFor(() => expect(fetchIssuesMock).toHaveBeenCalledTimes(1))

    await user.click(navButton('人员管理'))
    expect(await screen.findByText('新增巡检员')).toBeInTheDocument()

    await user.click(navButton('问题列表'))
    expect(await screen.findByText('暂无问题')).toBeInTheDocument()
    // 工作台未卸载重建：列表请求仍只有首次那一次
    expect(fetchIssuesMock).toHaveBeenCalledTimes(1)
  })
})
