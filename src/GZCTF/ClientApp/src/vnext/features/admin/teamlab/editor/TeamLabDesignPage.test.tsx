import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterAll, beforeAll, describe, expect, it, vi } from 'vitest'
import { addTopologyNode } from '../model/topologyCommands'
import { createEmptyTopologyDocument } from '../model/topologyDocument'
import { TeamLabDesignPage } from './TeamLabDesignPage'
import { createTopologyNode } from './nodeFactory'

const OriginalResizeObserver = globalThis.ResizeObserver

beforeAll(() => {
  globalThis.ResizeObserver = class ResizeObserver {
    observe() {}
    unobserve() {}
    disconnect() {}
  }
})

afterAll(() => {
  globalThis.ResizeObserver = OriginalResizeObserver
})

describe('TeamLabDesignPage', () => {
  it('adds a palette node as one history entry and supports undo', async () => {
    const onChange = vi.fn()
    render(<TeamLabDesignPage initialDocument={createEmptyTopologyDocument('Demo')} onDocumentChange={onChange} />)

    fireEvent.click(screen.getByRole('button', { name: /交换机.*承载一个隔离网段/ }))
    await waitFor(() => expect(onChange).toHaveBeenCalledTimes(1))
    expect(Object.values(onChange.mock.calls[0][0].nodes)).toHaveLength(1)

    fireEvent.keyDown(window, { key: 'z', ctrlKey: true })
    await waitFor(() => expect(onChange).toHaveBeenCalledTimes(2))
    expect(Object.values(onChange.mock.calls[1][0].nodes)).toHaveLength(0)
  })

  it('keeps every rapid palette command when React has not rendered between clicks', async () => {
    const onChange = vi.fn()
    render(<TeamLabDesignPage initialDocument={createEmptyTopologyDocument('Demo')} onDocumentChange={onChange} />)

    const addSwitch = screen.getByRole('button', { name: /交换机.*承载一个隔离网段/ })
    fireEvent.click(addSwitch)
    fireEvent.click(addSwitch)
    fireEvent.click(addSwitch)

    await waitFor(() => {
      const latestDocument = onChange.mock.calls.at(-1)?.[0]
      expect(Object.values(latestDocument.nodes)).toHaveLength(3)
    })
  })

  it('keeps the canvas mounted when focus mode changes', () => {
    const view = render(<TeamLabDesignPage initialDocument={createEmptyTopologyDocument('Demo')} />)
    const canvas = view.container.querySelector('.react-flow')
    expect(canvas).not.toBeNull()

    fireEvent.click(screen.getByRole('button', { name: '专注模式' }))

    expect(view.container.querySelector('.react-flow')).toBe(canvas)
  })

  it('sends auto-layout through the document change persistence path', async () => {
    let document = createEmptyTopologyDocument('Demo')
    document = addTopologyNode(document, createTopologyNode(document, 'switch', { x: 0, y: 0 })).document
    document = addTopologyNode(document, createTopologyNode(document, 'switch', { x: 40, y: 40 })).document
    const onChange = vi.fn()
    render(<TeamLabDesignPage initialDocument={document} onDocumentChange={onChange} />)

    fireEvent.click(screen.getByRole('button', { name: '一键自动排版' }))

    await waitFor(() => expect(onChange).toHaveBeenCalledTimes(1))
    const savedDocument = onChange.mock.calls[0][0]
    expect(Object.keys(savedDocument.networkLayouts)).toHaveLength(2)
    expect(savedDocument.nodes).not.toEqual(document.nodes)
    expect(screen.getByText(/布局将自动保存/)).toBeInTheDocument()
  })

  it('disables endpoint observation on a newly added standard asset', async () => {
    const onChange = vi.fn()
    render(<TeamLabDesignPage initialDocument={createEmptyTopologyDocument('Demo')} onDocumentChange={onChange} />)

    fireEvent.click(screen.getByRole('button', { name: /Docker：轻量容器服务/ }))

    await waitFor(() => expect(onChange).toHaveBeenCalledTimes(1))
    expect(Object.values(onChange.mock.calls[0][0].nodes)[0]).toMatchObject({ endpointObservation: 'disabled' })
  })

  it('does not expose creation of unsupported startup dependencies', () => {
    render(<TeamLabDesignPage initialDocument={createEmptyTopologyDocument('Demo')} />)

    expect(screen.queryByRole('button', { name: '启动依赖' })).not.toBeInTheDocument()
  })
})
