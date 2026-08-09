import { describe, expect, it } from 'vitest'
import { createEmptyTopologyDocument } from '../../model/topologyDocument'
import { createEditorHistory, editorHistoryLimit, editorReducer } from './editorReducer'

describe('editorReducer', () => {
  it('commits, undoes and redoes complete documents', () => {
    const initial = createEmptyTopologyDocument('Initial')
    const changed = { ...initial, name: 'Changed' }
    const committed = editorReducer(createEditorHistory(initial), { type: 'commit', document: changed })
    const undone = editorReducer(committed, { type: 'undo' })
    const redone = editorReducer(undone, { type: 'redo' })

    expect(committed.present).toBe(changed)
    expect(undone.present).toBe(initial)
    expect(redone.present).toBe(changed)
  })

  it('bounds history without splitting one committed interaction', () => {
    let state = createEditorHistory(createEmptyTopologyDocument('0'))
    for (let index = 1; index <= editorHistoryLimit + 8; index += 1) {
      state = editorReducer(state, { type: 'commit', document: { ...state.present, name: String(index) } })
    }
    expect(state.past).toHaveLength(editorHistoryLimit)
    expect(state.present.name).toBe(String(editorHistoryLimit + 8))
  })
})
