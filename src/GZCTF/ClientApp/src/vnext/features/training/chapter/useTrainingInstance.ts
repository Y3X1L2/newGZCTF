import { useCallback, useEffect, useRef, useState } from 'react'
import {
  ChallengeType,
  ClientFlagContext,
  ContainerEntryStatus,
  EnvironmentType,
  TrainingCourseChallengeDetailModel,
} from '@Api'
import { errorMessage } from '../../../shared/errors'
import { RuntimeInstanceController, RuntimeInstancePhase } from '../../challenge-runtime/types'
import { trainingChapterApi } from './trainingChapterApi'

type PendingOperation = 'create' | 'extend' | 'destroy'

interface OperationState {
  kind: PendingOperation
  startedAt: number
  previousCloseTime: number | null
}

function instanceKind(challenge?: TrainingCourseChallengeDetailModel): RuntimeInstanceController['kind'] {
  const isContainer =
    challenge?.type === ChallengeType.StaticContainer || challenge?.type === ChallengeType.DynamicContainer
  if (!isContainer) return 'none'
  return challenge.environment === EnvironmentType.WindowsVM ? 'windows' : 'docker'
}

function operationTimedOut(operation: OperationState) {
  return Date.now() - operation.startedAt > 120_000
}

function resolvedEntryStatus(context?: ClientFlagContext): ContainerEntryStatus | null {
  return context?.instanceEntryStatus ?? (context?.instanceEntry ? ContainerEntryStatus.Ready : null)
}

function resolvedPhase(context?: ClientFlagContext): RuntimeInstancePhase {
  const status = resolvedEntryStatus(context)
  if (status === ContainerEntryStatus.Error) return 'failed'
  if (status === ContainerEntryStatus.Pending) return 'provisioning'
  return context?.instanceEntry ? 'running' : 'idle'
}

