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
    vi.spyOn(teamLabRuntimeApi, 'listCaptureHistory').mockResolvedValue({ items: [], next: null })
    render(<SWRConfig value={{ provider: () => new Map(), dedupingInterval: 0 }}><CapturePanel networks={[{ key: 'entry', name: '入口网段', cidr: '10.10.0.0/24', gatewayIp: '10.10.0.1' }]} runtimeId={runtimeId} /></SWRConfig>)

    fireEvent.click(screen.getByRole('button', { name: '开始抓包' }))
    await waitFor(() => expect(start).toHaveBeenCalledWith(runtimeId, {
      scope: 'runtime', networkKey: null, maxSeconds: 300, maxBytes: 256 * 1024 * 1024, expiresInSeconds: 86400,
    }))
    fireEvent.click(await screen.findByRole('button', { name: '停止' }))
    const download = await screen.findByRole('link', { name: '下载 PCAP' })
    expect(download).toHaveAttribute('href', teamLabRuntimeApi.captureDownloadPath(runtimeId, running.id))
  })

  it('restores the latest capture after the panel remounts', async () => {
    vi.spyOn(teamLabRuntimeApi, 'listCaptureHistory').mockResolvedValue({ items: [running], next: null })
    vi.spyOn(teamLabRuntimeApi, 'getCapture').mockResolvedValue(running)
    render(<SWRConfig value={{ provider: () => new Map(), dedupingInterval: 0 }}><CapturePanel networks={[{ key: 'entry', name: '入口网段', cidr: '10.10.0.0/24', gatewayIp: '10.10.0.1' }]} runtimeId={runtimeId} /></SWRConfig>)

    expect(await screen.findByText('任务标识')).toBeTruthy()
    expect(screen.getByRole('button', { name: '停止' })).toBeTruthy()
  })

  it('lets operators inspect older failed captures and their segment errors', async () => {
    const failed = { ...running, id: 'older-capture', status: 'failed' as const, error: 'upload failed' }
    vi.spyOn(teamLabRuntimeApi, 'listCaptureHistory').mockResolvedValue({ items: [running, failed], next: null })
    vi.spyOn(teamLabRuntimeApi, 'getCapture').mockImplementation(async (_, id) => id === failed.id ? failed : running)
    render(<SWRConfig value={{ provider: () => new Map(), dedupingInterval: 0 }}><CapturePanel networks={[]} runtimeId={runtimeId} /></SWRConfig>)
    const buttons = await screen.findAllByRole('button', { name: '查看详情' })
    fireEvent.click(buttons[1])
    await waitFor(() => expect(teamLabRuntimeApi.getCapture).toHaveBeenCalledWith(runtimeId, failed.id))
    expect(await screen.findAllByText('upload failed')).not.toHaveLength(0)
  })
})
