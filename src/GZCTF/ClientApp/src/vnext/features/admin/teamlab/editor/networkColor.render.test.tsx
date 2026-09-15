import { render, screen } from '@testing-library/react'
import { Position, ReactFlowProvider, type EdgeProps, type NodeProps } from '@xyflow/react'
import { describe, expect, it, vi } from 'vitest'
import { NetworkEdge } from './edges/NetworkEdge'
import type { TeamLabFlowEdge } from './edges/edgeTypes'
import { networkColorSlot } from './networkColor'
import { NetworkRegionNode, type TeamLabRegionFlowNode } from './regions/NetworkRegionNode'

describe('network color rendering', () => {
  it('applies the same stable accent to a region and its membership edge', () => {
    const networkKey = 'data-network'
    const actions = { toggleCollapsed: vi.fn(), resize: vi.fn(), fitToMembers: vi.fn() }
    const regionProps = {
      id: `region:${networkKey}`,
      type: 'region',
      selected: false,
      data: {
        networkKey,
        switchKey: 'data-switch',
        name: '数据网段',
        addressPool: '10.20.0.0/16',
        isEntry: false,
        memberCount: 2,
        readOnly: false,
        active: false,
        collapsed: false,
        actions,
      },
    } as unknown as NodeProps<TeamLabRegionFlowNode>
    const edgeProps = {
      id: 'asset-data',
      source: 'asset',
      target: 'data-switch',
      sourceX: 0,
      sourceY: 0,
      targetX: 120,
      targetY: 80,
      sourcePosition: Position.Right,
      targetPosition: Position.Left,
      selected: false,
      data: { connection: null, label: '', tone: 'membership', networkKey },
    } as EdgeProps<TeamLabFlowEdge>

    const view = render(
      <ReactFlowProvider>
        <NetworkRegionNode {...regionProps} />
        <svg>
          <NetworkEdge {...edgeProps} />
        </svg>
      </ReactFlowProvider>
    )

    const region = screen.getByRole('article', { name: '网络区域 数据网段' })
    const edge = view.container.querySelector('.react-flow__edge-path')
    const colorSlot = networkColorSlot(networkKey)
    expect(region).toHaveAttribute('data-color-slot', colorSlot)
    expect(edge).toHaveAttribute('data-color-slot', colorSlot)
  })
})
