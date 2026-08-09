import type {
  TeamLabAssetResources,
  TeamLabBootstrapReference,
  TeamLabConnectionDirection,
  TeamLabDependencyCondition,
  TeamLabEndpointObservationMode,
  TeamLabHealthCheck,
  TeamLabObservationPolicy,
} from '../api/teamlabContracts'

export interface TopologyPosition {
  x: number
  y: number
  width: number | null
  height: number | null
  collapsed: boolean
}

export interface TopologySwitchNode {
  type: 'switch'
  implicit?: boolean
  key: string
  name: string
  networkName: string
  position: TopologyPosition
  networkKey: string
  poolCidr: string
  runtimePrefixLength: number
  isEntry: boolean
  orderIndex: number
}

export interface TopologyRouterNode {
  type: 'router'
  key: string
  name: string
  position: TopologyPosition
}

interface TopologyAssetNodeBase {
  key: string
  name: string
  position: TopologyPosition
  imageTemplateId: number
  resources: TeamLabAssetResources
  routingEnabled: boolean
  exposePort: number | null
  environment: Readonly<Record<string, string>> | null
  startCommand: string | null
  healthCheck: TeamLabHealthCheck | null
  orderIndex: number
  stateless: boolean
  bootstrap: TeamLabBootstrapReference | null
  endpointObservation: TeamLabEndpointObservationMode
  bakeAtPublish: boolean
  imageDigest: string | null
}

export interface TopologyDockerNode extends TopologyAssetNodeBase {
  type: 'docker'
}

export interface TopologyLinuxVmNode extends TopologyAssetNodeBase {
  type: 'linux-vm'
}

export interface TopologyWindowsVmNode extends TopologyAssetNodeBase {
  type: 'windows-vm'
}

export type TopologyAssetNode = TopologyDockerNode | TopologyLinuxVmNode | TopologyWindowsVmNode
export type TopologyNode = TopologySwitchNode | TopologyRouterNode | TopologyAssetNode
export type TopologyNodeType = TopologyNode['type']

export interface TopologyMembershipConnection {
  type: 'membership'
  key: string
  interfaceKey?: string
  nodeKey: string
  switchKey: string
  hostOffset: number
  primary: boolean
  orderIndex: number
}

export interface TopologyRouteConnection {
  type: 'route'
  key: string
  fromSwitchKey: string
  toSwitchKey: string
  viaNodeKey: string
  direction: TeamLabConnectionDirection
}

export interface TopologyDependencyConnection {
  type: 'dependency'
  key: string
  assetKey: string
  dependsOnKey: string
  condition: TeamLabDependencyCondition
}

export type TopologyConnection = TopologyMembershipConnection | TopologyRouteConnection | TopologyDependencyConnection

export interface TopologyDocument {
  schemaVersion: 2
  name: string
  nodes: Readonly<Record<string, TopologyNode>>
  connections: Readonly<Record<string, TopologyConnection>>
  observation: TeamLabObservationPolicy
}

export const defaultTopologyPosition = (): TopologyPosition => ({
  x: 0,
  y: 0,
  width: null,
  height: null,
  collapsed: false,
})

export const createEmptyTopologyDocument = (name: string): TopologyDocument => ({
  schemaVersion: 2,
  name,
  nodes: {},
  connections: {},
  observation: {
    flowMetadataEnabled: true,
    onDemandPcapEnabled: true,
    endpointObservation: 'optional',
  },
})

export function isTopologyAsset(node: TopologyNode): node is TopologyAssetNode {
  return node.type === 'docker' || node.type === 'linux-vm' || node.type === 'windows-vm'
}

export function isTopologyInfrastructure(node: TopologyNode): node is TopologySwitchNode | TopologyRouterNode {
  return node.type === 'switch' || node.type === 'router'
}
