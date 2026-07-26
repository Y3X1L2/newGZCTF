import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { resolveEditorShortcut, useEditorShortcuts, type EditorShortcutHandlers } from './useEditorShortcuts'

function ShortcutHarness({ handlers }: { handlers: EditorShortcutHandlers }) {
  useEditorShortcuts(true, handlers)
  return <input aria-label="名称" />
}

const handlers = (): EditorShortcutHandlers => ({
  undo: vi.fn(),
  redo: vi.fn(),
  copy: vi.fn(),
  paste: vi.fn(),
  duplicate: vi.fn(),
  delete: vi.fn(),
  selectAll: vi.fn(),
  save: vi.fn(),
  nudge: vi.fn(),
})

describe('editor shortcuts', () => {
  it('resolves the confirmed shortcut set', () => {
    expect(resolveEditorShortcut({ key: 'z', ctrlKey: true, metaKey: false, shiftKey: false })).toBe('undo')
    expect(resolveEditorShortcut({ key: 'Z', ctrlKey: false, metaKey: true, shiftKey: true })).toBe('redo')
    expect(resolveEditorShortcut({ key: 'y', ctrlKey: true, metaKey: false, shiftKey: false })).toBe('redo')
    expect(resolveEditorShortcut({ key: 'ArrowLeft', ctrlKey: false, metaKey: false, shiftKey: true })).toBe(
      'nudge-left'
    )
  })

  it('does not intercept text inputs and accelerates shifted nudges', () => {
    const actions = handlers()
    render(<ShortcutHarness handlers={actions} />)
    fireEvent.keyDown(screen.getByRole('textbox', { name: '名称' }), { key: 'z', ctrlKey: true })
    fireEvent.keyDown(window, { key: 'ArrowRight', shiftKey: true })

    expect(actions.undo).not.toHaveBeenCalled()
    expect(actions.nudge).toHaveBeenCalledWith({ x: 10, y: 0 })
  })
})
