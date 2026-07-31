import type { TopologyDocument } from '../../model/topologyDocument'

export const editorHistoryLimit = 100

export interface EditorHistoryState {
  past: readonly TopologyDocument[]
  present: TopologyDocument
  future: readonly TopologyDocument[]
}

export type EditorHistoryAction =
  | { type: 'commit'; document: TopologyDocument }
  | { type: 'replace'; document: TopologyDocument }
  | { type: 'undo' }
  | { type: 'redo' }

export const createEditorHistory = (document: TopologyDocument): EditorHistoryState => ({
  past: [],
  present: document,
  future: [],
})

export function editorReducer(state: EditorHistoryState, action: EditorHistoryAction): EditorHistoryState {
  if (action.type === 'replace') return createEditorHistory(action.document)
  if (action.type === 'commit') {
    if (action.document === state.present) return state
    return {
      past: [...state.past, state.present].slice(-editorHistoryLimit),
      present: action.document,
      future: [],
    }
  }
  if (action.type === 'undo') {
    const previous = state.past.at(-1)
    if (!previous) return state
    return {
      past: state.past.slice(0, -1),
      present: previous,
      future: [state.present, ...state.future],
    }
  }
  const next = state.future[0]
  if (!next) return state
  return {
    past: [...state.past, state.present].slice(-editorHistoryLimit),
    present: next,
    future: state.future.slice(1),
  }
}
