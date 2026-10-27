// 请求层测试：成功、空数组、中文错误、非 JSON 错误、网络失败、超时、外部取消与异常结构。
// 通过替身 fetch 复现故障，不依赖真实后端。

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { API_BASE_URL, REQUEST_TIMEOUT_MS, fetchIssues, isRequestCancelled, updateIssueStatus } from './issues'
import type { Issue } from '../types/issue'

const sampleIssue: Issue = {
  id: 'id-1',
  title: '入口墙面破损',
  description: '左侧墙体存在裂缝',
  priority: 'high',
  status: 'open',
  position: { x: 0.42, y: 0.03, z: 1.26 },
  createdAt: '2026-10-27T10:00:00+08:00',
  updatedAt: '2026-10-27T10:00:00+08:00',
}

/** 构造替身响应；body 为 undefined 时模拟响应体不是 JSON */
function mockResponse(status: number, body?: unknown): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    json: () => (body === undefined ? Promise.reject(new SyntaxError('Unexpected token')) : Promise.resolve(body)),
  } as Response
}

/** 构造挂起的 fetch 替身：只在 signal 中止时以 AbortError 拒绝，模拟真实 fetch 行为 */
function pendingFetchMock() {
  return vi.fn(
    (_url: unknown, init?: RequestInit) =>
      new Promise<Response>((_resolve, reject) => {
        init?.signal?.addEventListener('abort', () => reject(new DOMException('aborted', 'AbortError')))
      }),
  )
}

beforeEach(() => {
  vi.stubGlobal('fetch', vi.fn())
})

afterEach(() => {
  vi.unstubAllGlobals()
  vi.useRealTimers()
})

describe('fetchIssues', () => {
  it('成功返回结构检查后的完整问题数组', async () => {
    const fetchMock = vi.fn().mockResolvedValue(mockResponse(200, { data: [sampleIssue] }))
    vi.stubGlobal('fetch', fetchMock)

    const issues = await fetchIssues()

    expect(issues).toEqual([sampleIssue])
    expect(fetchMock).toHaveBeenCalledExactlyOnceWith(`${API_BASE_URL}/api/issues`, { method: 'GET', signal: expect.any(AbortSignal) })
  })

  it('空数组是合法结果', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(mockResponse(200, { data: [] })))
    await expect(fetchIssues()).resolves.toEqual([])
  })

  it('服务端中文错误透出错误码、信息与状态', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(mockResponse(400, { error: { code: 'invalid_argument', message: '标题不能为空' } })),
    )

    const error = await fetchIssues().catch((e: unknown) => e)

    expect(error).toMatchObject({ code: 'invalid_argument', message: '标题不能为空', status: 400 })
  })

  it('错误响应体不是 JSON 时保留含状态码的默认提示', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(mockResponse(500)))

    const error = await fetchIssues().catch((e: unknown) => e)

    expect(error).toMatchObject({ code: 'unknown_error', status: 500 })
    expect((error as { message: string }).message).toContain('HTTP 500')
  })

  it('网络失败转换为 network_error，不是空列表也不是成功', async () => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('Failed to fetch')))

    const error = await fetchIssues().catch((e: unknown) => e)

    expect(error).toMatchObject({ code: 'network_error', status: 0 })
    expect((error as { message: string }).message).toContain('无法连接后端')
  })

  it('超过 10 秒未响应按超时失败', async () => {
    vi.useFakeTimers()
    const fetchMock = pendingFetchMock()
    vi.stubGlobal('fetch', fetchMock)

    const pending = fetchIssues()
    const assertion = expect(pending).rejects.toMatchObject({ code: 'timeout', status: 0 })
    await vi.advanceTimersByTimeAsync(REQUEST_TIMEOUT_MS)
    await assertion

    expect(fetchMock).toHaveBeenCalledExactlyOnceWith(
      `${API_BASE_URL}/api/issues`,
      { method: 'GET', signal: expect.any(AbortSignal) },
    )
  })

  it('外部 signal 中止时原样抛出取消异常，不算失败', async () => {
    const fetchMock = pendingFetchMock()
    vi.stubGlobal('fetch', fetchMock)
    const controller = new AbortController()

    const pending = fetchIssues(controller.signal)
    controller.abort()

    const error = await pending.catch((e: unknown) => e)
    expect(isRequestCancelled(error)).toBe(true)
  })

  it.each([
    ['data 不是数组', { data: { id: 'id-1' } }],
    ['缺少 data 字段', { error: null }],
    ['成功响应不是 JSON', undefined],
  ])('异常响应（%s）按 bad_response 处理', async (_name, body) => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(mockResponse(200, body)))

    const error = await fetchIssues().catch((e: unknown) => e)

    expect(error).toMatchObject({ code: 'bad_response', status: 0 })
  })

  it.each([
    ['缺少 id', { ...sampleIssue, id: '' }],
    ['title 类型错误', { ...sampleIssue, title: 1 }],
    ['description 缺失', { description: undefined }],
    ['枚举非法', { ...sampleIssue, priority: 'urgent' }],
    ['状态枚举非法', { ...sampleIssue, status: 'done' }],
    ['坐标缺失', { ...sampleIssue, position: { x: 1, y: 2 } }],
    ['坐标非有限数值', { ...sampleIssue, position: { x: Number.NaN, y: 2, z: 3 } }],
    ['createdAt 缺失', { createdAt: undefined }],
  ])('问题结构非法（%s）按 bad_response 处理，不进入渲染', async (_name, broken) => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(mockResponse(200, { data: [broken] })))

    const error = await fetchIssues().catch((e: unknown) => e)

    expect(error).toMatchObject({ code: 'bad_response' })
  })
})

describe('updateIssueStatus', () => {
  it('只发送 status 字段并返回服务端完整问题', async () => {
    const fetchMock = vi.fn().mockResolvedValue(mockResponse(200, { data: { ...sampleIssue, status: 'in_progress' } }))
    vi.stubGlobal('fetch', fetchMock)

    const updated = await updateIssueStatus('id-1', 'in_progress')

    expect(updated).toEqual({ ...sampleIssue, status: 'in_progress' })
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(url).toBe(`${API_BASE_URL}/api/issues/id-1/status`)
    expect(init.method).toBe('PATCH')
    expect(new Headers(init.headers).get('Content-Type')).toBe('application/json')
    expect(JSON.parse(init.body as string)).toEqual({ status: 'in_progress' })
  })

  it('ID 中的特殊字符被编码进路径', async () => {
    const fetchMock = vi.fn().mockResolvedValue(mockResponse(200, { data: sampleIssue }))
    vi.stubGlobal('fetch', fetchMock)

    await updateIssueStatus('a b', 'open')

    expect(fetchMock.mock.calls[0][0]).toBe(`${API_BASE_URL}/api/issues/a%20b/status`)
  })

  it('问题不存在按服务端 404 错误失败，不伪装成功', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(mockResponse(404, { error: { code: 'issue_not_found', message: '指定的问题不存在' } })),
    )

    const error = await updateIssueStatus('missing', 'resolved').catch((e: unknown) => e)

    expect(error).toMatchObject({ code: 'issue_not_found', status: 404 })
  })

  it('保存同样受 10 秒超时约束', async () => {
    vi.useFakeTimers()
    const fetchMock = pendingFetchMock()
    vi.stubGlobal('fetch', fetchMock)

    const pending = updateIssueStatus('id-1', 'resolved')
    const assertion = expect(pending).rejects.toMatchObject({ code: 'timeout' })
    await vi.advanceTimersByTimeAsync(REQUEST_TIMEOUT_MS)
    await assertion
  })
})
