import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import type { NodeSummary } from '../api'
import { NodeTeamLabPanel } from './NodeTeamLabPanel'

describe('node TeamLab capability summary', () => {
  it('separates available Docker from unavailable VM and collapses raw protocol details', () => {
    const probe = vi.fn().mockResolvedValue(undefined)
    const node = {
      canHostTeamLabFabric: true,
      canHostTeamLabDocker: true,
      canHostTeamLabVm: false,
      teamLabTunnelStatus: 0,
      unschedulableByCapability: { teamLabVm: 'KVM 未就绪' },
      agentFeatures: ['docker.create.v1', 'teamlab.fabric.v2'],
      capabilityManifestSchemaVersion: 2,
    } as unknown as NodeSummary
    render(<NodeTeamLabPanel node={node} working={false} onProbe={probe} />)
    expect(screen.getAllByText('可调度')).toHaveLength(2)
    expect(screen.getByText('暂不可调度')).toBeTruthy()
    expect(screen.getByText('KVM 未就绪')).toBeTruthy()
    expect(screen.getByText('docker.create.v1').closest('details')?.open).toBe(false)
    fireEvent.click(screen.getByRole('button', { name: '运行兼容性检测' }))
    expect(probe).toHaveBeenCalledOnce()
  })
})
