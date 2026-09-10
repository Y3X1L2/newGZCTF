import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { RemoteAuditPanel } from './RemoteAuditPanel'

const { audit } = vi.hoisted(() => ({ audit: vi.fn() }))
vi.mock('./useRemoteAudit', () => ({ useRemoteAudit: audit }))

describe('RemoteAuditPanel', () => {
  it('allows archival only after the session ends', () => {
    const generate = vi.fn()
    audit.mockReturnValue({ data: { state: 'pending', retentionDays: 90, items: [] }, generate, mutate: vi.fn() })
    const view = render(<RemoteAuditPanel sessionId="session-a" />)
    fireEvent.click(screen.getByRole('button', { name: '归档操作证据' }))
    expect(generate).toHaveBeenCalledOnce()
    audit.mockReturnValue({ data: { state: 'session-active', retentionDays: 90, items: [] }, mutate: vi.fn() })
    view.rerender(<RemoteAuditPanel sessionId="session-a" />)
    expect(screen.queryByRole('button', { name: '归档操作证据' })).toBeNull()
  })
  it('shows digest and expiry and reports download failures without saving an error document', () => {
    const download = vi.fn()
    audit.mockReturnValue({ data: { state: 'ready', retentionDays: 90, items: [{ id: 7, size: 1024,
      sha256: 'a'.repeat(64), createdAt: 1788796800000, expiresAt: 1796572800000 }] }, download, mutate: vi.fn(), actionError: new Error('审计文件内容校验失败。') })
    render(<RemoteAuditPanel sessionId="session-a" />)
    expect(screen.getByText('a'.repeat(64))).toBeTruthy()
    expect(screen.getByText('审计文件内容校验失败。')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: '下载证据 JSON' }))
    expect(download).toHaveBeenCalledWith(7)
  })
})
