import { useAdminImages } from '../images/useAdminImages'
import { useAdminInstances } from '../instances/useAdminInstances'
import { useAdminLogs } from '../logs/useAdminLogs'
import { useAdminNodes } from '../nodes/useAdminNodes'
import { useDeploymentQueue } from '../queue/useDeploymentQueue'
import { deriveDashboardMetrics } from './dashboardSelectors'

export function useAdminDashboard() {
  const nodes = useAdminNodes()
  const images = useAdminImages({})
  const queue = useDeploymentQueue({ page: 1, pageSize: 20, cursor: null })
  const instances = useAdminInstances(nodes.nodes, nodes.error, { status: 'active' })
  const logs = useAdminLogs({ level: 'Error', count: 50, offset: 0, cursor: null })
  const metrics = deriveDashboardMetrics(nodes.nodes, images.images, instances.inventory)

  const refresh = async () => {
    await Promise.all([nodes.mutate(), images.mutate(), queue.mutate(), instances.mutate(), logs.mutate()])
  }

  return { nodes, images, queue, instances, logs, metrics, refresh }
}
