import { describe, expect, it } from 'vitest'
import type { GlobalInstanceItem } from '../api'
import { canDestroyInstance, instanceContextLabel, instanceStatusMeta } from './instancePresentation'

function instance(overrides: Partial<GlobalInstanceItem> = {}): GlobalInstanceItem {
  return {
    nodeId: 'node-1',
    nodeName: 'worker-1',
    kind: 'container',
    id: 'container-1',
    name: 'SSTI',
    status: 'Running',
    isActive: true,
    startedAt: 1,
    expectedStopAt: null,
    stoppedAt: null,
    duration: '1分钟',
    image: null,
    runtimeId: null,
    entry: null,
    ip: null,
    port: null,
    gameId: null,
    gameTitle: null,
    challengeId: null,
    challengeTitle: null,
    challengeCategory: null,
    teamId: null,
    teamName: null,
    userId: null,
    userName: null,
    providerName: null,
    osType: null,
    ...overrides,
  }
}

describe('instance presentation', () => {
  it('surfaces abnormal states before generic active state', () => {
    expect(instanceStatusMeta(instance({ status: 'Orphaned' }))).toMatchObject({ label: '孤儿资源', tone: 'danger' })
  })

  it('keeps the business context factual', () => {
    expect(instanceContextLabel(instance({ gameTitle: '训练赛', challengeTitle: 'SSTI' }))).toBe('训练赛 / SSTI')
    expect(instanceContextLabel(instance())).toBe('无业务上下文')
  })

  it('only enables global destroy for active Docker and VM resources', () => {
    expect(canDestroyInstance(instance())).toBe(true)
    expect(canDestroyInstance(instance({ kind: 'pentest' }))).toBe(false)
    expect(canDestroyInstance(instance({ isActive: false }))).toBe(false)
  })
})
