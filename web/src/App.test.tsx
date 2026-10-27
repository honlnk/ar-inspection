// 页面组件测试：首次加载、空列表、失败重试、刷新草稿保留、保存时序与并发互斥。
// 请求层以模块替身注入，页面行为不依赖真实后端。

import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fetchIssues, updateIssueStatus } from './api/issues'
import type { Issue } from './types/issue'
import App from './App'

vi.mock('./api/issues', async (importOriginal) => {
  const actual = await importOriginal<typeof import('./api/issues')>()
  return {
    ...actual,
    fetchIssues: vi.fn<typeof actual.fetchIssues>(),
    updateIssueStatus: vi.fn<typeof actual.updateIssueStatus>(),
  }
})

const fetchIssuesMock = vi.mocked(fetchIssues)
const updateIssueStatusMock = vi.mocked(updateIssueStatus)

const baseIssue: Issue = {
  id: 'id-1',
  title: '入口墙面破损',
  description: '左侧墙体存在裂缝',
  priority: 'high',
  status: 'open',
  position: { x: 0.42, y: 0.03, z: 1.26 },
  createdAt: '2026-10-27T10:00:00+08:00',
  updatedAt: '2026-10-27T10:00:00+08:00',
}

function issue(over: Partial<Issue> = {}): Issue {
  return { ...baseIssue, ...over }
}

function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((res) => {
    resolve = res
  })
  return { promise, resolve }
}

function rowOf(title: string) {
  // 行角色不从内容命名，以标题文本定位其所在行
  const row = screen.getByText(title).closest('tr')
  if (row === null) throw new Error(`未找到标题为「${title}」的行`)
  return {
    row,
    select: within(row).getByRole('combobox'),
    saveButton: within(row).getByRole('button', { name: /保存/ }),
  }
}

beforeEach(() => {
  fetchIssuesMock.mockReset()
  updateIssueStatusMock.mockReset()
})

describe('首次加载', () => {
  it('成功展示标题、描述、优先级、状态与北京时间，数量为实际值', async () => {
    fetchIssuesMock.mockResolvedValue([issue()])
    render(<App />)

    expect(await screen.findByText('入口墙面破损')).toBeInTheDocument()
    expect(screen.getByText('左侧墙体存在裂缝')).toBeInTheDocument()
    expect(screen.getByText('高')).toBeInTheDocument()
    expect(screen.getByText('待处理', { selector: 'span' })).toBeInTheDocument()
    expect(screen.getByText('2026/10/27 10:00:00')).toBeInTheDocument()
    expect(screen.getByText('共 1 条')).toBeInTheDocument()
    expect(screen.queryByText('正在从后端加载问题…')).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: '刷新' })).toBeEnabled()
  })

  it('空描述显示“无描述”', async () => {
    fetchIssuesMock.mockResolvedValue([issue({ description: '' })])
    render(<App />)

    expect(await screen.findByText('入口墙面破损')).toBeInTheDocument()
    expect(screen.getByText('无描述')).toBeInTheDocument()
  })

  it('空数据库对应的空列表显示“暂无问题”与 0 条', async () => {
    fetchIssuesMock.mockResolvedValue([])
    render(<App />)

    expect(await screen.findByText('暂无问题')).toBeInTheDocument()
    expect(screen.getByText('共 0 条')).toBeInTheDocument()
    expect(screen.queryByText(/失败/)).not.toBeInTheDocument()
  })
})

describe('首次失败与重试', () => {
  it('显示错误原因与重试按钮，数量不伪装为 0，重试成功后恢复', async () => {
    const user = userEvent.setup()
    fetchIssuesMock.mockRejectedValueOnce(new Error('无法连接后端：Failed to fetch'))
    render(<App />)

    expect(await screen.findByText(/无法获取问题/)).toBeInTheDocument()
    expect(screen.getByText(/无法连接后端/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: '重试' })).toBeInTheDocument()
    expect(screen.getByText('共 — 条')).toBeInTheDocument()
    expect(screen.getByText('暂时无法显示问题列表，请稍后重试')).toBeInTheDocument()

    fetchIssuesMock.mockResolvedValue([issue()])
    await user.click(screen.getByRole('button', { name: '重试' }))

    expect(await screen.findByText('入口墙面破损')).toBeInTheDocument()
    expect(screen.queryByText(/无法获取问题/)).not.toBeInTheDocument()
  })
})

