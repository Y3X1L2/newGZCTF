import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterAll, beforeAll, describe, expect, it, vi } from 'vitest'
import { createEmptyTopologyDocument } from '../model/topologyDocument'
import { TeamLabDesignPage } from './TeamLabDesignPage'

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

  it('keeps the canvas mounted when focus mode changes', () => {
    const view = render(<TeamLabDesignPage initialDocument={createEmptyTopologyDocument('Demo')} />)
    const canvas = view.container.querySelector('.react-flow')
    expect(canvas).not.toBeNull()

    fireEvent.click(screen.getByRole('button', { name: '专注模式' }))

    expect(view.container.querySelector('.react-flow')).toBe(canvas)
  })
})
