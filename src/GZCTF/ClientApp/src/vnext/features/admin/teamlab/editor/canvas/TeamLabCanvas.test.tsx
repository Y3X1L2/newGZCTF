import { render } from '@testing-library/react'
import type { PropsWithChildren } from 'react'
import { describe, expect, it, vi } from 'vitest'
import { createEmptyTopologyDocument } from '../../model/topologyDocument'
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
})
