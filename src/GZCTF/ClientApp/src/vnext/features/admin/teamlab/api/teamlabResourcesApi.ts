import { runtimeJsonClient, type RuntimeJsonClient } from '../../api/runtimeJsonClient'
import type {
  RegisterTeamLabConnectorRequest,
  RegisterTeamLabDevicePackageRequest,
  TeamLabConnectorHealth,
  TeamLabHostInterface,
} from './teamlabResourcesContracts'
import {
  parseTeamLabConnector,
  parseTeamLabConnectorPage,
  parseTeamLabDevicePackage,
  parseTeamLabDevicePackagePage,
  parseTeamLabNodeCachePage,
} from './teamlabResourcesParsers'
import { teamLabParsing as parse } from './teamlabParsers'

function parseTeamLabHostInterfaces(value: unknown): readonly TeamLabHostInterface[] {
  return parse.array(value, '节点网卡', (entry, label) => {
    const item = parse.record(entry, label)
    return {
      name: parse.string(item.name, `${label}.name`),
      macAddress: parse.string(item.macAddress, `${label}.macAddress`),
      linkUp: parse.boolean(item.linkUp, `${label}.linkUp`),
      addresses: parse.array(item.addresses, `${label}.addresses`, parse.string),
    }
  })
}

const root = '/api/admin/teamlab'

export const teamLabResourceKeys = {
  devicePackages: (name: string | null) => ['vnext:admin:teamlab:device-packages', name ?? ''] as const,
  connectors: () => ['vnext:admin:teamlab:connectors'] as const,
  nodeCache: ['vnext:admin:teamlab:node-cache'] as const,
}

export function createTeamLabResourcesApi(client: RuntimeJsonClient = runtimeJsonClient) {
  return {
    async listDevicePackages(query: { name?: string; after?: string; limit?: number } = {}) {
      return parseTeamLabDevicePackagePage(
        await client.get(`${root}/device-packages`, {
          name: query.name || undefined,
          after: query.after || undefined,
          limit: query.limit ?? 50,
        })
      )
    },

    async getDevicePackage(packageId: string) {
      return parseTeamLabDevicePackage(await client.get(`${root}/device-packages/${packageId}`))
    },

    async registerDevicePackage(request: RegisterTeamLabDevicePackageRequest) {
      return parseTeamLabDevicePackage(await client.postJson(`${root}/device-packages`, request))
    },

    async setDevicePackageEnabled(packageId: string, enabled: boolean) {
      return parseTeamLabDevicePackage(
        await client.postJson(`${root}/device-packages/${packageId}/${enabled ? 'enable' : 'disable'}`, {})
      )
    },

    async archiveDevicePackage(packageId: string) {
      await client.postJson(`${root}/device-packages/${packageId}/archive`, {})
    },

    async listConnectors(query: { after?: string; limit?: number } = {}) {
      return parseTeamLabConnectorPage(
        await client.get(`${root}/connectors`, { after: query.after || undefined, limit: query.limit ?? 50 })
      )
    },

    async registerConnector(request: RegisterTeamLabConnectorRequest) {
      return parseTeamLabConnector(await client.postJson(`${root}/connectors`, request))
    },

    async listNodeInterfaces(nodeId: string) {
      return parseTeamLabHostInterfaces(
        await client.get(`${root}/connector-nodes/${encodeURIComponent(nodeId)}/interfaces`)
      )
    },

    async setConnectorHealth(connectorId: string, health: TeamLabConnectorHealth) {
      return parseTeamLabConnector(
        await client.postJson(`${root}/connectors/${connectorId}/health`, { health })
      )
    },

    async archiveConnector(connectorId: string) {
      await client.postJson(`${root}/connectors/${connectorId}/archive`, {})
    },

    async listNodeCache(query: { after?: string; limit?: number } = {}) {
      return parseTeamLabNodeCachePage(
        await client.get(`${root}/resource-pools/node-cache`, {
          after: query.after || undefined,
          limit: query.limit ?? 50,
        })
      )
    },
  }
}

export const teamLabResourcesApi = createTeamLabResourcesApi()
