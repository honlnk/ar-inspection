// 人员管理页测试（计划 008 阶段 2）：列表渲染、新增、修改、删除确认、
// 错误与空态、会话过期回登录。请求层以模块替身注入，不依赖真实后端。

import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createUser, deleteUser, fetchUsers, updateUser } from '../api/users'
import type { ManagedAccount } from '../api/users'
import { ApiError } from '../api/issues'
import { PersonnelBoard } from './PersonnelBoard'

vi.mock('../api/users', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../api/users')>()
  return {
    ...actual,
    fetchUsers: vi.fn<typeof actual.fetchUsers>(),
    createUser: vi.fn<typeof actual.createUser>(),
    updateUser: vi.fn<typeof actual.updateUser>(),
    deleteUser: vi.fn<typeof actual.deleteUser>(),
  }
})

const fetchUsersMock = vi.mocked(fetchUsers)
const createUserMock = vi.mocked(createUser)
const updateUserMock = vi.mocked(updateUser)
const deleteUserMock = vi.mocked(deleteUser)

const inspectorAccount: ManagedAccount = {
  username: 'zhangsan',
  displayName: '张三',
  role: 'inspector',
  createdAt: '2026-10-28T09:00:00+08:00',
  updatedAt: '2026-10-28T09:00:00+08:00',
}
const adminAccount: ManagedAccount = {
  username: 'admin',
  displayName: '管理员',
  role: 'admin',
  createdAt: '2026-10-28T09:00:00+08:00',
  updatedAt: '2026-10-28T09:00:00+08:00',
}

function renderBoard(onSessionExpired = vi.fn()) {
  return render(<PersonnelBoard onSessionExpired={onSessionExpired} />)
}

beforeEach(() => {
  fetchUsersMock.mockReset()
  createUserMock.mockReset()
  updateUserMock.mockReset()
  deleteUserMock.mockReset()
})

describe('列表渲染', () => {
  it('展示巡检员与只读管理员两个分区；管理员行不出现修改/删除按钮', async () => {
    fetchUsersMock.mockResolvedValue([adminAccount, inspectorAccount])
    renderBoard()

    expect(await screen.findByText('张三')).toBeInTheDocument()
    expect(screen.getByText('zhangsan')).toBeInTheDocument()
    // 「管理员」同时出现在显示名与角色徽标，按角色徽标断言
    expect(screen.getByText('管理员', { selector: 'span' })).toBeInTheDocument()
    expect(screen.getByText('系统账号：由环境变量维护，页面只读')).toBeInTheDocument()
    // 管理员账号无操作按钮；巡检员行有修改/删除
    const inspectorRow = screen.getByText('zhangsan').closest('tr') as HTMLTableRowElement
    expect(within(inspectorRow).getByRole('button', { name: '修改' })).toBeInTheDocument()
    expect(within(inspectorRow).getByRole('button', { name: '删除' })).toBeInTheDocument()
    const adminRow = screen.getByText('系统账号：由环境变量维护，页面只读').closest('tr') as HTMLTableRowElement
    expect(within(adminRow).queryByRole('button')).not.toBeInTheDocument()
  })

  it('空列表显示「还没有巡检员账号」', async () => {
    fetchUsersMock.mockResolvedValue([adminAccount])
    renderBoard()

    expect(await screen.findByText('还没有巡检员账号')).toBeInTheDocument()
  })

  it('加载失败显示中文错误与重试；重试成功恢复列表', async () => {
    const user = userEvent.setup()
    fetchUsersMock.mockRejectedValueOnce(new Error('无法连接后端：refused'))
    renderBoard()

    expect(await screen.findByText(/无法获取账号列表：无法连接后端：refused/)).toBeInTheDocument()
    fetchUsersMock.mockResolvedValueOnce([adminAccount, inspectorAccount])
    await user.click(screen.getByRole('button', { name: '重试' }))
    expect(await screen.findByText('张三')).toBeInTheDocument()
  })
})

describe('新增巡检员', () => {
  it('提交成功后清空表单、提示并刷新列表', async () => {
    const user = userEvent.setup()
    fetchUsersMock.mockResolvedValueOnce([adminAccount])
    renderBoard()
    expect(await screen.findByText('系统账号：由环境变量维护，页面只读')).toBeInTheDocument()

    createUserMock.mockResolvedValueOnce(inspectorAccount)
    fetchUsersMock.mockResolvedValueOnce([adminAccount, inspectorAccount])
    await user.type(screen.getByPlaceholderText('如 zhangsan'), 'zhangsan')
    await user.type(screen.getByPlaceholderText('如 张三'), '张三')
    await user.type(screen.getByPlaceholderText('8～72 位'), 'zhangsan-pw-1')
    await user.click(screen.getByRole('button', { name: '创建' }))

    expect(createUserMock).toHaveBeenCalledWith({ username: 'zhangsan', displayName: '张三', password: 'zhangsan-pw-1' })
    expect(await screen.findByText(/巡检员「张三」已创建，立即可登录/)).toBeInTheDocument()
    expect(await screen.findByText('zhangsan')).toBeInTheDocument()
    expect(screen.getByPlaceholderText('如 zhangsan')).toHaveValue('')
  })

  it('后端参数错误以中文展示，不清空表单', async () => {
    const user = userEvent.setup()
    fetchUsersMock.mockResolvedValue([adminAccount])
    renderBoard()
    expect(await screen.findByText('系统账号：由环境变量维护，页面只读')).toBeInTheDocument()

    createUserMock.mockRejectedValueOnce(new Error('用户名已存在'))
    await user.type(screen.getByPlaceholderText('如 zhangsan'), 'admin')
    await user.type(screen.getByPlaceholderText('如 张三'), '甲')
    await user.type(screen.getByPlaceholderText('8～72 位'), '12345678')
    await user.click(screen.getByRole('button', { name: '创建' }))

    expect(await screen.findByText(/新增失败：用户名已存在/)).toBeInTheDocument()
    expect(screen.getByPlaceholderText('如 zhangsan')).toHaveValue('admin')
  })
})

