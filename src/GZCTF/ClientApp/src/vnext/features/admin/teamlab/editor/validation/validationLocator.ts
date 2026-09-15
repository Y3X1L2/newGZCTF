import type { TeamLabValidationIssue } from '../../api'
import { compileTopologyDocument } from '../../model/topologyCompiler'
import type { TopologyDocument } from '../../model/topologyDocument'

export interface ValidationLocation {
  nodeKey: string | null
  connectionKey: string | null
  field: string | null
}

const indexedPath = /^(networks|assets|infrastructure|connections)\[(\d+)](?:\.(.+))?$/

export function locateValidationIssue(
  document: TopologyDocument,
  issue: Pick<TeamLabValidationIssue, 'path'>
): ValidationLocation {
  const match = indexedPath.exec(issue.path)
  if (!match) return { nodeKey: null, connectionKey: null, field: issue.path || null }
  const [, collection, rawIndex, field = null] = match
  const index = Number(rawIndex)
  const compiled = compileTopologyDocument(document)

  if (collection === 'networks') {
    const network = compiled.networks[index]
    const node = Object.values(document.nodes).find(
      (candidate) => candidate.type === 'switch' && candidate.networkKey === network?.key
    )
    return { nodeKey: node?.key ?? null, connectionKey: null, field }
  }
  if (collection === 'assets') {
    return { nodeKey: compiled.assets[index]?.key ?? null, connectionKey: null, field }
  }
  if (collection === 'infrastructure') {
    return { nodeKey: compiled.infrastructure[index]?.key ?? null, connectionKey: null, field }
  }
  return { nodeKey: null, connectionKey: compiled.connections[index]?.key ?? null, field }
}
