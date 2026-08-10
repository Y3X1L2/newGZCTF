import { Monitor } from 'lucide-react'
import { memo } from 'react'
import type { NodeProps } from '@xyflow/react'
import type { TeamLabFlowNode } from './nodeTypes'
import { TopologyNodeShell } from './TopologyNodeShell'

export const WindowsVmNode = memo(function WindowsVmNode({ data, selected }: NodeProps<TeamLabFlowNode>) {
  const node = data.topologyNode
  if (node.type !== 'windows-vm') return null
  return (
    <TopologyNodeShell
      details={[`${node.resources.cpuUnits} vCPU`, `${node.resources.memoryMiB} MiB`, `${data.connectionCount} 张网卡`]}
      eyebrow="Windows 虚拟机"
      icon={<Monitor size={18} />}
      readOnly={data.readOnly}
      selected={selected}
      title={node.name}
      tone="windows"
    />
  )
})
