import type { NodeTypes } from '@xyflow/react'
import { NetworkRegionNodeView } from '../regions/NetworkRegionNode'
import { DockerNode } from './DockerNode'
import { LinuxVmNode } from './LinuxVmNode'
import { ManagedRouterNode } from './ManagedRouterNode'
import { ManagedSwitchNode } from './ManagedSwitchNode'
import { WindowsVmNode } from './WindowsVmNode'

export const teamLabNodeTypes = {
  region: NetworkRegionNodeView,
  switch: ManagedSwitchNode,
  router: ManagedRouterNode,
  docker: DockerNode,
  'linux-vm': LinuxVmNode,
  'windows-vm': WindowsVmNode,
} satisfies NodeTypes
