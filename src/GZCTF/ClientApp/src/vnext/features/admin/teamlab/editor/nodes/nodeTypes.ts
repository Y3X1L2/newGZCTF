import type { Node } from '@xyflow/react'
import type { TopologyNode, TopologyNodeType } from '../../model/topologyDocument'

export interface TeamLabNodeData extends Record<string, unknown> {
  topologyNode: TopologyNode
  connectionCount: number
  readOnly: boolean
}

export type TeamLabFlowNode = Node<TeamLabNodeData, TopologyNodeType>
