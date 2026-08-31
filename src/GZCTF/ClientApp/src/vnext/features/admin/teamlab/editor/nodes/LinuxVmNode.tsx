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
      badge={data.isBorder ? '跨网段' : undefined}
      details={[`${node.resources.cpuUnits} vCPU`, `${node.resources.memoryMiB} MiB`, `${data.connectionCount} 张网卡`]}
      eyebrow="Linux 虚拟机"
      icon={<MonitorCog size={18} />}
      readOnly={data.readOnly}
      selected={selected}
      title={node.name}
      tone="linux"
    />
  )
})
