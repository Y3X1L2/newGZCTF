import { useCallback, useEffect, useState } from 'react'
import {
  emptyTopologySelection,
  normalizeTopologySelection,
  type TopologySelection,
} from '../../model/topologySelection'
import type { TopologyDocument } from '../../model/topologyDocument'

export function useEditorSelection(document: TopologyDocument) {
  const [selection, setSelection] = useState<TopologySelection>(emptyTopologySelection)

  useEffect(() => {
    setSelection((current) => normalizeTopologySelection(document, current))
  }, [document])

  const select = useCallback((nodeKeys: Iterable<string>, connectionKeys: Iterable<string>) => {
    setSelection({ nodeKeys: new Set(nodeKeys), connectionKeys: new Set(connectionKeys) })
  }, [])
  const clear = useCallback(() => setSelection(emptyTopologySelection()), [])

  return { selection, setSelection, select, clear }
}
