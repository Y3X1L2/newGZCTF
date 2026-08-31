import { runtimeJsonClient, type RuntimeJsonClient } from '../../api/runtimeJsonClient'
import type {
  RegisterTeamLabConnectorRequest,
  RegisterTeamLabDevicePackageRequest,
  TeamLabConnectorHealth,
} from './teamlabResourcesContracts'
import {
  parseTeamLabConnector,
  parseTeamLabConnectorPage,
  parseTeamLabDevicePackage,
  parseTeamLabDevicePackagePage,
  parseTeamLabNodeCachePage,
} from './teamlabResourcesParsers'

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