describe('状态编辑与保存', () => {
  it('只切换下拉框不发送请求，保存按钮按是否变化启用', async () => {
    const user = userEvent.setup()
    fetchIssuesMock.mockResolvedValue([issue()])
    render(<App />)
    await screen.findByText('入口墙面破损')
    const { select, saveButton } = rowOf('入口墙面破损')
    expect(saveButton).toBeDisabled()

    await user.selectOptions(select, 'in_progress')

    expect(updateIssueStatusMock).not.toHaveBeenCalled()
    expect(fetchIssuesMock).toHaveBeenCalledTimes(1)
    expect(saveButton).toBeEnabled()
  })

  it('保存以行问题 ID 与草稿状态调用接口，成功用服务端完整结果替换该行', async () => {
    const user = userEvent.setup()
    fetchIssuesMock.mockResolvedValue([issue()])
    updateIssueStatusMock.mockResolvedValue(issue({ status: 'resolved', updatedAt: '2026-10-27T11:30:00+08:00' }))
    render(<App />)
    await screen.findByText('入口墙面破损')
    const { select, saveButton } = rowOf('入口墙面破损')

    await user.selectOptions(select, 'resolved')
    await user.click(saveButton)

    await waitFor(() =>
      expect(updateIssueStatusMock).toHaveBeenCalledWith('id-1', 'resolved', expect.any(AbortSignal)),
    )
    expect(await screen.findByText('已解决', { selector: 'span' })).toBeInTheDocument()
    expect(screen.getByText('2026/10/27 11:30:00')).toBeInTheDocument()
    expect(screen.getByText(/问题「入口墙面破损」状态已更新为「已解决」/)).toBeInTheDocument()
    expect(select).toHaveValue('resolved')
    expect(saveButton).toBeDisabled()
  })

  it('三种状态均可修改保存，页面跟随服务端结果', async () => {
    const user = userEvent.setup()
    fetchIssuesMock.mockResolvedValue([issue()])
    render(<App />)
    await screen.findByText('入口墙面破损')
    const { select, saveButton } = rowOf('入口墙面破损')

    let saved = 0
    for (const [status, label] of [
      ['in_progress', '处理中'],
      ['resolved', '已解决'],
      ['open', '待处理'],
    ] as const) {
      updateIssueStatusMock.mockResolvedValue(issue({ status, updatedAt: '2026-10-27T12:00:00+08:00' }))
      await user.selectOptions(select, status)
      await user.click(saveButton)
      saved += 1
      expect(await screen.findByText(label, { selector: 'span' })).toBeInTheDocument()
      await waitFor(() => expect(updateIssueStatusMock).toHaveBeenCalledTimes(saved))
    }
  })

  it('保存失败保留原状态与未保存草稿，可在原行重试且无额外重试按钮', async () => {
    const user = userEvent.setup()
    fetchIssuesMock.mockResolvedValue([issue()])
    updateIssueStatusMock.mockRejectedValue(new Error('服务器错误，请稍后重试'))
    render(<App />)
    await screen.findByText('入口墙面破损')
    const { select, saveButton } = rowOf('入口墙面破损')

    await user.selectOptions(select, 'in_progress')
    await user.click(saveButton)

    expect(await screen.findByText(/状态保存失败/)).toBeInTheDocument()
    expect(screen.getByText(/服务器错误/)).toBeInTheDocument()
    expect(screen.getByText('待处理', { selector: 'span' })).toBeInTheDocument()
    expect(select).toHaveValue('in_progress')
    expect(saveButton).toBeEnabled()
    expect(screen.queryByRole('button', { name: '重试' })).not.toBeInTheDocument()

    // 原行再次点击保存即重试
    updateIssueStatusMock.mockResolvedValue(issue({ status: 'in_progress' }))
    await user.click(saveButton)
    await waitFor(() => expect(updateIssueStatusMock).toHaveBeenCalledTimes(2))
    expect(await screen.findByText(/状态已更新为「处理中」/)).toBeInTheDocument()
  })
})

