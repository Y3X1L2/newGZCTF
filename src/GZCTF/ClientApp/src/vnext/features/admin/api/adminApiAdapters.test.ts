import { describe, expect, it, vi } from 'vitest'
import { createAdminLogApi } from './adminLogApi'
import { createDeploymentQueueAdminApi } from './deploymentQueueAdminApi'
import { RuntimeApiError, type RuntimeJsonClient } from './runtimeJsonClient'

function createClient(overrides: Partial<RuntimeJsonClient> = {}): RuntimeJsonClient {
  const unexpected = async () => {
    throw new Error('Unexpected API call')
  }
  return {
    get: unexpected,
    postJson: unexpected,
    postForm: unexpected,
    putJson: unexpected,
    patchJson: unexpected,
    delete: unexpected,
    ...overrides,
  }
}

function deploymentTask() {
  return {
    id: '6a79afc4-02ae-4d09-9bdd-33ac0aebbd34',
    ticketId: null,
    targetId: '6a79afc4-02ae-4d09-9bdd-33ac0aebbd34',
    kind: null,
    type: 0,
    action: 0,
    actionLabel: '创建',
    typeLabel: 'Docker',
    requestLabel: 'admin / CTF题库 / SSTI',
    ownerLabel: 'admin #1',
    gameLabel: 'CTF题库 #23',
    challengeLabel: 'SSTI #19',
    image: 'registry/ctf/ssti:v1',
    targetNodeId: '3da51163-2372-4e83-95c9-5d1fec771a7d',
    targetNodeName: 'worker-1',
    targetNodeHost: '10.24.0.30',
    targetNodeLabel: 'worker-1 (10.24.0.30)',
    statusLabel: '已完成',
    statusKey: 'completed',
    status: 3,
    dockerSlots: 1,
    vmSlots: 0,
    queuePosition: 0,
    peopleAhead: 0,
    result: '203.0.113.10:30000',
    errorMessage: null,
    createdAt: 1_784_081_794_959,
    startedAt: 1_784_081_794_959,
    completedAt: 1_784_081_795_470,
  }
}

describe('deploymentQueueAdminApi', () => {
  it('uses the deployed page-number deployment-targets contract', async () => {
    const get = vi.fn().mockResolvedValue({
      total: 449,
      page: 2,
      pageSize: 20,
      items: [deploymentTask()],
    })
    const adapter = createDeploymentQueueAdminApi(createClient({ get }))

    const result = await adapter.list({ status: 'completed', page: 2, pageSize: 20 })

    expect(result).toMatchObject({
      contract: 'deployment-targets',
      total: 449,
      page: 2,
      pageSize: 20,
      nextCursor: null,
    })
    expect(get).toHaveBeenCalledWith('/api/v1/deployment-targets', {
      status: 'completed',
      page: 2,
      pageSize: 20,
    })
  })

  it('falls back to the newer cursor contract when the legacy route resolves to SPA HTML', async () => {
    const get = vi
      .fn()
      .mockRejectedValueOnce(
        new RuntimeApiError('Expected JSON', {
          kind: 'contract',
          status: 200,
          code: 'non_json_response',
        })
      )
      .mockResolvedValueOnce({
        items: [{ ...deploymentTask(), correlationId: deploymentTask().id, operation: 0, stage: 5 }],
        nextCursor: 'next-page',
      })
    const adapter = createDeploymentQueueAdminApi(createClient({ get }))

    const result = await adapter.list({ cursor: 'current-page', pageSize: 10 })

    expect(result).toMatchObject({
      contract: 'deployment-queue',
      total: null,
      page: null,
      pageSize: 10,
      nextCursor: 'next-page',
    })
    expect(get).toHaveBeenNthCalledWith(2, '/api/v1/deployment-queue', {
      status: undefined,
      cursor: 'current-page',
      pageSize: 10,
    })
  })

  it('parses the correlated Phase 7 task detail contract', async () => {
    const detail = {
      id: deploymentTask().id,
      correlationId: deploymentTask().id,
      targetNodeId: deploymentTask().targetNodeId,
      kind: 1,
      operation: 1,
      status: 5,
      stage: 18,
      targetNodeName: 'worker-1',
      targetNodeHost: '10.24.0.30',
      resultHost: null,
      resultPort: null,
      subjectDisplayName: 'admin',
      resourceDisplayName: 'SSTI',
      createdAt: 1_784_081_794_959,
      startedAt: 1_784_081_794_969,
      completedAt: 1_784_081_795_470,
      errorMessage: 'Image pull failed.',
      errorCategory: 2,
      errorCode: 'image_pull_failed',
      retryable: true,
    }
    const get = vi.fn().mockResolvedValue(detail)
    const adapter = createDeploymentQueueAdminApi(createClient({ get }))

    await expect(adapter.detail(detail.id)).resolves.toEqual(detail)
  })

  it('normalizes the deployed legacy task detail fields', async () => {
    const get = vi.fn().mockResolvedValue({
      id: deploymentTask().id,
      targetNodeId: deploymentTask().targetNodeId,
      type: 0,
      action: 2,
      status: 2,
      targetNodeName: 'worker-1',
      targetNodeHost: '10.24.0.30',
      resultHost: '203.0.113.10',
      resultPort: 30000,
      createdAt: 1_784_081_794_959,
      completedAt: 1_784_081_795_470,
      errorMessage: null,
    })
    const adapter = createDeploymentQueueAdminApi(createClient({ get }))

    await expect(adapter.detail(deploymentTask().id)).resolves.toMatchObject({
      kind: 0,
      operation: 2,
      stage: null,
      startedAt: null,
      resultHost: '203.0.113.10',
      resultPort: 30000,
    })
  })
})

describe('adminLogApi', () => {
  const entry = {
    time: 1_784_082_131_595,
    name: 'admin',
    level: 'Information',
    ip: '10.0.7.17',
    msg: 'Training check-in completed.',
    status: 'Success',
  }

  it('normalizes the deployed offset array response', async () => {
    const get = vi.fn().mockResolvedValue([entry])
    const adapter = createAdminLogApi(createClient({ get }))

    const result = await adapter.list({ count: 20, offset: 40 })

    expect(result).toEqual({ contract: 'offset', items: [entry], nextCursor: null, offset: 40 })
    expect(get).toHaveBeenCalledWith('/api/admin/logs', {
      level: 'All',
      count: 20,
      skip: 40,
      cursor: undefined,
    })
  })

  it('normalizes the newer cursor response without changing the page model', async () => {
    const get = vi.fn().mockResolvedValue({ items: [entry], nextCursor: 'cursor-2' })
    const adapter = createAdminLogApi(createClient({ get }))

    await expect(adapter.list({ cursor: 'cursor-1' })).resolves.toEqual({
      contract: 'cursor',
      items: [entry],
      nextCursor: 'cursor-2',
      offset: 0,
    })
  })
})