describe('修改巡检员', () => {
  it('只改显示名：保存成功提示并刷新', async () => {
    const user = userEvent.setup()
    fetchUsersMock.mockResolvedValue([adminAccount, inspectorAccount])
    renderBoard()
    expect(await screen.findByText('zhangsan')).toBeInTheDocument()

    updateUserMock.mockResolvedValueOnce({ ...inspectorAccount, displayName: '张三丰' })
    fetchUsersMock.mockResolvedValueOnce([adminAccount, { ...inspectorAccount, displayName: '张三丰' }])
    await user.click(screen.getByRole('button', { name: '修改' }))
    const nameInput = screen.getByLabelText('修改「zhangsan」的显示名')
    await user.clear(nameInput)
    await user.type(nameInput, '张三丰')
    await user.click(screen.getByRole('button', { name: '保存' }))

    expect(updateUserMock).toHaveBeenCalledWith('zhangsan', { displayName: '张三丰' })
    expect(await screen.findByText(/巡检员「zhangsan」已更新：显示名「张三丰」/)).toBeInTheDocument()
  })

  it('重置密码提示旧会话失效', async () => {
    const user = userEvent.setup()
    fetchUsersMock.mockResolvedValue([adminAccount, inspectorAccount])
    renderBoard()
    expect(await screen.findByText('zhangsan')).toBeInTheDocument()

    updateUserMock.mockResolvedValueOnce(inspectorAccount)
    fetchUsersMock.mockResolvedValueOnce([adminAccount, inspectorAccount])
    await user.click(screen.getByRole('button', { name: '修改' }))
    await user.type(screen.getByLabelText('重置「zhangsan」的密码'), 'new-pass-123')
    await user.click(screen.getByRole('button', { name: '保存' }))

    expect(updateUserMock).toHaveBeenCalledWith('zhangsan', { password: 'new-pass-123' })
    expect(await screen.findByText(/密码已重置（旧会话已全部失效）/)).toBeInTheDocument()
  })

  it('显示名与密码都为空时本地拦截，不发请求', async () => {
    const user = userEvent.setup()
    fetchUsersMock.mockResolvedValue([adminAccount, inspectorAccount])
    renderBoard()
    expect(await screen.findByText('zhangsan')).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: '修改' }))
    const nameInput = screen.getByLabelText('修改「zhangsan」的显示名')
    await user.clear(nameInput)
    await user.click(screen.getByRole('button', { name: '保存' }))

    expect(await screen.findByText('操作失败：请填写新的显示名或新密码')).toBeInTheDocument()
    expect(updateUserMock).not.toHaveBeenCalled()
  })
})

describe('删除巡检员', () => {
  it('首次点击进入确认文案，再点才删除；成功后提示后果与刷新', async () => {
    const user = userEvent.setup()
    fetchUsersMock.mockResolvedValue([adminAccount, inspectorAccount])
    renderBoard()
    expect(await screen.findByText('zhangsan')).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: '删除' }))
    expect(
      screen.getByText(/确认删除「zhangsan」？其历史问题将保留，归属转为未知，仅管理员可见/),
    ).toBeInTheDocument()
    expect(deleteUserMock).not.toHaveBeenCalled()

    deleteUserMock.mockResolvedValueOnce(undefined)
    fetchUsersMock.mockResolvedValueOnce([adminAccount])
    await user.click(screen.getByRole('button', { name: '确认删除' }))

    expect(deleteUserMock).toHaveBeenCalledWith('zhangsan')
    expect(await screen.findByText(/巡检员「zhangsan」已删除/)).toBeInTheDocument()
    await waitFor(() => expect(screen.getByText('还没有巡检员账号')).toBeInTheDocument())
  })

  it('删除失败回到初始态并显示错误', async () => {
    const user = userEvent.setup()
    fetchUsersMock.mockResolvedValue([adminAccount, inspectorAccount])
    renderBoard()
    expect(await screen.findByText('zhangsan')).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: '删除' }))
    deleteUserMock.mockRejectedValueOnce(new Error('删除失败，请稍后重试'))
    await user.click(screen.getByRole('button', { name: '确认删除' }))

    expect(await screen.findByText(/操作失败：删除失败，请稍后重试/)).toBeInTheDocument()
    expect(await screen.findByRole('button', { name: '删除' })).toBeInTheDocument()
  })
})

describe('会话过期', () => {
  it('任意操作收到 401 类错误时通知 App 回登录页', async () => {
    const onSessionExpired = vi.fn()
    // isAuthenticationError 按 ApiError + status===401 判定，需用真实 ApiError
    fetchUsersMock.mockRejectedValueOnce(new ApiError('authentication_required', '请先登录', 401))
    renderBoard(onSessionExpired)

    await waitFor(() => expect(onSessionExpired).toHaveBeenCalledTimes(1))
    // 未渲染登录相关内容（页面本身不处理会话，交给 App）
    expect(screen.queryByRole('button', { name: '登录' })).not.toBeInTheDocument()
  })
})