describe('并发与时序', () => {
  it('保存进行中禁用全部下拉框、保存按钮与刷新按钮，连续点击不重复发送', async () => {
    const user = userEvent.setup()
    fetchIssuesMock.mockResolvedValue([issue({ id: 'a', title: '入口墙面破损' }), issue({ id: 'b', title: '消防通道堵塞' })])
    const pending = deferred<Issue>()
    updateIssueStatusMock.mockReturnValueOnce(pending.promise)
    render(<App />)
    await screen.findByText('入口墙面破损')
    await screen.findByText('消防通道堵塞')
    const rowA = rowOf('入口墙面破损')
    const rowB = rowOf('消防通道堵塞')

    await user.selectOptions(rowA.select, 'resolved')
    await user.click(rowA.saveButton)

    expect(screen.getByText(/正在保存问题「入口墙面破损」的处理状态/)).toBeInTheDocument()
    expect(rowA.select).toBeDisabled()
    expect(rowB.select).toBeDisabled()
    expect(rowA.saveButton).toBeDisabled()
    expect(rowB.saveButton).toBeDisabled()
    expect(screen.getByRole('button', { name: /刷新/ })).toBeDisabled()

    await user.click(rowA.saveButton)
    expect(updateIssueStatusMock).toHaveBeenCalledTimes(1)

    pending.resolve(issue({ status: 'resolved' }))
    await waitFor(() => expect(rowB.select).toBeEnabled())
    expect(screen.getByText(/状态已更新为「已解决」/)).toBeInTheDocument()
  })

  it('刷新进行中禁用状态编辑与保存，完成后草稿保留', async () => {
    const user = userEvent.setup()
    fetchIssuesMock.mockResolvedValueOnce([issue()])
    render(<App />)
    await screen.findByText('入口墙面破损')
    const { select, saveButton } = rowOf('入口墙面破损')

    await user.selectOptions(select, 'in_progress')
    const pending = deferred<Issue[]>()
    fetchIssuesMock.mockReturnValueOnce(pending.promise)
    await user.click(screen.getByRole('button', { name: '刷新' }))

    expect(screen.getByRole('button', { name: /刷新中/ })).toBeDisabled()
    expect(screen.getByText(/正在刷新/)).toBeInTheDocument()
    expect(select).toBeDisabled()
    expect(saveButton).toBeDisabled()

    pending.resolve([issue()])
    await waitFor(() => expect(select).toBeEnabled())
    expect(select).toHaveValue('in_progress')
    expect(saveButton).toBeEnabled()
    expect(fetchIssuesMock).toHaveBeenCalledTimes(2)
  })

  it('刷新失败保留旧列表并提示可能不是最新数据，可重试', async () => {
    const user = userEvent.setup()
    fetchIssuesMock.mockResolvedValueOnce([issue()])
    render(<App />)
    await screen.findByText('入口墙面破损')
    fetchIssuesMock.mockRejectedValueOnce(new Error('请求超时（10 秒），请检查网络后重试'))

    await user.click(screen.getByRole('button', { name: '刷新' }))

    expect(await screen.findByText(/刷新失败，当前数据可能不是最新数据/)).toBeInTheDocument()
    expect(screen.getByText(/请求超时/)).toBeInTheDocument()
    expect(screen.getByText('入口墙面破损')).toBeInTheDocument()

    fetchIssuesMock.mockResolvedValueOnce([issue()])
    await user.click(screen.getByRole('button', { name: '重试' }))
    await waitFor(() => expect(fetchIssuesMock).toHaveBeenCalledTimes(3))
    expect(screen.queryByText(/刷新失败/)).not.toBeInTheDocument()
  })

  it('组件卸载后中止请求，迟到结果不会覆盖状态', async () => {
    const user = userEvent.setup()
    fetchIssuesMock.mockResolvedValueOnce([issue()])
    const { unmount } = render(<App />)
    await screen.findByText('入口墙面破损')

    const pending = deferred<Issue[]>()
    fetchIssuesMock.mockReturnValueOnce(pending.promise)
    await user.click(screen.getByRole('button', { name: '刷新' }))

    // 卸载触发中止；此后迟到的响应仅被丢弃，不抛异常
    unmount()
    pending.resolve([issue({ id: 'late', title: '迟到数据' })])

    await waitFor(() => expect(fetchIssuesMock).toHaveBeenCalledTimes(2))
    expect(updateIssueStatusMock).not.toHaveBeenCalled()
  })
})
