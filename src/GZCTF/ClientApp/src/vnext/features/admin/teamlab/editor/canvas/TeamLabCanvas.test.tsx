import { act, render, waitFor } from '@testing-library/react'
import type { PropsWithChildren } from 'react'
import { describe, expect, it, vi } from 'vitest'
import { createEmptyTopologyDocument } from '../../model/topologyDocument'
import { ASSET_NODE_HEIGHT, INFRA_NODE_HEIGHT, NODE_WIDTH } from '../../model/topologyGeometry'
import { createTopologyNode } from '../nodeFactory'
import { TeamLabCanvas } from './TeamLabCanvas'
import { topologyLayers } from './topologyLayers'

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
  it('never enables render-on-demand culling, so auto-layout edges stay visible during fitView animation', () => {
    // Regression: onlyRenderVisibleElements culls edges whose endpoints sit
    // outside the viewport rectangle while the fitView animation is running.
    // The layout moves every node in one commit; the visibility check still uses
    // the pre-animation node bounds, so edges disappear (or flash at stale
    // coordinates) until the animation settles. Browser A/B test confirmed it:
    // without the flag every edge renders in the same frame as the click.
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
        onFitRegion={vi.fn()}
        onMoveNodes={vi.fn()}
        onMoveRegion={vi.fn()}
        onNetworkRegionSelect={vi.fn()}
        onRedo={vi.fn()}
        onResizeRegion={vi.fn()}
        onSelectionChange={vi.fn()}
        onToggleFocus={vi.fn()}
        onToggleLeftPanel={vi.fn()}
        onToggleRegion={vi.fn()}
        onToggleRightPanel={vi.fn()}
        onUndo={vi.fn()}
        readOnly={false}
        rightPanelOpen
        selectedNetworkKey={null}
        selection={{ nodeKeys: new Set(), connectionKeys: new Set() }}
      />
    )

    expect(capturedFlowProps.current).not.toHaveProperty('onlyRenderVisibleElements')
  })

  it('projects explicit node width/height, so edge endpoints never wait for a resize measurement', () => {
    // Regression: without explicit dimensions React Flow computes edge endpoints
    // from the *measured* node size. Auto layout rebuilds every node in one
    // commit, delaying the ResizeObserver pass; during that window edges are
    // drawn to unmeasured (0/undefined)-size coordinates — a vertical strip of
    // lines at a far-off negative x that never recovers. Headless real-scene
    // A/B test locked this: memberships/route edges all became x=-1433 lines
    // after one layout click until dimensions were supplied.
    const empty = createEmptyTopologyDocument('Canvas')
    const switchNode = createTopologyNode(empty, 'switch', { x: 40, y: 40 })
    const withSwitch = { ...empty, nodes: { [switchNode.key]: switchNode } }
    const assetNode = createTopologyNode(withSwitch, 'docker', { x: 280, y: 40 })
    const props = {
      canRedo: false,
      canUndo: false,
      connectionMode: 'network' as const,
      document: {
        ...withSwitch,
        nodes: { ...withSwitch.nodes, [assetNode.key]: assetNode },
      },
      focusMode: false,
      layoutRequest: 0,
      leftPanelOpen: true,
      onAddNode: vi.fn(),
      onAutoLayout: vi.fn(),
      onConnectNodes: vi.fn(),
      onFitRegion: vi.fn(),
      onMoveNodes: vi.fn(),
      onMoveRegion: vi.fn(),
      onNetworkRegionSelect: vi.fn(),
      onRedo: vi.fn(),
      onResizeRegion: vi.fn(),
      onSelectionChange: vi.fn(),
      onToggleFocus: vi.fn(),
      onToggleLeftPanel: vi.fn(),
      onToggleRegion: vi.fn(),
      onToggleRightPanel: vi.fn(),
      onUndo: vi.fn(),
      readOnly: false,
      rightPanelOpen: true,
      selectedNetworkKey: null,
      selection: { nodeKeys: new Set<string>(), connectionKeys: new Set<string>() },
    }
    render(<TeamLabCanvas {...props} />)

    const nodes = capturedFlowProps.current?.nodes as Array<{ id: string; width?: number; height?: number }>
    const switchFlow = nodes.find((n) => n.id === switchNode.key)
    const assetFlow = nodes.find((n) => n.id === assetNode.key)
    expect(switchFlow?.width).toBe(NODE_WIDTH)
    expect(switchFlow?.height).toBe(INFRA_NODE_HEIGHT)
    expect(assetFlow?.width).toBe(NODE_WIDTH)
    expect(assetFlow?.height).toBe(ASSET_NODE_HEIGHT)
  })


  it('defaults to canvas panning and exposes an explicit box-selection tool', () => {
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
        onFitRegion={vi.fn()}
        onMoveNodes={vi.fn()}
        onMoveRegion={vi.fn()}
        onNetworkRegionSelect={vi.fn()}
        onRedo={vi.fn()}
        onResizeRegion={vi.fn()}
        onSelectionChange={vi.fn()}
        onToggleFocus={vi.fn()}
        onToggleLeftPanel={vi.fn()}
        onToggleRegion={vi.fn()}
        onToggleRightPanel={vi.fn()}
        onUndo={vi.fn()}
        readOnly={false}
        rightPanelOpen
        selectedNetworkKey={null}
        selection={{ nodeKeys: new Set(), connectionKeys: new Set() }}
      />
    )

    expect(capturedFlowProps.current).toMatchObject({
      panActivationKeyCode: 'Space',
      panOnDrag: [0, 1],
      selectionKeyCode: ['Meta', 'Control'],
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
      onFitRegion: vi.fn(),
      onMoveNodes: vi.fn(),
      onMoveRegion: vi.fn(),
      onNetworkRegionSelect: vi.fn(),
      onRedo: vi.fn(),
      onResizeRegion: vi.fn(),
      onSelectionChange: vi.fn(),
      onToggleFocus: vi.fn(),
      onToggleLeftPanel: vi.fn(),
      onToggleRegion: vi.fn(),
      onToggleRightPanel: vi.fn(),
      onUndo: vi.fn(),
      readOnly: false,
      rightPanelOpen: true,
      selectedNetworkKey: null,
    }
    const view = render(<TeamLabCanvas {...props} selection={{ nodeKeys: new Set(), connectionKeys: new Set() }} />)
    const initialNodes = capturedFlowProps.current?.nodes as Array<{ id: string }>
    const initialEdges = capturedFlowProps.current?.edges as Array<{ id: string }>
    const unchangedNode = initialNodes.find((node) => node.id === assetNode.key)

    // Layering contract: regions are the backdrop, links draw over them, devices
    // draw over links. With everything left at the default zIndex 0 (the previous
    // behaviour) React Flow's DOM order buried links under region rectangles.
    const layered = initialNodes as Array<{ id: string; zIndex?: number }>
    const regionZ = layered.find((node) => node.id.startsWith('region:'))!.zIndex!
    const deviceZ = layered.find((node) => node.id === assetNode.key)!.zIndex!
    const edgeZ = (initialEdges as Array<{ zIndex?: number }>)[0].zIndex!
    expect(regionZ).toBe(topologyLayers.region)
    expect(regionZ).toBeLessThan(edgeZ)
    expect(edgeZ).toBeLessThan(deviceZ)

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

  it('keeps region containers out of marquee selection and moves their members during the drag', async () => {
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
    const onSelectionChange = vi.fn()
    render(
      <TeamLabCanvas
        canRedo={false}
        canUndo={false}
        connectionMode="network"
        document={document}
        focusMode={false}
        layoutRequest={0}
        leftPanelOpen
        onAddNode={vi.fn()}
        onAutoLayout={vi.fn()}
        onConnectNodes={vi.fn()}
        onFitRegion={vi.fn()}
        onMoveNodes={vi.fn()}
        onMoveRegion={vi.fn()}
        onNetworkRegionSelect={vi.fn()}
        onRedo={vi.fn()}
        onResizeRegion={vi.fn()}
        onSelectionChange={onSelectionChange}
        onToggleFocus={vi.fn()}
        onToggleLeftPanel={vi.fn()}
        onToggleRegion={vi.fn()}
        onToggleRightPanel={vi.fn()}
        onUndo={vi.fn()}
        readOnly={false}
        rightPanelOpen
        selectedNetworkKey={null}
        selection={{ nodeKeys: new Set(), connectionKeys: new Set() }}
      />
    )

    const initial = capturedFlowProps.current!
    const region = (initial.nodes as Array<{ id: string; position: { x: number; y: number } }>).find((node) => node.id.startsWith('region:'))!
    const initialAsset = (initial.nodes as Array<{ id: string; position: { x: number; y: number } }>).find((node) => node.id === assetNode.key)!
    await act(async () => {
      ;(initial.onNodesChange as (changes: unknown[]) => void)([{ type: 'select', id: region.id, selected: true }])
      ;(initial.onNodeDragStart as (event: unknown, node: unknown) => void)({}, region)
      ;(initial.onNodeDrag as (event: unknown, node: unknown) => void)({}, { ...region, position: { x: region.position.x + 96, y: region.position.y + 64 } })
    })

    await waitFor(() => {
      const nodes = capturedFlowProps.current?.nodes as Array<{ id: string; position: { x: number; y: number }; selected?: boolean }>
      // A region keeps React Flow's internal selection acknowledgement so marquee
      // selection cannot enter a controlled-state feedback loop. It is still
      // excluded from the domain selection reported to the editor.
      expect(nodes.find((node) => node.id === region.id)?.selected).toBe(true)
      expect(nodes.find((node) => node.id === assetNode.key)?.position).toEqual({ x: initialAsset.position.x + 96, y: initialAsset.position.y + 64 })
    })
    expect(onSelectionChange).not.toHaveBeenCalled()

    await act(async () => {
      const onNodesChange = capturedFlowProps.current?.onNodesChange
      expect(onNodesChange).toBeTypeOf('function')
      ;(onNodesChange as (changes: unknown[]) => void)([
        { type: 'select', id: assetNode.key, selected: true },
      ])
    })
    await waitFor(() => expect(onSelectionChange).toHaveBeenCalledWith([assetNode.key], []))
  })
})
