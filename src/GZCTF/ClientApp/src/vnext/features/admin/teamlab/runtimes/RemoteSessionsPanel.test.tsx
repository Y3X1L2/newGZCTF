import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { SWRConfig } from 'swr'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { teamLabRemoteAccessApi } from '../api'
import { RemoteSessionsPanel } from './RemoteSessionsPanel'

const { audit } = vi.hoisted(() => ({ audit: vi.fn() }))
vi.mock('./useRemoteAudit', () => ({ useRemoteAudit: audit }))

afterEach(() => vi.restoreAllMocks())

describe('RemoteSessionsPanel', () => {
  it('opens the selected session audit in a visible drawer and closes it', async () => {
    vi.spyOn(window, 'matchMedia').mockImplementation((query) => ({
      matches: query.includes('prefers-reduced-motion'),
      media: query,
      onchange: null,
      addEventListener: () => undefined,
      removeEventListener: () => undefined,
      addListener: () => undefined,
      removeListener: () => undefined,
      dispatchEvent: () => false,
    }))
    vi.spyOn(teamLabRemoteAccessApi, 'list').mockResolvedValue({
      items: [
        {
          session: {
            id: 'session-a',
            runtimeId: 'runtime-a',
            assetId: 7,
            assetName: 'Web 服务',
            protocol: 'ssh',
            status: 'ended',
            reason: '故障定位',
            createdAt: 1_784_832_000_000,
            expiresAt: 1_784_918_400_000,
            connectedAt: 1_784_832_100_000,
            endedAt: 1_784_833_000_000,
            endReason: 'completed',
          },
          workerNodeId: 'node-a',
          workerNodeName: '执行节点 A',
          requestedByUserId: 'user-a',
          requestedByName: '运维员',
        },
      ],
      nextCursor: null,
    })
    audit.mockReturnValue({
      data: { state: 'ready', retentionDays: 90, items: [] },
      mutate: vi.fn(),
      generate: vi.fn(),
      download: vi.fn(),
    })
    render(
      <SWRConfig value={{ provider: () => new Map(), dedupingInterval: 0 }}>
        <RemoteSessionsPanel runtimeId="runtime-a" />
      </SWRConfig>
    )

    const auditButton = await screen.findByRole('button', { name: '查看 Web 服务 操作审计' })
    fireEvent.click(auditButton)

    expect(auditButton).toHaveAttribute('aria-pressed', 'true')
    const drawer = screen.getByRole('dialog', { name: '操作审计 · Web 服务' })
    expect(within(drawer).getByText('会话操作审计证据')).toBeInTheDocument()
    fireEvent.click(within(drawer).getByRole('button', { name: '关闭' }))

    await waitFor(() => expect(screen.queryByRole('dialog', { name: '操作审计 · Web 服务' })).not.toBeInTheDocument())
    expect(auditButton).toHaveAttribute('aria-pressed', 'false')
  })
})
