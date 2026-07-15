import {
  contractFailure,
  isNullableNumber,
  isNullableString,
  isNumber,
  isOptionalNumber,
  isOptionalString,
  isRecord,
  isString,
} from './contractParsers'
import type { DeploymentQueueContract, DeploymentTask, DeploymentTaskPage } from './contracts'
import {
  isUnavailableEndpoint,
  runtimeJsonClient,
  type RuntimeJsonClient,
  type RuntimeQuery,
} from './runtimeJsonClient'

export interface DeploymentTaskQuery {
  status?: string
  page?: number
  pageSize?: number
  cursor?: string | null
}

function isDeploymentTask(value: unknown): value is DeploymentTask {
  return (
    isRecord(value) &&
    isString(value.id) &&
    isOptionalString(value.correlationId) &&
    isOptionalString(value.ticketId) &&
    isOptionalString(value.targetId) &&
    isOptionalNumber(value.kind) &&
    isOptionalNumber(value.type) &&
    isOptionalNumber(value.action) &&
    isOptionalNumber(value.operation) &&
    isOptionalNumber(value.stage) &&
    isString(value.actionLabel) &&
    isString(value.typeLabel) &&
    isString(value.requestLabel) &&
    isNullableString(value.ownerLabel) &&
    isNullableString(value.gameLabel) &&
    isNullableString(value.challengeLabel) &&
    isNullableString(value.image) &&
    isNullableString(value.targetNodeId) &&
    isNullableString(value.targetNodeName) &&
    isNullableString(value.targetNodeHost) &&
    isString(value.targetNodeLabel) &&
    isString(value.statusLabel) &&
    isString(value.statusKey) &&
    isNumber(value.status) &&
    isNumber(value.dockerSlots) &&
    isNumber(value.vmSlots) &&
    isNumber(value.queuePosition) &&
    isNumber(value.peopleAhead) &&
    isOptionalString(value.result) &&
    isOptionalString(value.stageMessage) &&
    isOptionalString(value.blockedReasonCode) &&
    isNullableString(value.errorMessage) &&
    isNumber(value.createdAt) &&
    isNullableNumber(value.startedAt) &&
    isNullableNumber(value.completedAt)
  )
}

function parseLegacyPage(value: unknown): DeploymentTaskPage {
  if (
    !isRecord(value) ||
    !isNumber(value.total) ||
    !isNumber(value.page) ||
    !isNumber(value.pageSize) ||
    !Array.isArray(value.items) ||
    !value.items.every(isDeploymentTask)
  ) {
    return contractFailure('Deployment target list', value)
  }
  return {
    contract: 'deployment-targets',
    items: value.items,
    total: value.total,
    page: value.page,
    pageSize: value.pageSize,
    nextCursor: null,
  }
}

function parseCurrentPage(value: unknown, pageSize: number): DeploymentTaskPage {
  if (
    !isRecord(value) ||
    !Array.isArray(value.items) ||
    !value.items.every(isDeploymentTask) ||
    !isNullableString(value.nextCursor)
  ) {
    return contractFailure('Deployment queue list', value)
  }
  return {
    contract: 'deployment-queue',
    items: value.items,
    total: null,
    page: null,
    pageSize,
    nextCursor: value.nextCursor,
  }
}

function route(contract: DeploymentQueueContract) {
  return contract === 'deployment-targets' ? '/api/v1/deployment-targets' : '/api/v1/deployment-queue'
}

export function createDeploymentQueueAdminApi(client: RuntimeJsonClient = runtimeJsonClient) {
  let resolvedContract: DeploymentQueueContract | null = null

  const candidates = () => {
    if (resolvedContract) {
      return [
        resolvedContract,
        resolvedContract === 'deployment-targets' ? 'deployment-queue' : 'deployment-targets',
      ] as const
    }
    return ['deployment-targets', 'deployment-queue'] as const
  }

  async function list(query: DeploymentTaskQuery = {}) {
    const pageSize = query.pageSize ?? 20
    let unavailable: unknown

    for (const contract of candidates()) {
      const params: RuntimeQuery =
        contract === 'deployment-targets'
          ? { status: query.status, page: query.page ?? 1, pageSize }
          : { status: query.status, cursor: query.cursor, pageSize }
      try {
        const value = await client.get(route(contract), params)
        const page = contract === 'deployment-targets' ? parseLegacyPage(value) : parseCurrentPage(value, pageSize)
        resolvedContract = contract
        return page
      } catch (error) {
        if (!isUnavailableEndpoint(error)) throw error
        unavailable = error
      }
    }

    throw unavailable
  }

  async function withResolvedRoute(action: (contract: DeploymentQueueContract) => Promise<void>) {
    let unavailable: unknown
    for (const contract of candidates()) {
      try {
        await action(contract)
        resolvedContract = contract
        return
      } catch (error) {
        if (!isUnavailableEndpoint(error)) throw error
        unavailable = error
      }
    }
    throw unavailable
  }

  return {
    list,
    get contract() {
      return resolvedContract
    },
    async detail(id: string) {
      let result: Record<string, unknown> | null = null
      await withResolvedRoute(async (contract) => {
        const value = await client.get(`${route(contract)}/${id}`)
        if (!isRecord(value) || !isString(value.id)) return contractFailure('Deployment task detail', value)
        result = value
      })
      return result
    },
    async cancel(id: string) {
      await withResolvedRoute((contract) => client.delete(`${route(contract)}/${id}`))
    },
  }
}

export const deploymentQueueAdminApi = createDeploymentQueueAdminApi()
