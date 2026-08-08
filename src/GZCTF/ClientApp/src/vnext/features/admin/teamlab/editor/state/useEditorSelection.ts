import { useCallback, useEffect, useState } from 'react'
import {
  emptyTopologySelection,
  normalizeTopologySelection,
  type TopologySelection,
} from '../../model/topologySelection'
import type { TopologyDocument } from '../../model/topologyDocument'

function sameKeys(left: ReadonlySet<string>, right: ReadonlySet<string>) {
  return left.size === right.size && [...left].every((key) => right.has(key))
}

export function useEditorSelection(document: TopologyDocument) {
  const [selection, setSelection] = useState<TopologySelection>(emptyTopologySelection)

  useEffect(() => {
    setSelection((current) => normalizeTopologySelection(document, current))
  }, [document])

  const select = useCallback((nodeKeys: Iterable<string>, connectionKeys: Iterable<string>) => {
    const nextNodes = new Set(nodeKeys)
    const nextConnections = new Set(connectionKeys)
    setSelection((current) =>
      sameKeys(current.nodeKeys, nextNodes) && sameKeys(current.connectionKeys, nextConnections)
        ? current
        : { nodeKeys: nextNodes, connectionKeys: nextConnections }
    )
  }, [])
  const clear = useCallback(() => {
    setSelection((current) =>
      current.nodeKeys.size === 0 && current.connectionKeys.size === 0 ? current : emptyTopologySelection()
    )
  }, [])

  return { selection, setSelection, select, clear }
}
