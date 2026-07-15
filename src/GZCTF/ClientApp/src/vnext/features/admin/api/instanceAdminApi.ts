import { contractFailure, isNumber, isRecord } from './contractParsers'
import type { LegacyContainerInstance, LegacyContainerInstancePage } from './contracts'
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

export function createInstanceAdminApi(client: RuntimeJsonClient = runtimeJsonClient) {
  return {
    async listContainers() {
      return parseLegacyContainers(await client.get('/api/admin/instances'))
    },
    async destroyContainer(containerGuid: string) {
      await client.delete(`/api/admin/instances/${containerGuid}`)
    },
  }
}

export const instanceAdminApi = createInstanceAdminApi()
