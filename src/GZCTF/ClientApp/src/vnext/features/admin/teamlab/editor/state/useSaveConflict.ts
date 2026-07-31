import { RuntimeApiError } from '../../../api/runtimeJsonClient'
import type { TopologyDocument } from '../../model/topologyDocument'

export interface TopologySaveConflict {
  localDocument: TopologyDocument
  expectedRevision: number
}

export function isTopologyRevisionConflict(error: unknown) {
  return error instanceof RuntimeApiError && error.status === 409 && error.code === 'topology_revision_conflict'
}

export function downloadTopologyDraft(conflict: TopologySaveConflict) {
  const blob = new Blob(
    [JSON.stringify({ revision: conflict.expectedRevision, document: conflict.localDocument }, null, 2)],
    { type: 'application/json' }
  )
  const url = URL.createObjectURL(blob)
  const anchor = document.createElement('a')
  anchor.href = url
  anchor.download = `teamlab-draft-revision-${conflict.expectedRevision}.json`
  anchor.click()
  URL.revokeObjectURL(url)
}
