import { fireEvent, render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { TeamLabRuntime } from '../api'
import { AssetDiagnosticsPanel } from './AssetDiagnosticsPanel'

const { read, refresh } = vi.hoisted(() => ({ read: vi.fn(), refresh: vi.fn() }))
vi.mock('./useAssetDiagnostics', () => ({ useAssetDiagnostics: read }))
const runtime: TeamLabRuntime = {
  id: 'runtime-a', releaseId: 'release-a', generation: 3, status: 'running', stage: 'runtime-ready',
  openForAccess: true, shards: [], networks: [], createdAt: 1788796800000, updatedAt: null, error: null,
  assets: [
    { id: 1, key: 'web', name: 'Web', kind: 'docker', runtimeResourceId: 'container-a',
      primaryIp: null, status: 'running', error: null },
    { id: 2, key: 'db', name: 'Database', kind: 'docker', runtimeResourceId: 'container-b',
      primaryIp: null, status: 'running', error: null },
  ],
}

describe('AssetDiagnosticsPanel', () => {
  beforeEach(() => {
    read.mockReset()
    refresh.mockReset()
    read.mockReturnValue({ data: { state: 'running', paused: false, exitCode: 0, restartCount: 2,
      startedAt: '2026-09-07T01:00:00Z', finishedAt: '0001-01-01T00:00:00Z', logsError: null,
      logs: '中文输出 <script>不是 HTML</script>', truncated: true, observedAt: 1788796800000 },
    error: undefined, isLoading: false, isValidating: false, mutate: refresh })
  })
  it('renders bounded output as text and supports refresh', () => {
    const { container } = render(<AssetDiagnosticsPanel runtime={runtime} />)
    expect(screen.getByLabelText('Web 容器输出').textContent).toContain('<script>不是 HTML</script>')
    expect(container.querySelector('script')).toBeNull()
    expect(screen.getByText(/64 KiB/)).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: '刷新诊断' }))
    expect(refresh).toHaveBeenCalledOnce()
  })
  it('scopes requests to the selected asset and runtime generation', () => {
    render(<AssetDiagnosticsPanel runtime={runtime} />)
    fireEvent.change(screen.getByLabelText('资产'), { target: { value: '2' } })
    fireEvent.change(screen.getByLabelText('最近日志'), { target: { value: '500' } })
    expect(read).toHaveBeenLastCalledWith('runtime-a', 3, 2, 500)
  })
  it('does not show stale output alongside a failed refresh', () => {
    read.mockReturnValue({ data: { logs: 'stale output' }, error: new Error('node unavailable'),
      isLoading: false, isValidating: false, mutate: refresh })
    render(<AssetDiagnosticsPanel runtime={runtime} />)
    expect(screen.queryByText('stale output')).toBeNull()
    expect(screen.getByText('node unavailable')).toBeTruthy()
  })
  it('does not request unsupported VM assets', () => {
    render(<AssetDiagnosticsPanel runtime={{ ...runtime, assets: [{ ...runtime.assets[0], kind: 'vm' }] }} />)
    expect(read).toHaveBeenLastCalledWith('runtime-a', 3, undefined, 200)
    expect(screen.getByText('暂无可诊断的容器资产')).toBeTruthy()
  })
  it('keeps actual state when logs are unavailable without claiming empty output', () => {
    const result = read.getMockImplementation()!()
    read.mockReturnValue({ ...result, data: { ...result.data, logs: '', truncated: false,
      logsError: 'diagnostics.logs_unavailable' } })
    render(<AssetDiagnosticsPanel runtime={runtime} />)
    expect(screen.getByText('运行中')).toBeTruthy()
    expect(screen.getByText('容器日志读取失败，请检查节点日志驱动后重试。')).toBeTruthy()
    expect(screen.queryByText('容器暂无标准输出或错误输出')).toBeNull()
    expect(screen.getByText('退出时间').nextElementSibling?.textContent).toBe('-')
  })
})
