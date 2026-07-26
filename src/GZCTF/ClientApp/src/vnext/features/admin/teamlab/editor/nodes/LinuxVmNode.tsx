import { MonitorCog } from 'lucide-react'
import { memo } from 'react'
import type { NodeProps } from '@xyflow/react'
import type { TeamLabFlowNode } from './nodeTypes'
import { TopologyNodeShell } from './TopologyNodeShell'

export const LinuxVmNode = memo(function LinuxVmNode({ data, selected }: NodeProps<TeamLabFlowNode>) {
  const node = data.topologyNode
  if (node.type !== 'linux-vm') return null
  return (
    <TopologyNodeShell
      details={[`${node.resources.cpuUnits} vCPU`, `${node.resources.memoryMiB} MiB`, `${data.connectionCount} NIC`]}
      eyebrow="Linux VM"
      icon={<MonitorCog size={18} />}
      readOnly={data.readOnly}
      selected={selected}
      title={node.name}
      tone="linux"
    />
  )
})
