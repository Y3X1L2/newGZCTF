import { render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { CheckerStatus } from '@Api'
import { AwdpInstance } from '../../awdp/awdpDomain'
import { AwdpServiceTable } from './AwdpServiceTable'

function instance(overrides: Partial<AwdpInstance>): AwdpInstance {
  return {
    instanceId: 1,
    serviceId: 1,
    serviceName: 'Web Service',
    teamId: 1,
    teamName: 'Team A',
    ipAddress: '10.24.0.30',
    port: 32768,
    endpoint: 'http://10.24.0.30:32768',
    checkerStatus: CheckerStatus.OK,
    running: true,
    remainingResetCount: 2,
    remainingRecoveryCount: 1,
    canManage: true,
    ...overrides,
  }
}

describe('AWDP service table', () => {
  it('shows every attack target but only exposes management actions for the own team', () => {
    render(
      <AwdpServiceTable
        instances={[
          instance({ instanceId: 1 }),
          instance({
            instanceId: 2,
            teamId: 2,
            teamName: 'Team B',
            canManage: false,
            port: 32769,
            endpoint: 'http://10.24.0.30:32769',
          }),
        ]}
        myTeamId={1}
        onAction={vi.fn()}
        operation={null}
      />
    )
    expect(screen.getByText('Team A')).toBeInTheDocument()
    expect(screen.getByText('Team B')).toBeInTheDocument()
    expect(screen.getAllByRole('link')).toHaveLength(2)
    expect(screen.getByRole('button', { name: '重置 Web Service' })).toBeInTheDocument()
    expect(screen.getByText('仅访问')).toBeInTheDocument()
  })
})
