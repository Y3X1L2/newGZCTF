import { useCallback, useEffect, useRef, useState } from 'react'
import { ChallengeDetailModel, ChallengeType, ClientFlagContext, ContainerEntryStatus, EnvironmentType } from '@Api'
import { errorMessage } from '../../../shared/errors'
import { RuntimeInstanceController, RuntimeInstancePhase } from '../../challenge-runtime/types'
import { gamePlayerApi, PlayerVmStatus } from '../gamePlayerApi'

// Backend image preparation permits two hours before the bounded Agent create call.
const dockerProvisioningTimeoutMs = 130 * 60_000

function challengeInstanceKind(challenge?: ChallengeDetailModel): RuntimeInstanceController['kind'] {
  const container =
    challenge?.type === ChallengeType.StaticContainer || challenge?.type === ChallengeType.DynamicContainer
  if (!container) return 'none'
  return challenge.environment === EnvironmentType.WindowsVM ? 'windows' : 'docker'
}

function vmPhase(status: PlayerVmStatus): RuntimeInstancePhase {
  if (status.status === 'Error' || status.stage === 'error') return 'failed'
  if (status.status === 'Destroyed' || status.status === 'Stopped') return 'idle'
  if ((status.rdpHost && status.rdpPort) || status.rdpUrl || status.stage === 'ready') return 'running'
  if (status.queue?.queuePosition || status.queue?.peopleAhead) return 'queued'
  return 'provisioning'
}

function resolvedEntryStatus(context?: ClientFlagContext): ContainerEntryStatus | null {
  return context?.instanceEntryStatus ?? (context?.instanceEntry ? ContainerEntryStatus.Ready : null)
}

function dockerPhase(context?: ClientFlagContext): RuntimeInstancePhase {
  const status = resolvedEntryStatus(context)
  if (status === ContainerEntryStatus.Error) return 'failed'
  if (status === ContainerEntryStatus.Pending) return 'provisioning'
  return context?.instanceEntry ? 'running' : 'idle'
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
  const instanceEntry = challenge?.context?.instanceEntry ?? null
  const instanceEntryStatus = resolvedEntryStatus(challenge?.context)
  const instanceEntryReadyAt = challenge?.context?.instanceEntryReadyAt ?? null
  const instanceEntryError = challenge?.context?.instanceEntryError ?? null
  const closeTime = challenge?.context?.closeTime ?? null
  const [phase, setPhase] = useState<RuntimeInstancePhase>('idle')
  const [vmStatus, setVmStatus] = useState<PlayerVmStatus | null>(null)
  const [error, setError] = useState<string | null>(null)
  const activeChallengeRef = useRef(challengeId)
  const provisioningStartedRef = useRef<number | null>(null)

  useEffect(() => {
    activeChallengeRef.current = challengeId
    provisioningStartedRef.current = null
    setError(null)
    setVmStatus(null)
    if (kind === 'docker') setPhase(dockerPhase(challenge?.context))
    else if (kind === 'windows') setPhase('provisioning')
    else setPhase('idle')
  }, [challengeId, kind])

  useEffect(() => {
    if (kind !== 'docker') return
    const nextPhase = dockerPhase(challenge?.context)
    setPhase((current) => {
      if (current === 'extending' || current === 'stopping') return current
      if (!provisioningStartedRef.current || nextPhase === 'failed') return nextPhase
      return current
    })
    if (nextPhase === 'failed') setError(instanceEntryError ?? '公网入口发布失败。')
    else if (nextPhase === 'running') setError(null)
  }, [instanceEntry, instanceEntryError, instanceEntryStatus, kind])

  const refresh = useCallback(async () => {
    if (!challengeId || kind === 'none') return
    if (kind === 'docker') {
      try {
        const next = await refreshChallenge()
        if (activeChallengeRef.current !== challengeId || !next) return
        updateChallenge(next)
        const entryStatus = resolvedEntryStatus(next.context)
        if (entryStatus === ContainerEntryStatus.Ready && next.context?.instanceEntry) {
          provisioningStartedRef.current = null
          setError(null)
          setPhase('running')
        } else if (entryStatus === ContainerEntryStatus.Error) {
          provisioningStartedRef.current = null
          setError(next.context?.instanceEntryError ?? '公网入口发布失败，请联系管理员或稍后刷新。')
          setPhase('failed')
        } else if (
          provisioningStartedRef.current &&
          Date.now() - provisioningStartedRef.current >= dockerProvisioningTimeoutMs
        ) {
          provisioningStartedRef.current = null
          setError('实例准备超过预期时间，请刷新状态或重新创建。')
          setPhase('failed')
        } else if (provisioningStartedRef.current) {
          setPhase('provisioning')
        } else if (entryStatus === ContainerEntryStatus.Pending) {
          setPhase('provisioning')
        } else {
          setPhase('idle')
        }
      } catch (requestError) {
        if (activeChallengeRef.current !== challengeId) return
        setError(errorMessage(requestError, '实例状态读取失败，请稍后刷新。'))
        if (instanceEntryStatus !== ContainerEntryStatus.Pending) setPhase('failed')
      }
      return
    }

    try {
      const next = await gamePlayerApi.vmStatus(gameId, challengeId)
      if (activeChallengeRef.current !== challengeId) return
      const nextPhase = next ? vmPhase(next) : 'idle'
      setVmStatus(next)
      setError(
        nextPhase === 'failed'
          ? (next?.queue?.errorMessage ?? next?.stageMessage ?? 'Windows 靶机创建失败，请联系管理员。')
          : null
      )
      setPhase(nextPhase)
    } catch (requestError) {
      if (activeChallengeRef.current !== challengeId) return
      setError(errorMessage(requestError, 'Windows 靶机状态读取失败。'))
      setPhase('failed')
    }
  }, [challengeId, gameId, instanceEntryStatus, kind, refreshChallenge, updateChallenge])

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
        const entryStatus =
          response.entryStatus ?? (response.entry ? ContainerEntryStatus.Ready : ContainerEntryStatus.Pending)
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
        setPhase(entryStatus === ContainerEntryStatus.Ready && response.entry ? 'running' : 'provisioning')
        if (entryStatus === ContainerEntryStatus.Ready && response.entry) provisioningStartedRef.current = null
        if (entryStatus !== ContainerEntryStatus.Ready || !response.entry) {
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
          instanceEntryStatus: response.entryStatus ?? challenge?.context?.instanceEntryStatus,
          instanceEntryReadyAt: response.entryReadyAt ?? challenge?.context?.instanceEntryReadyAt,
          instanceEntryError: response.entryError ?? challenge?.context?.instanceEntryError,
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
      provisioningStartedRef.current = null
      updateChallenge({
        ...challenge,
        context: {
          ...challenge?.context,
          closeTime: null,
          instanceEntry: null,
          instanceEntryStatus: null,
          instanceEntryReadyAt: null,
          instanceEntryError: null,
        },
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
        ? vmStatus?.rdpHost && vmStatus.rdpPort
          ? `${vmStatus.rdpHost}:${vmStatus.rdpPort}`
          : (vmStatus?.rdpUrl ?? vmStatus?.ipAddress ?? null)
        : instanceEntry,
    entryStatus: kind === 'docker' ? instanceEntryStatus : null,
    entryReadyAt: kind === 'docker' ? instanceEntryReadyAt : null,
    entryError: kind === 'docker' ? instanceEntryError : null,
    closeTime,
    vmStatus,
    error,
    busy: ['queued', 'provisioning', 'extending', 'stopping'].includes(phase),
    create,
    extend,
    destroy,
    refresh,
  }
}
