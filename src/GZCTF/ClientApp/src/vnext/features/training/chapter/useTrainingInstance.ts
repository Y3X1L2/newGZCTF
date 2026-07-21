import { useCallback, useEffect, useRef, useState } from 'react'
import { ChallengeType, EnvironmentType, TrainingCourseChallengeDetailModel } from '@Api'
import { errorMessage } from '../../../shared/errors'
import { publicEntryAvailableAt } from '../../challenge-runtime/entryReadiness'
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
  const [phase, setPhase] = useState<RuntimeInstancePhase>(challenge?.context?.instanceEntry ? 'running' : 'idle')
  const [error, setError] = useState<string | null>(null)
  const [entryAvailableAt, setEntryAvailableAt] = useState<number | null>(null)
  const activeChallengeRef = useRef(challengeId)
  const operationRef = useRef<OperationState | null>(null)

  useEffect(() => {
    activeChallengeRef.current = challengeId
    operationRef.current = null
    setError(null)
    setEntryAvailableAt(null)
    setPhase(challenge?.context?.instanceEntry ? 'running' : 'idle')
  }, [challengeId, kind])

  useEffect(() => {
    if (operationRef.current || kind === 'none') return
    setPhase(challenge?.context?.instanceEntry ? 'running' : 'idle')
  }, [challenge?.context?.instanceEntry, kind])

  const refresh = useCallback(async () => {
    if (!challengeId || kind === 'none') return
    try {
      const next = await refreshChallenge()
      if (activeChallengeRef.current !== challengeId || !next) return
      const operation = operationRef.current
      const entry = next.context?.instanceEntry ?? null
      const closeTime = next.context?.closeTime ?? null

      if (!operation) {
        setError(null)
        setPhase(entry ? 'running' : 'idle')
        return
      }

      if (operation.kind === 'destroy' && !entry) {
        operationRef.current = null
        setEntryAvailableAt(null)
        setError(null)
        setPhase('idle')
        return
      }

      if (operation.kind === 'create' && entry) {
        operationRef.current = null
        setEntryAvailableAt((current) => current ?? publicEntryAvailableAt())
        setError(null)
        setPhase('running')
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
        setPhase(entry ? 'running' : 'failed')
      }
    } catch (requestError) {
      if (activeChallengeRef.current !== challengeId) return
      operationRef.current = null
      setError(errorMessage(requestError, '实例状态读取失败。'))
      setPhase(challenge?.context?.instanceEntry ? 'running' : 'failed')
    }
  }, [challenge?.context?.instanceEntry, challengeId, kind, refreshChallenge])

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
      if (response.entry) {
        setEntryAvailableAt(publicEntryAvailableAt())
        updateChallenge({
          ...challenge,
          context: {
            ...challenge?.context,
            closeTime: response.expectStopAt,
            instanceEntry: response.entry,
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
    entry: challenge?.context?.instanceEntry ?? null,
    entryAvailableAt: kind === 'docker' ? entryAvailableAt : null,
    closeTime: challenge?.context?.closeTime ?? null,
    vmStatus: null,
    error,
    busy: ['provisioning', 'extending', 'stopping'].includes(phase),
    create,
    extend,
    destroy,
    refresh,
  }
}
