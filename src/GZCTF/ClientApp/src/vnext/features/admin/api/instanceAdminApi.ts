import { externalEntryHref } from '../../../shared/urls'
import { boundedMap } from '../shared/boundedMap'
import { contractFailure, isNumber, isRecord } from './contractParsers'
import type {
  GlobalInstanceInventory,
  GlobalInstanceItem,
  LegacyContainerInstance,
  LegacyContainerInstancePage,
  NodeResourceItem,
  NodeResourcePage,
  NodeSummary,
} from './contracts'
import { nodeAdminApi } from './nodeAdminApi'
import { runtimeJsonClient, type RuntimeJsonClient } from './runtimeJsonClient'

function isLegacyContainer(value: unknown): value is LegacyContainerInstance {
  return isRecord(value)
}

function parseLegacyContainers(value: unknown): LegacyContainerInstancePage {
  if (
    !isRecord(value) ||
    !Array.isArray(value.data) ||
    !value.data.every(isLegacyContainer) ||
    !isNumber(value.length) ||
    (value.total !== undefined && !isNumber(value.total))
  ) {
    return contractFailure('Container instance list', value)
  }
  return {
    data: value.data,
    length: value.length,
    total: typeof value.total === 'number' ? value.total : value.length,
  }
}

type InstanceNodeResourceApi = Pick<typeof nodeAdminApi, 'resources'>

export function createInstanceAdminApi(
  client: RuntimeJsonClient = runtimeJsonClient,
  resourceApi: InstanceNodeResourceApi = nodeAdminApi
) {
  async function loadNodeResources(node: NodeSummary, status: 'active' | 'history') {
    const first = await resourceApi.resources(node.id, { status, page: 1, pageSize: 50 })
    const pages = Math.max(1, Math.ceil(first.total / first.pageSize))
    const additional: NodeResourcePage[] = []
    for (let page = 2; page <= pages; page += 1) {
      additional.push(await resourceApi.resources(node.id, { status, page, pageSize: 50 }))
    }
    return [first, ...additional].flatMap((result) =>
      result.items.map<GlobalInstanceItem>((item) => ({
        ...item,
        entry: externalEntryHref(item.entry),
        nodeId: node.id,
        nodeName: node.name,
      }))
    )
  }

  async function legacyInventory(): Promise<GlobalInstanceInventory> {
    const page = parseLegacyContainers(await client.get('/api/admin/instances'))
    const items = page.data.flatMap<GlobalInstanceItem>((container, index) => {
      const id = container.containerGuid || container.containerId
      if (!id) return []
      const startedAt = container.startedAt ?? Date.now()
      return [
        {
          nodeId: 'legacy',
          nodeName: '传统容器接口',
          kind: 'container',
          id,
          name: container.challenge?.title || `比赛容器 ${index + 1}`,
          status: 'Running',
          isActive: true,
          startedAt,
          expectedStopAt: container.expectStopAt ?? null,
          stoppedAt: null,
          duration: '',
          image: container.image ?? null,
          runtimeId: container.containerId ?? null,
          entry: container.ip && container.port ? externalEntryHref(`${container.ip}:${container.port}`) : null,
          ip: container.ip ?? null,
          port: container.port ?? null,
          gameId: null,
          gameTitle: null,
          challengeId: container.challenge?.id ?? null,
          challengeTitle: container.challenge?.title ?? null,
          challengeCategory: container.challenge?.category ?? null,
          teamId: container.team?.id ?? null,
          teamName: container.team?.name ?? null,
          userId: null,
          userName: null,
          providerName: 'legacy',
          osType: null,
        },
      ]
    })

    return {
      source: 'legacy-containers',
      items: items.sort((left, right) => right.startedAt - left.startedAt),
      totalNodes: 0,
      loadedNodes: 0,
      failures: [],
      collectedAt: Date.now(),
    }
  }

  return {
    async listContainers() {
      return parseLegacyContainers(await client.get('/api/admin/instances'))
    },
    async destroyContainer(containerGuid: string) {
      await client.delete(`/api/admin/instances/${containerGuid}`)
    },
    async destroy(resource: NodeResourceItem) {
      if (resource.kind === 'container') {
        await client.delete(`/api/admin/instances/${resource.id}`)
        return
      }
      if (resource.kind === 'vm') {
        await nodeAdminApi.destroyVm(resource.id)
        return
      }
      throw new Error('该资源类型暂不支持从全域实例页销毁。')
    },
    legacyInventory,
    async inventory(nodes: NodeSummary[], status: 'active' | 'history', nodeId?: string) {
      const selectedNodes = nodeId ? nodes.filter((node) => node.id === nodeId) : nodes
      const results = await boundedMap(selectedNodes, 4, async (node) => {
        try {
          return { node, items: await loadNodeResources(node, status), error: null }
        } catch (error) {
          return { node, items: [] as GlobalInstanceItem[], error }
        }
      })
      const failures = results
        .filter((result) => result.error)
        .map((result) => ({
          nodeId: result.node.id,
          nodeName: result.node.name,
          message: result.error instanceof Error ? result.error.message : '节点资源读取失败。',
        }))
      const loadedNodes = selectedNodes.length - failures.length

      if (status === 'active' && selectedNodes.length > 0 && loadedNodes === 0) return legacyInventory()

      return {
        source: 'node-resources' as const,
        items: results.flatMap((result) => result.items).sort((left, right) => right.startedAt - left.startedAt),
        totalNodes: selectedNodes.length,
        loadedNodes,
        failures,
        collectedAt: Date.now(),
      }
    },
  }
}

export const instanceAdminApi = createInstanceAdminApi()
