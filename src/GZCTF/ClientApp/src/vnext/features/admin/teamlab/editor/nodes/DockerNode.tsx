import { Container } from 'lucide-react'
import { memo } from 'react'
import type { NodeProps } from '@xyflow/react'
import type { TeamLabFlowNode } from './nodeTypes'
import { TopologyNodeShell } from './TopologyNodeShell'

export const DockerNode = memo(function DockerNode({ data, selected }: NodeProps<TeamLabFlowNode>) {
  const node = data.topologyNode
  if (node.type !== 'docker') return null
  return (
    <TopologyNodeShell
      details={[`${node.resources.cpuUnits} CPU`, `${node.resources.memoryMiB} MiB`, `${data.connectionCount} NIC`]}
      eyebrow="Docker"
      icon={<Container size={18} />}
      readOnly={data.readOnly}
      selected={selected}
      title={node.name}
      tone="docker"
    />
  )
})
