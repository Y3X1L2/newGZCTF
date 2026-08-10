import { Router } from 'lucide-react'
import { memo } from 'react'
import type { NodeProps } from '@xyflow/react'
import type { TeamLabFlowNode } from './nodeTypes'
import { TopologyNodeShell } from './TopologyNodeShell'

export const ManagedRouterNode = memo(function ManagedRouterNode({ data, selected }: NodeProps<TeamLabFlowNode>) {
  const node = data.topologyNode
  if (node.type !== 'router') return null
  return (
    <TopologyNodeShell
      details={[`${data.connectionCount} 个网段`, '双向路由']}
      eyebrow="托管路由器"
      icon={<Router size={18} />}
      readOnly={data.readOnly}
      selected={selected}
      title={node.name}
      tone="router"
    />
  )
})
