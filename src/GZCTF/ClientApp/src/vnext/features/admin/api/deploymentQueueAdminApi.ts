import {
  contractFailure,
  isOptionalBoolean,
  isNullableNumber,
  isNullableString,
  isNumber,
  isOptionalNumber,
  isOptionalString,
  isRecord,
  isString,
} from './contractParsers'
import type { DeploymentQueueContract, DeploymentTask, DeploymentTaskDetail, DeploymentTaskPage } from './contracts'
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

function parseDetail(value: unknown): DeploymentTaskDetail {
  const errorCategoryValid =
    value &&
    isRecord(value) &&
    (value.errorCategory === undefined ||
      value.errorCategory === null ||
      isString(value.errorCategory) ||
      isNumber(value.errorCategory))

  if (
    !isRecord(value) ||
    !isString(value.id) ||
    !isOptionalString(value.correlationId) ||
    !isOptionalString(value.targetNodeId) ||
    !isOptionalNumber(value.kind) ||
    !isOptionalNumber(value.type) ||
    !isOptionalNumber(value.operation) ||
    !isOptionalNumber(value.action) ||
    !isOptionalNumber(value.status) ||
    !isOptionalNumber(value.stage) ||
    !isOptionalString(value.targetNodeName) ||
    !isOptionalString(value.targetNodeHost) ||
    !isOptionalString(value.resultHost) ||
    !isOptionalNumber(value.resultPort) ||
    !isOptionalString(value.subjectDisplayName) ||
    !isOptionalString(value.resourceDisplayName) ||
    !isNumber(value.createdAt) ||
    !isOptionalNumber(value.startedAt) ||
    !isOptionalNumber(value.completedAt) ||
    !isOptionalString(value.errorMessage) ||
    !errorCategoryValid ||
    !isOptionalString(value.errorCode) ||
    !isOptionalBoolean(value.retryable)
  ) {
    return contractFailure('Deployment task detail', value)
  }

  return {
    id: value.id,
    correlationId: value.correlationId ?? value.id,
    targetNodeId: value.targetNodeId ?? null,
    kind: value.kind ?? value.type ?? null,
    operation: value.operation ?? value.action ?? null,
    status: value.status ?? null,
    stage: value.stage ?? null,
    targetNodeName: value.targetNodeName ?? null,
    targetNodeHost: value.targetNodeHost ?? null,
    resultHost: value.resultHost ?? null,
    resultPort: value.resultPort ?? null,
    subjectDisplayName: value.subjectDisplayName ?? null,
    resourceDisplayName: value.resourceDisplayName ?? null,
    createdAt: value.createdAt,
    startedAt: value.startedAt ?? null,
    completedAt: value.completedAt ?? null,
    errorMessage: value.errorMessage ?? null,
    errorCategory: (value.errorCategory as string | number | null | undefined) ?? null,
    errorCode: value.errorCode ?? null,
    retryable: value.retryable ?? null,
  }
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
      let result: DeploymentTaskDetail | null = null
      await withResolvedRoute(async (contract) => {
        const value = await client.get(`${route(contract)}/${id}`)
        result = parseDetail(value)
      })
      if (!result) return contractFailure('Deployment task detail', null)
      return result as DeploymentTaskDetail
    },
    async cancel(id: string) {
      await withResolvedRoute((contract) => client.delete(`${route(contract)}/${id}`))
    },
  }
}

export const deploymentQueueAdminApi = createDeploymentQueueAdminApi()
