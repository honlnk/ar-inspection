// 管理台主体测试：首次加载、空列表、失败重试、刷新草稿保留、保存时序与并发互斥，
// 以及实时事件的列表合并、草稿保护与保存期间远端更新互不覆盖。
// 请求层以模块替身注入，实时客户端以假工厂注入，页面行为不依赖真实后端与网络。

import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fetchIssues, updateIssueStatus } from '../api/issues'
import type { Issue } from '../types/issue'
import { IssueBoard } from './IssueBoard'
import type { RealtimeClient, RealtimeClientOptions } from '../lib/realtime'

vi.mock('../api/issues', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../api/issues')>()
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
  const row = screen.getByText(title).closest('tr')
  if (row === null) throw new Error(`未找到标题为「${title}」的行`)
  return {
    row,
    select: within(row).getByRole('combobox'),
    saveButton: within(row).getByRole('button', { name: /保存/ }),
  }
}

/** 记录回调的假实时客户端：测试手动派发状态与事件 */
function createFakeRealtimeFactory() {
  let options: RealtimeClientOptions | null = null
  const fake = {
    start: vi.fn(),
    stop: vi.fn(),
    reconnectNow: vi.fn(),
    emitStatus(status: Parameters<NonNullable<RealtimeClientOptions['onStatus']>>[0]) {
      options?.onStatus(status)
    },
    emitEvent(type: 'issue.created' | 'issue.updated', seq: number, it: Issue) {
      options?.onEvent({ type, seq, issue: it })
    },
  }
  const factory = vi.fn((opts: RealtimeClientOptions): RealtimeClient => {
    options = opts
    return fake as unknown as RealtimeClient
  })
  return { factory, fake }
}

function renderBoard(extraProps: Partial<Parameters<typeof IssueBoard>[0]> = {}) {
  const { factory, fake } = createFakeRealtimeFactory()
  const view = render(
    <IssueBoard
      hidden={false}
      onSessionExpired={vi.fn()}
      createRealtimeClient={factory}
      {...extraProps}
    />,
  )
  return { ...view, fake }
}

beforeEach(() => {
  fetchIssuesMock.mockReset()
  updateIssueStatusMock.mockReset()
})

describe('首次加载', () => {
  it('成功展示标题、描述、优先级、状态与北京时间，计数为实际值', async () => {
    fetchIssuesMock.mockResolvedValue([issue()])
    renderBoard()

    expect(await screen.findByText('入口墙面破损')).toBeInTheDocument()
    expect(screen.getByText('左侧墙体存在裂缝')).toBeInTheDocument()
    expect(screen.getByText('高', { selector: 'span' })).toBeInTheDocument()
    expect(screen.getByText('待处理', { selector: 'span' })).toBeInTheDocument()
    expect(screen.getByText('2026/10/27 10:00:00')).toBeInTheDocument()
    expect(screen.getByText('显示 1 / 共 1 条')).toBeInTheDocument()
    expect(screen.queryByText('正在从后端加载问题…')).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: '刷新' })).toBeEnabled()
  })

  it('空描述显示“无描述”', async () => {
    fetchIssuesMock.mockResolvedValue([issue({ description: '' })])
    renderBoard()

    expect(await screen.findByText('入口墙面破损')).toBeInTheDocument()
    expect(screen.getByText('无描述')).toBeInTheDocument()
  })

  it('空数据库对应的空列表显示“暂无问题”', async () => {
    fetchIssuesMock.mockResolvedValue([])
    renderBoard()

    expect(await screen.findByText('暂无问题')).toBeInTheDocument()
    expect(screen.queryByText(/失败/)).not.toBeInTheDocument()
  })
})

