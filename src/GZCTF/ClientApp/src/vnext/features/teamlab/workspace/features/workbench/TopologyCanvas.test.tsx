import { MantineProvider } from '@mantine/core'
import { render } from '@testing-library/react'
import { type ComponentProps, type ReactNode } from 'react'
import { describe, expect, it, vi } from 'vitest'
import { TopologyCanvas } from './TopologyCanvas'

const viewport = vi.hoisted(() => ({
  fitView: vi.fn(),
  getNodes: vi.fn(),
  getNode: vi.fn(),
  getViewport: () => ({ x: 40, y: 30, zoom: 1 }),
  setViewport: vi.fn(),
  zoomIn: vi.fn(),
  zoomOut: vi.fn(),
}))
const store = vi.hoisted(() => ({ getState: () => ({ width: 900, height: 600 }) }))
vi.mock('@xyflow/react', async (original) => ({
  ...(await original<typeof import('@xyflow/react')>()),
  ReactFlowProvider: ({ children }: { children: ReactNode }) => children,
  ReactFlow: () => null,
  useReactFlow: () => viewport,
  useStoreApi: () => store,
  useNodesInitialized: () => false,
}))
vi.mock('motion/react', () => ({
  animate: (_from: number, _to: number, options: { onUpdate: (progress: number) => void }) => {
    options.onUpdate(1)
    return { stop: vi.fn() }
  },
}))

describe('topology selection and locating', () => {
  it('moves only for an explicit locate request, including repeated locating of the same node', () => {
    const nodes: ComponentProps<typeof TopologyCanvas>['nodes'] = ['first', 'second'].map((id, i) => ({
      id,
      type: 'asset',
      position: { x: i * 300, y: 100 },
      data: { label: id, secondary: '', kind: 'vm', state: 'running', color: 'blue' },
    }))
    viewport.getNodes.mockReturnValue(nodes)
    viewport.getNode.mockImplementation((id: string) => nodes.find((node) => node.id === id))
    const view = (selection?: string, focusRequest?: { id: string }) => (
      <MantineProvider env="test">
        <TopologyCanvas
          nodes={nodes}
          edges={[]}
          selection={selection}
          focusRequest={focusRequest}
          onSelect={vi.fn()}
          editing={false}
          onConnect={vi.fn()}
          onContext={vi.fn()}
        />
      </MantineProvider>
    )
    const { rerender } = render(view())
    rerender(view('first'))
    rerender(view('second'))
    expect(viewport.setViewport).not.toHaveBeenCalled()
    const request = { id: 'first' }
    rerender(view('first', request))
    expect(viewport.setViewport).toHaveBeenCalledTimes(1)
    // An ordinary selection must not replay a previous locate request.
    rerender(view('second', request))
    rerender(view('first', request))
    expect(viewport.setViewport).toHaveBeenCalledTimes(1)
    rerender(view('first', { id: 'first' }))
    expect(viewport.setViewport).toHaveBeenCalledTimes(2)
    rerender(view())
    expect(viewport.setViewport).toHaveBeenCalledTimes(2)
  })
})
