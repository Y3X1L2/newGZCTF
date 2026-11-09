import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { SWRConfig } from 'swr'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { NodeTelemetryPanel } from './NodeTelemetryPanel'
import { nodeMetricHistoryApi } from '../api/nodeMetricHistoryApi'

vi.mock('../api/nodeMetricHistoryApi', () => ({ nodeMetricHistoryApi: { history: vi.fn() } }))
vi.mock('./NodeHistoryChart', () => ({ NodeHistoryChart: ({ history }: { history: { points: unknown[] } }) =>
  <div data-testid="historical-chart">历史 {history.points.length} 个时间点</div> }))
const history = { fromUtc: 60_000, toUtc: 180_000, intervalSeconds: 60, points: [
  { timestamp: 60_000, cpuLoad: 0.2, memoryLoad: 0.5, nodeCount: 1, sampleCount: 6 },
  { timestamp: 120_000, cpuLoad: null, memoryLoad: null, nodeCount: 0, sampleCount: 0 },
] }
const mount = (nodeId?: string) => render(<SWRConfig value={{ provider: () => new Map(), shouldRetryOnError: false }}>
  <NodeTelemetryPanel nodes={[]} nodeId={nodeId} />
</SWRConfig>)

describe('persisted load history', () => {
  beforeEach(() => { vi.mocked(nodeMetricHistoryApi.history).mockReset().mockResolvedValue(history) })
  it('loads history immediately even without a live heartbeat and after remounting', async () => {
    const first = mount()
    expect(await screen.findByTestId('historical-chart')).toHaveTextContent('历史 2 个时间点')
    first.unmount()
    mount()
    expect(await screen.findByTestId('historical-chart')).toBeInTheDocument()
    expect(nodeMetricHistoryApi.history).toHaveBeenCalledTimes(2)
    expect(screen.queryByText(/本次打开|重新采样/)).not.toBeInTheDocument()
  })
  it('queries each selected range and keeps the selected node scope', async () => {
    mount('node-1')
    await screen.findByTestId('historical-chart')
    fireEvent.click(screen.getByRole('button', { name: '近 7 天' }))
    await waitFor(() => expect(nodeMetricHistoryApi.history).toHaveBeenLastCalledWith('7d', 'node-1'))
    expect(screen.getByRole('button', { name: '近 7 天' })).toHaveAttribute('aria-pressed', 'true')
  })
  it('shows a retryable failure and does not replace missing history with live samples', async () => {
    vi.mocked(nodeMetricHistoryApi.history).mockRejectedValueOnce(new Error('unavailable'))
    mount()
    expect(await screen.findByText('历史负载读取失败，请重试。')).toBeInTheDocument()
    expect(screen.queryByTestId('historical-chart')).not.toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: '刷新趋势' }))
    expect(await screen.findByTestId('historical-chart')).toBeInTheDocument()
  })
})
