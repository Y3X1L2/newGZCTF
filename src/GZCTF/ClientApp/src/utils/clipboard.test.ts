import { afterEach, describe, expect, it, vi } from 'vitest'
import { copyText } from './clipboard'

const originalClipboard = Object.getOwnPropertyDescriptor(navigator, 'clipboard')!
const originalCommand = Object.getOwnPropertyDescriptor(document, 'execCommand')

function clipboard(value: unknown) {
  Object.defineProperty(navigator, 'clipboard', { configurable: true, value })
}

function command(value: unknown) {
  Object.defineProperty(document, 'execCommand', { configurable: true, value })
}

afterEach(() => {
  Object.defineProperty(navigator, 'clipboard', originalClipboard)
  if (originalCommand) Object.defineProperty(document, 'execCommand', originalCommand)
  else Reflect.deleteProperty(document, 'execCommand')
  document.body.replaceChildren()
})

describe('copyText', () => {
  it('uses the native clipboard when it succeeds', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined)
    clipboard({ writeText })
    const fallback = vi.fn()
    command(fallback)
    expect(await copyText('入口 example.invalid:30000')).toBe(true)
    expect(writeText).toHaveBeenCalledWith('入口 example.invalid:30000')
    expect(fallback).not.toHaveBeenCalled()
  })

  it('copies on HTTP without Clipboard API and restores input selection', async () => {
    clipboard(undefined)
    const input = document.createElement('input')
    input.value = 'existing text'
    document.body.append(input)
    input.focus()
    input.setSelectionRange(2, 6)
    command(
      vi.fn(() => {
        expect((document.activeElement as HTMLTextAreaElement).value).toBe('中文\nsecond line')
        return true
      })
    )
    expect(await copyText('中文\nsecond line')).toBe(true)
    expect(document.querySelector('textarea')).toBeNull()
    expect(document.activeElement).toBe(input)
    expect([input.selectionStart, input.selectionEnd]).toEqual([2, 6])
  })

  it('falls back inside the active modal when native clipboard is denied', async () => {
    clipboard({ writeText: vi.fn().mockRejectedValue(new Error('denied')) })
    const dialog = document.createElement('dialog')
    dialog.setAttribute('open', '')
    const button = document.createElement('button')
    dialog.append(button)
    document.body.append(dialog)
    button.focus()
    command(
      vi.fn(() => {
        expect(document.activeElement?.parentElement).toBe(dialog)
        return true
      })
    )
    expect(await copyText('test text')).toBe(true)
    expect(document.activeElement).toBe(button)
    expect(dialog.querySelector('textarea')).toBeNull()
  })

  it.each(['false', 'throw', 'unsupported'])('reports %s fallback failure without leaving a textarea', async (mode) => {
    clipboard(undefined)
    command(
      mode === 'unsupported'
        ? undefined
        : vi.fn(() => {
            if (mode === 'throw') throw new Error('blocked')
            return false
          })
    )
    expect(await copyText('test text')).toBe(false)
    expect(document.querySelector('textarea')).toBeNull()
  })
})
