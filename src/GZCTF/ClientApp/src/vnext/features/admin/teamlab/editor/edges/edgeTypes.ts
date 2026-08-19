import type { Edge } from '@xyflow/react'
import type { TopologyConnection } from '../../model/topologyDocument'

/**
 * Visual weight of a link. Resolved at projection time from the connection kind
 * so an edge component never re-inspects the domain model to pick a style.
 */
export type TeamLabEdgeTone = 'membership' | 'route' | 'dependency' | 'traffic'

export interface TeamLabEdgeData extends Record<string, unknown> {
  connection: TopologyConnection | null
  label: string
  tone?: TeamLabEdgeTone
}

export type TeamLabFlowEdge = Edge<TeamLabEdgeData, 'network' | 'dependency' | 'traffic'>
