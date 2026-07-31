import { useEffect } from 'react'

export type EditorShortcutAction =
  | 'undo'
  | 'redo'
  | 'copy'
  | 'paste'
  | 'duplicate'
  | 'delete'
  | 'select-all'
  | 'save'
  | 'nudge-left'
  | 'nudge-right'
  | 'nudge-up'
  | 'nudge-down'

export interface EditorShortcutHandlers {
  undo: () => void
  redo: () => void
  copy: () => void
  paste: () => void
  duplicate: () => void
  delete: () => void
  selectAll: () => void
  save: () => void
  nudge: (delta: { x: number; y: number }) => void
}

export function isEditableShortcutTarget(target: EventTarget | null) {
  return (
    target instanceof Element &&
    target.closest('input, textarea, select, [contenteditable="true"], [role="textbox"]') !== null
  )
}

export function resolveEditorShortcut(event: Pick<KeyboardEvent, 'key' | 'ctrlKey' | 'metaKey' | 'shiftKey'>) {
  const key = event.key.toLowerCase()
  const primary = event.ctrlKey || event.metaKey
  if (primary && key === 'z') return event.shiftKey ? 'redo' : 'undo'
  if (primary && key === 'y') return 'redo'
  if (primary && key === 'c') return 'copy'
  if (primary && key === 'v') return 'paste'
  if (primary && key === 'd') return 'duplicate'
  if (primary && key === 'a') return 'select-all'
  if (primary && key === 's') return 'save'
  if (!primary && (key === 'delete' || key === 'backspace')) return 'delete'
  if (!primary && key === 'arrowleft') return 'nudge-left'
  if (!primary && key === 'arrowright') return 'nudge-right'
  if (!primary && key === 'arrowup') return 'nudge-up'
  if (!primary && key === 'arrowdown') return 'nudge-down'
  return null
}

export function useEditorShortcuts(enabled: boolean, handlers: EditorShortcutHandlers) {
  useEffect(() => {
    if (!enabled) return undefined
    const onKeyDown = (event: KeyboardEvent) => {
      if (isEditableShortcutTarget(event.target)) return
      const action = resolveEditorShortcut(event)
      if (!action) return
      event.preventDefault()
      if (action === 'undo') handlers.undo()
      else if (action === 'redo') handlers.redo()
      else if (action === 'copy') handlers.copy()
      else if (action === 'paste') handlers.paste()
      else if (action === 'duplicate') handlers.duplicate()
      else if (action === 'delete') handlers.delete()
      else if (action === 'select-all') handlers.selectAll()
      else if (action === 'save') handlers.save()
      else {
        const amount = event.shiftKey ? 10 : 1
        handlers.nudge({
          x: action === 'nudge-left' ? -amount : action === 'nudge-right' ? amount : 0,
          y: action === 'nudge-up' ? -amount : action === 'nudge-down' ? amount : 0,
        })
      }
    }
    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [enabled, handlers])
}
