import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { SWRConfig } from 'swr'
import { describe, expect, it, vi } from 'vitest'
import { teamLabRuntimeApi, type TeamLabLinkPolicy } from '../api'
import { RuntimeLinkPolicyPanel } from './RuntimeLinkPolicyPanel'
import { useRuntimeLinkPolicies } from './useRuntimeLinkPolicies'

vi.mock('./useRuntimeLinkPolicies', () => ({ useRuntimeLinkPolicies: vi.fn() }))

const runtimeId = '019f0000-0000-7000-8000-000000000010'

const activePolicy: TeamLabLinkPolicy = {
  id: '019f0000-0000-7000-8000-000000000050',
  runtimeId,
  networkKey: 'office',
  assetKey: 'plc-1',
  kind: 'latency',
  parameters: { delayMillis: 120 },
  status: 'active',
  recoverAt: null,
  appliedAt: '2026-08-17T08:00:00Z',
  recoveredAt: null,
  recoverOrigin: 'none',
  lastError: null,
}

const recoveredPolicy: TeamLabLinkPolicy = {
  ...activePolicy,
  id: '019f0000-0000-7000-8000-000000000051',
  kind: 'link-break',
  parameters: null,
  status: 'recovered',
  recoveredAt: '2026-08-17T08:30:00Z',
  recoverOrigin: 'scheduled',
}

function policies(overrides: Record<string, unknown> = {}) {
  return {
    policies: [activePolicy, recoveredPolicy],
    page: { items: [activePolicy, recoveredPolicy], next: null },
    error: undefined,
    isLoading: false,
    isRefreshing: false,
    mutate: vi.fn(),
    ...overrides,
  }
}

describe('RuntimeLinkPolicyPanel', () => {
  it('lists policies with parameter summaries and recovery actions', async () => {
    const recover = vi.spyOn(teamLabRuntimeApi, 'recoverLinkPolicy').mockResolvedValue(recoveredPolicy)
    vi.mocked(useRuntimeLinkPolicies).mockReturnValue(policies() as ReturnType<typeof useRuntimeLinkPolicies>)
    render(
      <SWRConfig value={{ provider: () => new Map(), dedupingInterval: 0 }}>
        <RuntimeLinkPolicyPanel
          assets={[{ key: 'plc-1', name: 'PLC' }]}
          networks={[{ key: 'office', name: '办公网' }]}
          runtimeId={runtimeId}
        />
      </SWRConfig>
    )

    const rows = screen.getAllByRole('listitem')
    expect(rows[0]).toHaveTextContent('时延')
    expect(rows[0]).toHaveTextContent('delayMillis: 120')
    expect(rows[1]).toHaveTextContent('全链路中断')
    expect(rows[1]).toHaveTextContent('定时')

    fireEvent.click(screen.getByRole('button', { name: '恢复' }))
    await waitFor(() => expect(recover).toHaveBeenCalledWith(runtimeId, activePolicy.id))
  })

  it('applies a policy through the dialog with parsed parameters and optional recovery', async () => {
    vi.mocked(useRuntimeLinkPolicies).mockReturnValue(
      policies({ policies: [], page: { items: [], next: null } }) as ReturnType<typeof useRuntimeLinkPolicies>
    )
    const apply = vi
      .spyOn(teamLabRuntimeApi, 'applyLinkPolicy')
      .mockResolvedValue(activePolicy)
    render(
      <SWRConfig value={{ provider: () => new Map(), dedupingInterval: 0 }}>
        <RuntimeLinkPolicyPanel
          assets={[]}
          networks={[{ key: 'office', name: '办公网' }]}
          runtimeId={runtimeId}
        />
      </SWRConfig>
    )

    fireEvent.click(screen.getByRole('button', { name: '应用策略' }))
    fireEvent.change(screen.getByLabelText('定时恢复分钟'), { target: { value: '10' } })
    fireEvent.click(screen.getByRole('button', { name: '确认应用' }))
    await waitFor(() =>
      expect(apply).toHaveBeenCalledWith(runtimeId, {
        runtimeId,
        networkKey: 'office',
        assetKey: null,
        kind: 'latency',
        parameters: { delayMillis: 100 },
        recoverAt: expect.stringMatching(/^\d{4}-\d{2}-\d{2}T/)
      })
    )
  })
})