export function useTrainingInstance({
  courseId,
  chapterId,
  challenge,
  updateChallenge,
  refreshChallenge,
}: {
  courseId: number
  chapterId: number
  challenge?: TrainingCourseChallengeDetailModel
  updateChallenge: (next: TrainingCourseChallengeDetailModel) => void
  refreshChallenge: () => Promise<TrainingCourseChallengeDetailModel | undefined>
}): RuntimeInstanceController {
  const challengeId = challenge?.id ?? 0
  const kind = instanceKind(challenge)
  const instanceEntry = challenge?.context?.instanceEntry ?? null
  const instanceEntryStatus = resolvedEntryStatus(challenge?.context)
  const instanceEntryReadyAt = challenge?.context?.instanceEntryReadyAt ?? null
  const instanceEntryError = challenge?.context?.instanceEntryError ?? null
  const closeTime = challenge?.context?.closeTime ?? null
  const [phase, setPhase] = useState<RuntimeInstancePhase>(resolvedPhase(challenge?.context))
  const [error, setError] = useState<string | null>(null)
  const activeChallengeRef = useRef(challengeId)
  const operationRef = useRef<OperationState | null>(null)

  useEffect(() => {
    activeChallengeRef.current = challengeId
    operationRef.current = null
    setError(null)
    setPhase(resolvedPhase(challenge?.context))
  }, [challengeId, kind])

  useEffect(() => {
    if (operationRef.current || kind === 'none') return
    const nextPhase = resolvedPhase(challenge?.context)
    setPhase(nextPhase)
    setError(nextPhase === 'failed' ? (instanceEntryError ?? '公网入口发布失败。') : null)
  }, [instanceEntry, instanceEntryError, instanceEntryStatus, kind])

  const refresh = useCallback(async () => {
    if (!challengeId || kind === 'none') return
    try {
      const next = await refreshChallenge()
      if (activeChallengeRef.current !== challengeId || !next) return
      updateChallenge(next)
      const operation = operationRef.current
      const entry = next.context?.instanceEntry ?? null
      const entryStatus = resolvedEntryStatus(next.context)
      const closeTime = next.context?.closeTime ?? null

      if (!operation) {
        setError(entryStatus === ContainerEntryStatus.Error ? (next.context?.instanceEntryError ?? '公网入口发布失败。') : null)
        setPhase(resolvedPhase(next.context))
        return
      }

      if (operation.kind === 'destroy' && !entry) {
        operationRef.current = null
        setError(null)
        setPhase('idle')
        return
      }

      if (operation.kind === 'create' && entryStatus === ContainerEntryStatus.Ready && entry) {
        operationRef.current = null
        setError(null)
        setPhase('running')
        return
      }

      if (operation.kind === 'create' && entryStatus === ContainerEntryStatus.Error) {
        operationRef.current = null
        setError(next.context?.instanceEntryError ?? '公网入口发布失败，请联系管理员或稍后刷新。')
        setPhase('failed')
        return
      }

      if (
        operation.kind === 'extend' &&
        entry &&
        closeTime &&
        (!operation.previousCloseTime || closeTime > operation.previousCloseTime)
      ) {
        operationRef.current = null
        setError(null)
        setPhase('running')
        return
      }

      if (operationTimedOut(operation)) {
        operationRef.current = null
        setError('实例操作等待超时，请刷新状态后重试。')
        setPhase(resolvedPhase(next.context) === 'idle' ? 'failed' : resolvedPhase(next.context))
      }
    } catch (requestError) {
      if (activeChallengeRef.current !== challengeId) return
      operationRef.current = null
      setError(errorMessage(requestError, '实例状态读取失败。'))
      const currentPhase = resolvedPhase(challenge?.context)
      setPhase(currentPhase === 'idle' ? 'failed' : currentPhase)
    }
  }, [challengeId, instanceEntry, instanceEntryStatus, kind, refreshChallenge, updateChallenge])

  useEffect(() => {
    if (!['provisioning', 'extending', 'stopping'].includes(phase)) return undefined
    const timer = window.setInterval(() => void refresh(), 2500)
    return () => window.clearInterval(timer)
  }, [phase, refresh])

  const create = useCallback(async () => {
    if (!challengeId || kind === 'none' || !['idle', 'failed'].includes(phase)) return
    setError(null)
    operationRef.current = { kind: 'create', startedAt: Date.now(), previousCloseTime: null }
    setPhase('provisioning')
    try {
      const response = await trainingChapterApi.createInstance(courseId, chapterId, challengeId)
      if (activeChallengeRef.current !== challengeId) return
      const entryStatus = response.entryStatus ??
        (response.entry ? ContainerEntryStatus.Ready : ContainerEntryStatus.Pending)
      if (entryStatus === ContainerEntryStatus.Ready && response.entry) {
        updateChallenge({
          ...challenge,
          context: {
            ...challenge?.context,
            closeTime: response.expectStopAt,
            instanceEntry: response.entry,
            instanceEntryStatus: entryStatus,
            instanceEntryReadyAt: response.entryReadyAt,
            instanceEntryError: response.entryError,
          },
        })
        operationRef.current = null
        setPhase('running')
      } else {
        updateChallenge({
          ...challenge,
          context: {
            ...challenge?.context,
            closeTime: response.expectStopAt,
            instanceEntry: null,
            instanceEntryStatus: entryStatus,
            instanceEntryReadyAt: response.entryReadyAt,
            instanceEntryError: response.entryError,
          },
        })
        window.setTimeout(() => void refresh(), 900)
      }
    } catch (requestError) {
      if (activeChallengeRef.current !== challengeId) return
      operationRef.current = null
      setError(errorMessage(requestError, '实例创建失败，请稍后重试。'))
      setPhase('failed')
    }
  }, [challenge, challengeId, chapterId, courseId, kind, phase, refresh, updateChallenge])

  const extend = useCallback(async () => {
    if (!challengeId || kind === 'none' || phase !== 'running') return
    setError(null)
    operationRef.current = {
      kind: 'extend',
      startedAt: Date.now(),
      previousCloseTime: challenge?.context?.closeTime ?? null,
    }
    setPhase('extending')
    try {
      const response = await trainingChapterApi.extendInstance(courseId, chapterId, challengeId)
      if (activeChallengeRef.current !== challengeId) return
      if (response.entry && response.expectStopAt) {
        updateChallenge({
          ...challenge,
          context: {
            ...challenge?.context,
            closeTime: response.expectStopAt,
            instanceEntry: response.entry,
            instanceEntryStatus: response.entryStatus ?? challenge?.context?.instanceEntryStatus,
            instanceEntryReadyAt: response.entryReadyAt ?? challenge?.context?.instanceEntryReadyAt,
            instanceEntryError: response.entryError ?? challenge?.context?.instanceEntryError,
          },
        })
        operationRef.current = null
        setPhase('running')
      } else {
        window.setTimeout(() => void refresh(), 900)
      }
    } catch (requestError) {
      if (activeChallengeRef.current !== challengeId) return
      operationRef.current = null
      setError(errorMessage(requestError, '实例延期失败。'))
      setPhase('running')
    }
  }, [challenge, challengeId, chapterId, courseId, kind, phase, refresh, updateChallenge])

  const destroy = useCallback(async () => {
    if (!challengeId || kind === 'none' || phase === 'idle') return
    setError(null)
    operationRef.current = {
      kind: 'destroy',
      startedAt: Date.now(),
      previousCloseTime: challenge?.context?.closeTime ?? null,
    }
    setPhase('stopping')
    try {
      await trainingChapterApi.destroyInstance(courseId, chapterId, challengeId)
      if (activeChallengeRef.current !== challengeId) return
      window.setTimeout(() => void refresh(), 900)
    } catch (requestError) {
      if (activeChallengeRef.current !== challengeId) return
      operationRef.current = null
      setError(errorMessage(requestError, '实例销毁失败。'))
      setPhase(challenge?.context?.instanceEntry ? 'running' : 'failed')
    }
  }, [
    challenge?.context?.closeTime,
    challenge?.context?.instanceEntry,
    challengeId,
    chapterId,
    courseId,
    kind,
    phase,
    refresh,
  ])

  return {
    kind,
    phase,
    entry: instanceEntry,
    entryStatus: kind === 'docker' ? instanceEntryStatus : null,
    entryReadyAt: kind === 'docker' ? instanceEntryReadyAt : null,
    entryError: kind === 'docker' ? instanceEntryError : null,
    closeTime,
    vmStatus: null,
    error,
    busy: ['provisioning', 'extending', 'stopping'].includes(phase),
    create,
    extend,
    destroy,
    refresh,
  }
}
