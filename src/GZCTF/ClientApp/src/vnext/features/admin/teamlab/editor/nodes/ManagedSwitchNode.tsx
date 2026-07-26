import { Network } from 'lucide-react'
import { memo } from 'react'
import type { NodeProps } from '@xyflow/react'
import type { TeamLabFlowNode } from './nodeTypes'
import { TopologyNodeShell } from './TopologyNodeShell'

export const ManagedSwitchNode = memo(function ManagedSwitchNode({ data, selected }: NodeProps<TeamLabFlowNode>) {
  const node = data.topologyNode
  if (node.type !== 'switch') return null
  return (
    <TopologyNodeShell
      badge={node.isEntry ? '入口' : undefined}
      details={[node.poolCidr, `${data.connectionCount} 端口`]}
      eyebrow="Managed switch"
      icon={<Network size={18} />}
      readOnly={data.readOnly}
      selected={selected}
      title={node.name}
      tone="switch"
    />
  )
})
