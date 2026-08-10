import { runtimeJsonClient, type RuntimeJsonClient } from '../../api/runtimeJsonClient'
import type {
  CreateTeamLabTopologyRequest,
  PublishTeamLabTopologyRequest,
  UpdateTeamLabTopologyRequest,
} from './teamlabContracts'
import {
  parseTeamLabAdminScenePage,
  parseTeamLabAdminRuntimePage,
  parseTeamLabCapabilities,
  parseTeamLabPlan,
  parseTeamLabRelease,
  parseTeamLabReleaseList,
  parseTeamLabReleaseReadiness,
  parseTeamLabTopologyDetail,
  parseTeamLabValidation,
  serializeTeamLabWriteRequest,
} from './teamlabParsers'

const root = '/api/admin/teamlab'

export interface TeamLabSceneListQuery {
  search?: string
  owner?: string
  ownerId?: string
  status?: string
  cursor?: string
  limit?: number
}

export const teamLabAdminKeys = {
  capabilities: ['vnext:admin:teamlab:capabilities'] as const,
  topologies: ['vnext:admin:teamlab:topologies'] as const,
  topology: (topologyId: string) => ['vnext:admin:teamlab:topology', topologyId] as const,
  releases: (topologyId: string) => ['vnext:admin:teamlab:releases', topologyId] as const,
  runtimes: (topologyId: string) => ['vnext:admin:teamlab:runtimes', topologyId] as const,
  plan: (topologyId: string, releaseId: string) => ['vnext:admin:teamlab:plan', topologyId, releaseId] as const,
}

export function createTeamLabAdminApi(client: RuntimeJsonClient = runtimeJsonClient) {
  return {
    async capabilities() {
      return parseTeamLabCapabilities(await client.get(`${root}/capabilities`))
    },

    async listTopologies(query: TeamLabSceneListQuery = {}) {
      return parseTeamLabAdminScenePage(
        await client.get(`${root}/topologies`, {
          search: query.search,
          owner: query.owner,
          ownerId: query.ownerId,
          status: query.status,
          after: query.cursor,
          limit: query.limit ?? 30,
        })
      )
    },

    async createTopology(request: CreateTeamLabTopologyRequest) {
      return parseTeamLabTopologyDetail(
        await client.postJson(`${root}/topologies`, serializeTeamLabWriteRequest(request))
      )
    },

    async getTopology(topologyId: string) {
      return parseTeamLabTopologyDetail(await client.get(`${root}/topologies/${topologyId}`))
    },

    async updateTopology(topologyId: string, request: UpdateTeamLabTopologyRequest) {
      return parseTeamLabTopologyDetail(
        await client.putJson(`${root}/topologies/${topologyId}`, serializeTeamLabWriteRequest(request))
      )
    },

    async deleteTopology(topologyId: string) {
      await client.delete(`${root}/topologies/${topologyId}`)
    },

    async validateTopology(topologyId: string) {
      return parseTeamLabValidation(await client.postJson(`${root}/topologies/${topologyId}/validate`))
    },

    async publishTopology(topologyId: string, request: PublishTeamLabTopologyRequest) {
      return parseTeamLabRelease(await client.postJson(`${root}/topologies/${topologyId}/releases`, request))
    },

    async listReleases(topologyId: string) {
      return parseTeamLabReleaseList(await client.get(`${root}/topologies/${topologyId}/releases`))
    },

    async listTrialRuntimes(topologyId: string, cursor?: string, limit = 30) {
      return parseTeamLabAdminRuntimePage(
        await client.get(`${root}/runtimes`, { topologyId, after: cursor, limit })
      )
    },

    async planRelease(topologyId: string, releaseId: string) {
      return parseTeamLabPlan(await client.postJson(`${root}/topologies/${topologyId}/releases/${releaseId}/plan`))
    },

    async releaseReadiness(topologyId: string, releaseId: string) {
      return parseTeamLabReleaseReadiness(
        await client.get(`${root}/topologies/${topologyId}/releases/${releaseId}/readiness`)
      )
    },

    async prepareReleaseImages(topologyId: string, releaseId: string) {
      return parseTeamLabReleaseReadiness(
        await client.postJson(`${root}/topologies/${topologyId}/releases/${releaseId}/images/prepare`)
      )
    },

  }
}

export const teamLabAdminApi = createTeamLabAdminApi()