describe('首次失败与重试', () => {
  it('显示错误原因与重试按钮，数量不伪装为 0，重试成功后恢复', async () => {
    const user = userEvent.setup()
    fetchIssuesMock.mockRejectedValueOnce(new Error('无法连接后端：Failed to fetch'))
    renderBoard()

    expect(await screen.findByText(/无法获取问题/)).toBeInTheDocument()
    expect(screen.getByText(/无法连接后端/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: '重试' })).toBeInTheDocument()
    expect(screen.getByText('显示 — 条')).toBeInTheDocument()
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
    renderBoard()
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
    renderBoard()
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

  it('保存失败保留原状态与未保存草稿，可在原行重试且无额外重试按钮', async () => {
    const user = userEvent.setup()
    fetchIssuesMock.mockResolvedValue([issue()])
    updateIssueStatusMock.mockRejectedValue(new Error('服务器错误，请稍后重试'))
    renderBoard()
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
    renderBoard()
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
    renderBoard()
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
    renderBoard()
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
    const { unmount } = renderBoard()
    await screen.findByText('入口墙面破损')

    const pending = deferred<Issue[]>()
    fetchIssuesMock.mockReturnValueOnce(pending.promise)
    await user.click(screen.getByRole('button', { name: '刷新' }))

    unmount()
    pending.resolve([issue({ id: 'late', title: '迟到数据' })])

    await waitFor(() => expect(fetchIssuesMock).toHaveBeenCalledTimes(2))
    expect(updateIssueStatusMock).not.toHaveBeenCalled()
  })
})

describe('实时事件合并（事件序）', () => {
  it('另一客户端创建问题后自动出现，无需手动刷新', async () => {
    fetchIssuesMock.mockResolvedValue([issue()])
    const { fake } = renderBoard()
    await screen.findByText('入口墙面破损')

    const fresh = issue({ id: 'id-2', title: '消防通道堵塞', createdAt: '2026-10-28T09:00:00+08:00' })
    fake.emitEvent('issue.created', 1, fresh)

    expect(await screen.findByText('消防通道堵塞')).toBeInTheDocument()
    expect(screen.getByText('显示 2 / 共 2 条')).toBeInTheDocument()
    expect(fetchIssuesMock).toHaveBeenCalledTimes(1)
  })

  it('远端更新目标行时当前状态更新，未保存草稿保留并提示', async () => {
    const user = userEvent.setup()
    fetchIssuesMock.mockResolvedValue([issue()])
    const { fake } = renderBoard()
    await screen.findByText('入口墙面破损')
    const { select } = rowOf('入口墙面破损')

    // 用户选择 resolved 但未保存，远端把它改成 in_progress
    await user.selectOptions(select, 'resolved')
    fake.emitEvent('issue.updated', 1, issue({ status: 'in_progress' }))

    expect(await screen.findByText('处理中', { selector: 'span' })).toBeInTheDocument()
    expect(screen.getByText('远端已更新，草稿未保存')).toBeInTheDocument()
    // 草稿未被事件清除
    expect(select).toHaveValue('resolved')
  })

  it('保存进行中收到远端更新同一行：事件不取消保存，保存完成后以服务端响应为准', async () => {
    const user = userEvent.setup()
    fetchIssuesMock.mockResolvedValue([issue()])
    const pending = deferred<Issue>()
    updateIssueStatusMock.mockReturnValueOnce(pending.promise)
    const { fake } = renderBoard()
    await screen.findByText('入口墙面破损')
    const { select, saveButton } = rowOf('入口墙面破损')

    await user.selectOptions(select, 'resolved')
    await user.click(saveButton)

    // 保存未完成时远端把该行改为 in_progress：列表行更新，但保存不中断
    fake.emitEvent('issue.updated', 1, issue({ status: 'in_progress' }))
    expect(screen.getByText(/正在保存问题「入口墙面破损」/)).toBeInTheDocument()
    expect(await screen.findByText('处理中', { selector: 'span' })).toBeInTheDocument()

    pending.resolve(issue({ status: 'resolved', updatedAt: '2026-10-27T12:00:00+08:00' }))
    expect(await screen.findByText('已解决', { selector: 'span' })).toBeInTheDocument()
    expect(screen.getByText(/状态已更新为「已解决」/)).toBeInTheDocument()
  })

  it('重复事件被忽略，跳号触发全量校准', async () => {
    fetchIssuesMock.mockResolvedValue([issue()])
    const { fake } = renderBoard()
    await screen.findByText('入口墙面破损')

    const second = issue({ id: 'id-2', title: '跳号校准行', createdAt: '2026-10-28T09:00:00+08:00' })
    fake.emitEvent('issue.created', 1, second)
    await screen.findByText('跳号校准行')

    // 重复 seq=1：忽略，不新增
    fake.emitEvent('issue.created', 1, second)
    expect(screen.getAllByText('跳号校准行')).toHaveLength(1)

    // 跳号 seq=5：触发全量校准（fetchIssues 再次调用）
    fake.emitEvent('issue.updated', 5, issue({ id: 'id-2', title: '跳号校准行', status: 'resolved' }))
    await waitFor(() => expect(fetchIssuesMock).toHaveBeenCalled())
  })

  it('连接成功后自动全量校准（覆盖序号回退）', async () => {
    fetchIssuesMock.mockResolvedValue([issue()])
    const { fake } = renderBoard()
    await screen.findByText('入口墙面破损')
    const callsBefore = fetchIssuesMock.mock.calls.length

    fake.emitStatus('connected')

    await waitFor(() => expect(fetchIssuesMock.mock.calls.length).toBeGreaterThan(callsBefore))
  })
})

describe('筛选', () => {
  it('组合条件筛选、计数与一键清空；筛选期间草稿不丢', async () => {
    const user = userEvent.setup()
    fetchIssuesMock.mockResolvedValue([
      issue({ id: 'a', title: '入口墙面破损', description: '墙体裂缝' }),
      issue({ id: 'b', title: '消防通道堵塞', description: '杂物堆积', priority: 'medium', status: 'resolved' }),
    ])
    renderBoard()
    await screen.findByText('入口墙面破损')
    await screen.findByText('消防通道堵塞')

    // 关键词匹配标题与描述
    const search = screen.getByPlaceholderText('输入关键词')
    await user.type(search, '墙面')
    expect(screen.getByText('入口墙面破损')).toBeInTheDocument()
    expect(screen.queryByText('消防通道堵塞')).not.toBeInTheDocument()
    expect(screen.getByText('显示 1 / 共 2 条')).toBeInTheDocument()

    // 清空后恢复
    await user.click(screen.getByRole('button', { name: '清空筛选' }))
    expect(screen.getByText('消防通道堵塞')).toBeInTheDocument()
    expect(screen.getByText('显示 2 / 共 2 条')).toBeInTheDocument()

    // 优先级 + 状态组合
    await user.selectOptions(screen.getByRole('combobox', { name: '按优先级筛选' }), 'medium')
    await user.selectOptions(screen.getByRole('combobox', { name: '按状态筛选' }), 'resolved')
    expect(screen.queryByText('入口墙面破损')).not.toBeInTheDocument()
    expect(screen.getByText('消防通道堵塞')).toBeInTheDocument()

    // 筛选出某行后仍可编辑草稿
    const { select } = rowOf('消防通道堵塞')
    await user.selectOptions(select, 'in_progress')
    expect(select).toHaveValue('in_progress')
  })

  it('全部数据非空但筛选无结果显示“没有符合当前条件的问题”', async () => {
    fetchIssuesMock.mockResolvedValue([issue()])
    renderBoard()
    await screen.findByText('入口墙面破损')

    await userEvent.setup().type(screen.getByPlaceholderText('输入关键词'), '不存在的关键词')
    expect(await screen.findByText('没有符合当前条件的问题')).toBeInTheDocument()
  })
})
