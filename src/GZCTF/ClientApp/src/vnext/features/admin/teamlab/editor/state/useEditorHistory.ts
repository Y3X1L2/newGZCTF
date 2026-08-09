import { useCallback, useReducer } from 'react'
import type { TopologyDocument } from '../../model/topologyDocument'
import { createEditorHistory, editorReducer } from './editorReducer'

export function useEditorHistory(initialDocument: TopologyDocument) {
  const [state, dispatch] = useReducer(editorReducer, initialDocument, createEditorHistory)

  const commit = useCallback((document: TopologyDocument) => dispatch({ type: 'commit', document }), [])
  const replace = useCallback((document: TopologyDocument) => dispatch({ type: 'replace', document }), [])
  const undo = useCallback(() => dispatch({ type: 'undo' }), [])
  const redo = useCallback(() => dispatch({ type: 'redo' }), [])

  return {
    document: state.present,
    canUndo: state.past.length > 0,
    canRedo: state.future.length > 0,
    commit,
    replace,
    undo,
    redo,
  }
}
