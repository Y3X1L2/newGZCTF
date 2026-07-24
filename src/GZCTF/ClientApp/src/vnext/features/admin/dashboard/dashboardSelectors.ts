import { ImageStatus, NodeStatus } from '@Api'
import type { GlobalInstanceInventory, ImageTemplateSummary, NodeSummary } from '../api'

export function deriveDashboardMetrics(
  nodes: NodeSummary[] | undefined,
  images: ImageTemplateSummary[] | undefined,
  inventory: GlobalInstanceInventory | undefined
) {
  const nodeList = nodes ?? []
  return {
    onlineNodes: nodeList.filter((node) => node.status === NodeStatus.Online || node.status === NodeStatus.Busy).length,
    totalNodes: nodeList.length,
    schedulableNodes: nodeList.filter((node) => node.isSchedulable).length,
    dockerAvailable: nodeList.reduce(
      (total, node) => total + Math.max(0, node.maxContainers - node.allocatedContainers - node.reservedContainers),
      0
    ),
    vmAvailable: nodeList.reduce(
      (total, node) => total + Math.max(0, node.maxVms - node.allocatedVms - node.reservedVms),
      0
    ),
    activeInstances: inventory?.items.length ?? 0,
    instanceCoverage: inventory ? `${inventory.loadedNodes}/${inventory.totalNodes}` : '—',
    imageErrors: (images ?? []).filter((image) => image.status === ImageStatus.Error).length,
  }
}
