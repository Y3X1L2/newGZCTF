import { useCallback, useEffect, useRef, useState } from 'react'
import { ChallengeDetailModel, ChallengeType, ContainerStatus, EnvironmentType, VmStatusResponse } from '@Api'
import { errorMessage } from '../../../shared/errors'
import { publicEntryAvailableAt } from '../../challenge-runtime/entryReadiness'
import { RuntimeInstanceController, RuntimeInstancePhase } from '../../challenge-runtime/types'
import { gamePlayerApi } from '../gamePlayerApi'

function challengeInstanceKind(challenge?: ChallengeDetailModel): RuntimeInstanceController['kind'] {
  const container =
    challenge?.type === ChallengeType.StaticContainer || challenge?.type === ChallengeType.DynamicContainer
  if (!container) return 'none'
  return challenge.environment === EnvironmentType.WindowsVM ? 'windows' : 'docker'
}

function vmPhase(status: VmStatusResponse): RuntimeInstancePhase {
  if (status.status === 'Error' || status.stage === 'error') return 'failed'
  if (status.status === 'Destroyed' || status.status === 'Stopped') return 'idle'
  if (status.rdpUrl || status.stage === 'ready') return 'running'
  if (status.queue?.queuePosition || status.queue?.peopleAhead) return 'queued'
  return 'provisioning'
}

