import { RuntimeApiError, runtimeJsonClient, type RuntimeJsonClient } from '../../api/runtimeJsonClient'
import { parseAdminLogPage } from '../../api/adminLogApi'
import type {
  CreateTeamLabCaptureRequest,
  CreateTeamLabTrialRequest,
  ResetTeamLabRuntimeRequest,
} from './teamlabRuntimeContracts'
import {
  parseTeamLabAccessGrant,
  parseTeamLabAccessGrants,
  parseTeamLabCapture,
  parseTeamLabRuntime,
  parseTeamLabRuntimeEvents,
  parseTeamLabTrafficFlowPage,
  parseTeamLabTrafficPath,
  parseTeamLabTrafficPathPage,
} from './teamlabRuntimeParsers'

const root = '/api/admin/teamlab/runtimes'

interface RuntimeJsonClientWithHeaders extends RuntimeJsonClient {
  postJsonWithHeaders(path: string, body: unknown, headers: Readonly<Record<string, string>>): Promise<unknown>
}

interface RuntimeJsonClientWithDeleteResponse extends RuntimeJsonClient {
  deleteJson(path: string): Promise<unknown>
}

function supportsRequestHeaders(client: RuntimeJsonClient): client is RuntimeJsonClientWithHeaders {
  return 'postJsonWithHeaders' in client && typeof client.postJsonWithHeaders === 'function'
}

function supportsDeleteResponse(client: RuntimeJsonClient): client is RuntimeJsonClientWithDeleteResponse {
  return 'deleteJson' in client && typeof client.deleteJson === 'function'
}

export const teamLabRuntimeKeys = {
  runtime: (runtimeId: string) => ['vnext:admin:teamlab:runtime', runtimeId] as const,
  events: (runtimeId: string) => ['vnext:admin:teamlab:runtime-events', runtimeId] as const,
  accessGrants: (runtimeId: string) => ['vnext:admin:teamlab:runtime-access-grants', runtimeId] as const,
  flows: (runtimeId: string) => ['vnext:admin:teamlab:runtime-flows', runtimeId] as const,
  paths: (runtimeId: string) => ['vnext:admin:teamlab:runtime-paths', runtimeId] as const,
  path: (runtimeId: string, pathId: string) =>
    ['vnext:admin:teamlab:runtime-path', runtimeId, pathId] as const,
  logs: (runtimeId: string) => ['vnext:admin:teamlab:runtime-logs', runtimeId] as const,
  capture: (runtimeId: string, captureId: string) =>
    ['vnext:admin:teamlab:runtime-capture', runtimeId, captureId] as const,
}

export function createTeamLabRuntimeApi(client: RuntimeJsonClient = runtimeJsonClient) {
  return {
    async createTrial(idempotencyKey: string, request: CreateTeamLabTrialRequest) {
      if (!supportsRequestHeaders(client)) {
        throw new RuntimeApiError(
          'The runtime transport cannot send the Idempotency-Key header required for trial creation.',
          { kind: 'contract', code: 'request_headers_unsupported' }
        )
      }
      return parseTeamLabRuntime(
        await client.postJsonWithHeaders(`${root}/trials`, request, { 'Idempotency-Key': idempotencyKey })
      )
    },

    async getRuntime(runtimeId: string) {
      return parseTeamLabRuntime(await client.get(`${root}/${runtimeId}`))
    },

    async listEvents(runtimeId: string, after = 0, limit = 100) {
      return parseTeamLabRuntimeEvents(await client.get(`${root}/${runtimeId}/events`, { after, limit }))
    },

    async listLogs(runtimeId: string, cursor?: string | null, limit = 100) {
      return parseAdminLogPage(await client.get(`${root}/${runtimeId}/logs`, { cursor, count: limit }))
    },

    async resetRuntime(runtimeId: string, request: ResetTeamLabRuntimeRequest) {
      return parseTeamLabRuntime(await client.postJson(`${root}/${runtimeId}/reset`, request))
    },

    async destroyRuntime(runtimeId: string) {
      if (!supportsDeleteResponse(client)) {
        throw new RuntimeApiError('The runtime transport cannot parse the destroy response.', {
          kind: 'contract',
          code: 'delete_response_unsupported',
        })
      }
      return parseTeamLabRuntime(await client.deleteJson(`${root}/${runtimeId}`))
    },

    async createAccessGrant(runtimeId: string) {
      return parseTeamLabAccessGrant(
        await client.postJson(`${root}/${runtimeId}/access-grants`, { type: 'WireGuard' })
      )
    },

    async listAccessGrants(runtimeId: string) {
      return parseTeamLabAccessGrants(await client.get(`${root}/${runtimeId}/access-grants`))
    },

    async revokeAccessGrant(runtimeId: string, grantId: string) {
      await client.delete(`${root}/${runtimeId}/access-grants/${grantId}`)
    },

    accessGrantDownloadPath(runtimeId: string, grantId: string, token: string) {
      return `${root}/${runtimeId}/access-grants/${grantId}/download?token=${encodeURIComponent(token)}`
    },

    async listFlows(runtimeId: string, after?: string, limit = 100) {
      return parseTeamLabTrafficFlowPage(
        await client.get(`${root}/${runtimeId}/traffic/flows`, { after, limit })
      )
    },

    async listPaths(runtimeId: string, after?: string, limit = 100) {
      return parseTeamLabTrafficPathPage(
        await client.get(`${root}/${runtimeId}/traffic/paths`, { after, limit })
      )
    },

    async getPath(runtimeId: string, pathId: string) {
      return parseTeamLabTrafficPath(await client.get(`${root}/${runtimeId}/traffic/paths/${pathId}`))
    },

    async startCapture(runtimeId: string, request: CreateTeamLabCaptureRequest) {
      return parseTeamLabCapture(await client.postJson(`${root}/${runtimeId}/captures`, request))
    },

    async getCapture(runtimeId: string, captureId: string) {
      return parseTeamLabCapture(await client.get(`${root}/${runtimeId}/captures/${captureId}`))
    },

    async stopCapture(runtimeId: string, captureId: string) {
      return parseTeamLabCapture(await client.postJson(`${root}/${runtimeId}/captures/${captureId}/stop`))
    },

    captureDownloadPath(runtimeId: string, captureId: string) {
      return `${root}/${runtimeId}/captures/${captureId}/download`
    },
  }
}

export const teamLabRuntimeApi = createTeamLabRuntimeApi()
