import { useCallback, useEffect, useState } from 'react'
import { ContainerEntryStatus } from '@Api'
import { errorMessage } from '../../shared/errors'
import { RuntimeInstanceController, RuntimeInstancePhase } from '../challenge-runtime/types'
import {
  createContainer,
  destroyContainer,
  ExerciseDetailDto,
  extendContainer,
} from './api/practiceApi'

const activeQueueStatuses = new Set(['Pending', 'Scheduling', 'Scheduled', 'Running'])

export function resolvedPracticePhase(detail?: ExerciseDetailDto): RuntimeInstancePhase {
  const queue = detail?.queue
  if (queue && activeQueueStatuses.has(queue.status)) {
    if (queue.operation === 'Stop') return 'stopping'
    if (queue.operation === 'Extend') return 'extending'
    return queue.queuePosition > 1 ? 'queued' : 'provisioning'
  }
  if (detail?.context.instanceEntryStatus === ContainerEntryStatus.Error) return 'failed'
  if (detail?.context.instanceEntryStatus === ContainerEntryStatus.Pending) return 'provisioning'
  return detail?.context.instanceEntry || detail?.context.closeTime ? 'running' : 'idle'
}

export function usePracticeInstance({
  exerciseId,
  detail,
  refreshDetail,
}: {
  exerciseId: number
  detail?: ExerciseDetailDto
  refreshDetail: () => Promise<ExerciseDetailDto | undefined>
}): RuntimeInstanceController {
  const [phase, setPhase] = useState<RuntimeInstancePhase>(() => resolvedPracticePhase(detail))
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    const next = resolvedPracticePhase(detail)
    setPhase(next)
    setError(next === 'failed'
      ? detail?.queue?.errorMessage || detail?.context.instanceEntryError || '实例创建失败。'
      : null)
  }, [detail])

  const refresh = useCallback(async () => {
    try {
      const next = await refreshDetail()
      if (next) setPhase(resolvedPracticePhase(next))
    } catch (requestError) {
      setError(errorMessage(requestError, '实例状态读取失败。'))
    }
  }, [refreshDetail])

  useEffect(() => {
    if (!['queued', 'provisioning', 'extending', 'stopping'].includes(phase)) return undefined
    const timer = window.setInterval(() => void refresh(), 2500)
    return () => window.clearInterval(timer)
  }, [phase, refresh])

  const create = useCallback(async () => {
    if (!['idle', 'failed'].includes(phase)) return
    setError(null)
    setPhase('provisioning')
    try {
      await createContainer(exerciseId)
      window.setTimeout(() => void refresh(), 600)
    } catch (requestError) {
      setError(errorMessage(requestError, '实例创建失败，请稍后重试。'))
      setPhase('failed')
    }
  }, [exerciseId, phase, refresh])

  const extend = useCallback(async () => {
    if (phase !== 'running') return
    setError(null)
    setPhase('extending')
    try {
      await extendContainer(exerciseId)
      window.setTimeout(() => void refresh(), 600)
    } catch (requestError) {
      setError(errorMessage(requestError, '实例延期失败。'))
      setPhase('running')
    }
  }, [exerciseId, phase, refresh])

  const destroy = useCallback(async () => {
    if (phase === 'idle') return
    setError(null)
    setPhase('stopping')
    try {
      await destroyContainer(exerciseId)
      window.setTimeout(() => void refresh(), 600)
    } catch (requestError) {
      setError(errorMessage(requestError, '实例销毁失败。'))
      setPhase(detail?.context.instanceEntry ? 'running' : 'failed')
    }
  }, [detail?.context.instanceEntry, exerciseId, phase, refresh])

  return {
    kind: 'docker',
    phase,
    entry: detail?.context.instanceEntry ?? null,
    entryStatus: detail?.context.instanceEntryStatus ?? null,
    entryReadyAt: detail?.context.instanceEntryReadyAt ?? null,
    entryError: detail?.context.instanceEntryError ?? null,
    closeTime: detail?.context.closeTime ?? null,
    vmStatus: detail?.queue ? {
      stageMessage: detail.queue.stageMessage,
      queue: {
        queuePosition: detail.queue.queuePosition,
        peopleAhead: detail.queue.peopleAhead,
        targetNodeName: detail.queue.targetNodeName,
      },
    } : null,
    error,
    busy: ['queued', 'provisioning', 'extending', 'stopping'].includes(phase),
    create,
    extend,
    destroy,
    refresh,
  }
}
