import { render, waitFor } from '@testing-library/react'
import type { PropsWithChildren } from 'react'
import { describe, expect, it, vi } from 'vitest'
import { createEmptyTopologyDocument } from '../../model/topologyDocument'
import { createTopologyNode } from '../nodeFactory'
import { TeamLabCanvas } from './TeamLabCanvas'

const capturedFlowProps = vi.hoisted(() => ({ current: null as Record<string, unknown> | null }))

vi.mock('@xyflow/react', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@xyflow/react')>()
  return {
    ...actual,
    ReactFlow: ({ children, ...props }: PropsWithChildren<Record<string, unknown>>) => {
      capturedFlowProps.current = props
      return <div data-testid="teamlab-react-flow">{children}</div>
    },
  }
})

describe('TeamLabCanvas', () => {
  it('uses direct manipulation navigation without an explicit interaction mode', () => {
    render(
      <TeamLabCanvas
        canRedo={false}
        canUndo={false}
        connectionMode="network"
        document={createEmptyTopologyDocument('Canvas')}
        focusMode={false}
        layoutRequest={0}
        leftPanelOpen
        onAddNode={vi.fn()}
        onAutoLayout={vi.fn()}
        onConnectNodes={vi.fn()}
        onMoveNodes={vi.fn()}
        onRedo={vi.fn()}
        onSelectionChange={vi.fn()}
        onToggleFocus={vi.fn()}
        onToggleLeftPanel={vi.fn()}
        onToggleRightPanel={vi.fn()}
        onUndo={vi.fn()}
        readOnly={false}
        rightPanelOpen
        selection={{ nodeKeys: new Set(), connectionKeys: new Set() }}
      />
    )

    expect(capturedFlowProps.current).toMatchObject({
      panActivationKeyCode: 'Space',
      panOnDrag: true,
      selectionKeyCode: 'Shift',
      selectionOnDrag: false,
      zoomOnPinch: true,
      zoomOnScroll: true,
    })
  })

  it('preserves unaffected node and edge objects when selection changes', async () => {
    const empty = createEmptyTopologyDocument('Canvas')
    const switchNode = createTopologyNode(empty, 'switch', { x: 40, y: 40 })
    const withSwitch = { ...empty, nodes: { [switchNode.key]: switchNode } }
    const assetNode = createTopologyNode(withSwitch, 'docker', { x: 280, y: 40 })
    const document = {
      ...withSwitch,
      nodes: { ...withSwitch.nodes, [assetNode.key]: assetNode },
      connections: {
        membership: {
          type: 'membership' as const,
          key: 'membership',
          nodeKey: assetNode.key,
          switchKey: switchNode.key,
          hostOffset: 10,
          primary: true,
          orderIndex: 0,
        },
      },
    }
    const props = {
      canRedo: false,
      canUndo: false,
      connectionMode: 'network' as const,
      document,
      focusMode: false,
      layoutRequest: 0,
      leftPanelOpen: true,
      onAddNode: vi.fn(),
      onAutoLayout: vi.fn(),
      onConnectNodes: vi.fn(),
      onMoveNodes: vi.fn(),
      onRedo: vi.fn(),
      onSelectionChange: vi.fn(),
      onToggleFocus: vi.fn(),
      onToggleLeftPanel: vi.fn(),
      onToggleRightPanel: vi.fn(),
      onUndo: vi.fn(),
      readOnly: false,
      rightPanelOpen: true,
    }
    const view = render(<TeamLabCanvas {...props} selection={{ nodeKeys: new Set(), connectionKeys: new Set() }} />)
    const initialNodes = capturedFlowProps.current?.nodes as Array<{ id: string }>
    const initialEdges = capturedFlowProps.current?.edges as Array<{ id: string }>
    const unchangedNode = initialNodes.find((node) => node.id === assetNode.key)

    view.rerender(
      <TeamLabCanvas {...props} selection={{ nodeKeys: new Set([switchNode.key]), connectionKeys: new Set() }} />
    )

    await waitFor(() => {
      const nextNodes = capturedFlowProps.current?.nodes as Array<{ id: string; selected?: boolean }>
      expect(nextNodes.find((node) => node.id === switchNode.key)?.selected).toBe(true)
    })
    const nextNodes = capturedFlowProps.current?.nodes as Array<{ id: string }>
    const nextEdges = capturedFlowProps.current?.edges as Array<{ id: string }>
    expect(nextNodes.find((node) => node.id === assetNode.key)).toBe(unchangedNode)
    expect(nextEdges[0]).toBe(initialEdges[0])
  })
})
