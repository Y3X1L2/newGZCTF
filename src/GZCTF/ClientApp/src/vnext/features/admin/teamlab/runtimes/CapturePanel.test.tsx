import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { SWRConfig } from 'swr'
import { describe, expect, it, vi } from 'vitest'
import { teamLabRuntimeApi, type TeamLabCapture } from '../api'
import { CapturePanel } from './CapturePanel'

const runtimeId = '019f0000-0000-7000-8000-000000000010'
const running: TeamLabCapture = {
  id: '019f0000-0000-7000-8000-000000000040',
  status: 'running',
  scope: 'runtime',
  networkKey: null,
  maxBytes: 256 * 1024 * 1024,
  maxSeconds: 300,
  capturedBytes: 1024,
  createdAt: 1_784_832_000_000,
  startedAt: 1_784_832_000_000,
  completedAt: null,
  expiresAt: 1_784_918_400_000,
  segments: [],
  error: null,
}

describe('CapturePanel', () => {
  it('starts, stops and exposes the adapter download URL for a completed capture', async () => {
    const start = vi.spyOn(teamLabRuntimeApi, 'startCapture').mockResolvedValue(running)
    vi.spyOn(teamLabRuntimeApi, 'getCapture').mockResolvedValue(running)
    vi.spyOn(teamLabRuntimeApi, 'stopCapture').mockResolvedValue({ ...running, status: 'completed', completedAt: 1_784_832_300_000 })
    render(<SWRConfig value={{ provider: () => new Map(), dedupingInterval: 0 }}><CapturePanel networks={[{ key: 'entry', name: '入口网段', cidr: '10.10.0.0/24', gatewayIp: '10.10.0.1' }]} runtimeId={runtimeId} /></SWRConfig>)

    fireEvent.click(screen.getByRole('button', { name: '开始抓包' }))
    await waitFor(() => expect(start).toHaveBeenCalledWith(runtimeId, {
      scope: 'runtime', networkKey: null, maxSeconds: 300, maxBytes: 256 * 1024 * 1024, expiresInSeconds: 86400,
    }))
    fireEvent.click(await screen.findByRole('button', { name: '停止' }))
    const download = await screen.findByRole('link', { name: '下载 PCAP' })
    expect(download).toHaveAttribute('href', teamLabRuntimeApi.captureDownloadPath(runtimeId, running.id))
  })
})
