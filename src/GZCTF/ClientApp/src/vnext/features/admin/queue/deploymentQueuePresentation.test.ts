import { describe, expect, it } from 'vitest'
import type { DeploymentTask } from '../api'
import {
  deploymentStageLabel,
  deploymentStatusMeta,
  formatDeploymentDuration,
} from './deploymentQueuePresentation'

function task(overrides: Partial<DeploymentTask> = {}): DeploymentTask {
  return {
    id: 'task-1',
    actionLabel: '创建',
    typeLabel: 'Docker',
    requestLabel: '测试任务',
    ownerLabel: null,
    gameLabel: null,
    challengeLabel: null,
    image: null,
    targetNodeId: null,
    targetNodeName: null,
    targetNodeHost: null,
    targetNodeLabel: '未分配',
    statusLabel: '执行中',
    statusKey: 'running',
    status: 3,
    dockerSlots: 1,
    vmSlots: 0,
    queuePosition: 0,
    peopleAhead: 0,
    errorMessage: null,
    createdAt: 1_000,
    startedAt: 2_000,
    completedAt: null,
    ...overrides,
  }
}

describe('deployment queue presentation', () => {
  it('maps active and terminal states without relying on colors alone', () => {
    expect(deploymentStatusMeta('running')).toMatchObject({ label: '执行中', active: true })
    expect(deploymentStatusMeta('failed')).toMatchObject({ label: '失败', active: false, tone: 'danger' })
  })

  it('uses the fact-based deployment stage labels', () => {
    expect(deploymentStageLabel(4)).toBe('拉取镜像')
    expect(deploymentStageLabel(99, '自定义阶段')).toBe('自定义阶段')
  })

  it('formats duration from the actual start and completion timestamps', () => {
    expect(formatDeploymentDuration(task({ completedAt: 67_000 }))).toBe('1 分 5 秒')
  })
})
