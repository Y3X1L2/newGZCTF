import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { InstanceControl } from './InstanceControl'
import type { RuntimeInstanceController } from './types'

const originalClipboard = Object.getOwnPropertyDescriptor(navigator, 'clipboard')!
const originalCommand = Object.getOwnPropertyDescriptor(document, 'execCommand')
afterEach(() => {
  Object.defineProperty(navigator, 'clipboard', originalClipboard)
  if (originalCommand) Object.defineProperty(document, 'execCommand', originalCommand)
  else Reflect.deleteProperty(document, 'execCommand')
})

const controller: RuntimeInstanceController = {
  kind: 'docker',
  phase: 'running',
  entry: 'example.invalid:30000',
  closeTime: null,
  entryStatus: null,
  entryReadyAt: null,
  entryError: null,
  vmStatus: null,
  error: null,
  busy: false,
  create: vi.fn(),
  extend: vi.fn(),
  destroy: vi.fn(),
  refresh: vi.fn(),
}

describe('instance entry copying over HTTP', () => {
  it.each([true, false])('shows the actual fallback result when copying returns %s', async (success) => {
    Object.defineProperty(navigator, 'clipboard', { configurable: true, value: undefined })
    const copy = vi.fn(() => success)
    Object.defineProperty(document, 'execCommand', { configurable: true, value: copy })
    render(<InstanceControl controller={controller} />)
    const button = screen.getByRole('button', { name: '复制实例入口' })
    fireEvent.click(button)
    await waitFor(() => expect(copy).toHaveBeenCalledWith('copy'))
    if (success) {
      await waitFor(() => expect(button.querySelector('.lucide-check')).not.toBeNull())
      expect(screen.queryByText('复制失败，请选中文本后手动复制。')).not.toBeInTheDocument()
    } else {
      expect(await screen.findByText('复制失败，请选中文本后手动复制。')).toBeInTheDocument()
      expect(button.querySelector('.lucide-check')).toBeNull()
    }
  })
})