export function useGameInstance({
  gameId,
  challenge,
  updateChallenge,
  refreshChallenge,
}: {
  gameId: number
  challenge?: ChallengeDetailModel
  updateChallenge: (next: ChallengeDetailModel) => void
  refreshChallenge: () => Promise<ChallengeDetailModel | undefined>
}): RuntimeInstanceController {
  const challengeId = challenge?.id ?? 0
  const kind = challengeInstanceKind(challenge)
  const [phase, setPhase] = useState<RuntimeInstancePhase>('idle')
  const [vmStatus, setVmStatus] = useState<VmStatusResponse | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [entryAvailableAt, setEntryAvailableAt] = useState<number | null>(null)
  const activeChallengeRef = useRef(challengeId)
  const provisioningStartedRef = useRef<number | null>(null)

  useEffect(() => {
    activeChallengeRef.current = challengeId
    provisioningStartedRef.current = null
    setError(null)
    setVmStatus(null)
    setEntryAvailableAt(null)
    if (kind === 'docker' && challenge?.context?.instanceEntry) setPhase('running')
    else if (kind === 'windows') setPhase('provisioning')
    else setPhase('idle')
  }, [challengeId, kind])

  useEffect(() => {
    if (kind !== 'docker' || phase === 'extending' || phase === 'stopping') return
    if (challenge?.context?.instanceEntry) {
      if (!provisioningStartedRef.current) setPhase('running')
    } else if (phase === 'running') {
      setPhase('idle')
    }
  }, [challenge?.context?.instanceEntry, kind, phase])

  const refresh = useCallback(async () => {
    if (!challengeId || kind === 'none') return
    if (kind === 'docker') {
      const next = await refreshChallenge()
      if (activeChallengeRef.current !== challengeId) return
      if (next?.context?.instanceEntry) {
        if (provisioningStartedRef.current) {
          setEntryAvailableAt((current) => current ?? publicEntryAvailableAt())
        }
        provisioningStartedRef.current = null
        setPhase('running')
      } else if (provisioningStartedRef.current && Date.now() - provisioningStartedRef.current < 120_000) {
        setPhase('provisioning')
      } else if (provisioningStartedRef.current) {
        setError('实例创建超时，请刷新状态或重新创建。')
        setPhase('failed')
      } else {
        setPhase('idle')
      }
      return
    }

    try {
      const next = await gamePlayerApi.vmStatus(gameId, challengeId)
      if (activeChallengeRef.current !== challengeId) return
      setVmStatus(next)
      setError(null)
      setPhase(next ? vmPhase(next) : 'idle')
    } catch (requestError) {
      if (activeChallengeRef.current !== challengeId) return
      setError(errorMessage(requestError, 'Windows 靶机状态读取失败。'))
      setPhase('failed')
    }
  }, [challengeId, gameId, kind, refreshChallenge])

  useEffect(() => {
    if (kind !== 'windows' || !challengeId) return
    void refresh()
  }, [challengeId, kind, refresh])

  useEffect(() => {
    const shouldPoll =
      (kind === 'windows' && (phase === 'queued' || phase === 'provisioning')) ||
      (kind === 'docker' && phase === 'provisioning')
    if (!shouldPoll) return undefined
    const timer = window.setInterval(() => void refresh(), kind === 'windows' ? 5000 : 2500)
    return () => window.clearInterval(timer)
  }, [kind, phase, refresh])

  const create = useCallback(async () => {
    if (!challengeId || kind === 'none' || !['idle', 'failed'].includes(phase)) return
    setError(null)
    provisioningStartedRef.current = Date.now()
    setPhase('provisioning')
    try {
      const response = await gamePlayerApi.createInstance(gameId, challengeId)
      if (activeChallengeRef.current !== challengeId) return
      if (kind === 'docker') {
        setEntryAvailableAt(response.entry ? publicEntryAvailableAt() : null)
        updateChallenge({
          ...challenge,
          context: {
            ...challenge?.context,
            closeTime: response.expectStopAt,
            instanceEntry: response.entry,
          },
        })
        setPhase(response.entry ? 'running' : 'provisioning')
        if (response.entry) provisioningStartedRef.current = null
        if (!response.entry || response.status === ContainerStatus.Pending) {
          window.setTimeout(() => void refresh(), 1200)
        }
      } else {
        setPhase('provisioning')
        window.setTimeout(() => void refresh(), 800)
      }
    } catch (requestError) {
      if (activeChallengeRef.current !== challengeId) return
      setError(errorMessage(requestError, '实例创建失败，请稍后重试。'))
      setPhase('failed')
    }
  }, [challenge, challengeId, gameId, kind, phase, refresh, updateChallenge])

  const extend = useCallback(async () => {
    if (!challengeId || kind !== 'docker' || phase !== 'running') return
    setError(null)
    setPhase('extending')
    try {
      const response = await gamePlayerApi.extendInstance(gameId, challengeId)
      if (activeChallengeRef.current !== challengeId) return
      updateChallenge({
        ...challenge,
        context: {
          ...challenge?.context,
          closeTime: response.expectStopAt,
          instanceEntry: response.entry ?? challenge?.context?.instanceEntry,
        },
      })
      setPhase('running')
    } catch (requestError) {
      if (activeChallengeRef.current !== challengeId) return
      setError(errorMessage(requestError, '实例延期失败。'))
      setPhase('running')
    }
  }, [challenge, challengeId, gameId, kind, phase, updateChallenge])

  const destroy = useCallback(async () => {
    if (!challengeId || kind === 'none' || phase === 'idle') return
    setError(null)
    setPhase('stopping')
    try {
      if (kind === 'windows') await gamePlayerApi.destroyVm(gameId, challengeId)
      else await gamePlayerApi.destroyContainer(gameId, challengeId)
      if (activeChallengeRef.current !== challengeId) return
      setVmStatus(null)
      setEntryAvailableAt(null)
      provisioningStartedRef.current = null
      updateChallenge({
        ...challenge,
        context: { ...challenge?.context, closeTime: null, instanceEntry: null },
      })
      setPhase('idle')
    } catch (requestError) {
      if (activeChallengeRef.current !== challengeId) return
      setError(errorMessage(requestError, '实例销毁失败。'))
      setPhase(kind === 'windows' && vmStatus ? vmPhase(vmStatus) : 'running')
    }
  }, [challenge, challengeId, gameId, kind, phase, updateChallenge, vmStatus])

  return {
    kind,
    phase,
    entry:
      kind === 'windows'
        ? (vmStatus?.rdpUrl ?? vmStatus?.ipAddress ?? null)
        : (challenge?.context?.instanceEntry ?? null),
    entryAvailableAt: kind === 'docker' ? entryAvailableAt : null,
    closeTime: challenge?.context?.closeTime ?? null,
    vmStatus,
    error,
    busy: ['queued', 'provisioning', 'extending', 'stopping'].includes(phase),
    create,
    extend,
    destroy,
    refresh,
  }
}
