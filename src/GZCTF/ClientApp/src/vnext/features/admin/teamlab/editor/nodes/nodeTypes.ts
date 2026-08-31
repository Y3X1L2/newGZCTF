import type { Node } from '@xyflow/react'
import type { TopologyNode, TopologyNodeType } from '../../model/topologyDocument'

export interface TeamLabNodeData extends Record<string, unknown> {
  topologyNode: TopologyNode
  connectionCount: number
  readOnly: boolean
  /**
   * True for a device that spans more than one network (a router, or a
   * dual-homed asset). Border devices are laid out between region containers
   * rather than inside one, so they are marked to explain why they sit outside.
   */
  isBorder?: boolean
}

export type TeamLabFlowNode = Node<TeamLabNodeData, TopologyNodeType>
