import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { encryptApiData } from '@Utils/Crypto'
import { AwdpPatchStatus } from '@Api'
import { errorMessage } from '../../../shared/errors'
import { AwdpInstance, AwdpPlayerSnapshot, resolveMyTeamId } from '../../awdp/awdpDomain'
import { useAwdpMonitor } from '../../awdp/useAwdpMonitor'
import { awdpPlayerApi } from './api/awdpPlayerApi'

type Feedback = { tone: 'danger' | 'success'; message: string }
type InstanceAction = 'recover' | 'reset'

export function useAwdpWorkspaceController(gameId: number, publicKey?: string | null, currentTeamName?: string | null) {
  const [snapshot, setSnapshot] = useState<AwdpPlayerSnapshot | null>(null)
  const [error, setError] = useState<unknown>(null)
  const [loading, setLoading] = useState(true)
  const [refreshing, setRefreshing] = useState(false)
  const [operation, setOperation] = useState<string | null>(null)
  const [feedback, setFeedback] = useState<Feedback | null>(null)
  const requestRef = useRef(0)

  const load = useCallback(
    async (initial = false) => {
      const requestId = ++requestRef.current
      if (initial) setLoading(true)
      else setRefreshing(true)
      try {
        const next = await awdpPlayerApi.snapshot(gameId)
        if (requestRef.current !== requestId) return
        setSnapshot(next)
        setError(null)
      } catch (requestError) {
        if (requestRef.current === requestId) setError(requestError)
      } finally {
        if (requestRef.current === requestId) {
          setLoading(false)
          setRefreshing(false)
        }
      }
    },
    [gameId]
  )

  useEffect(() => {
    void load(true)
  }, [load])

  useEffect(() => {
    const timer = window.setInterval(() => {
      if (!document.hidden) void load(false)
    }, 30_000)
    return () => window.clearInterval(timer)
  }, [load])

  const monitorState = useAwdpMonitor(gameId, gameId > 0, () => void load(false))
  const myTeamId = useMemo(
    () => resolveMyTeamId(snapshot?.instances ?? [], snapshot?.scoreboard ?? [], currentTeamName),
    [currentTeamName, snapshot?.instances, snapshot?.scoreboard]
  )

  const run = useCallback(
    async (key: string, action: () => Promise<void>, success: string | null) => {
      setOperation(key)
      setFeedback(null)
      try {
        await action()
        if (success) setFeedback({ tone: 'success', message: success })
        await load(false)
        return true
      } catch (requestError) {
        setFeedback({ tone: 'danger', message: errorMessage(requestError, 'AWDP 操作失败，请稍后重试。') })
        return false
      } finally {
        setOperation(null)
      }
    },
    [load]
  )

  const submitFlag = useCallback(
    async (flag: string) => {
      const value = flag.trim()
      if (!value) return false
      return run(
        'flag',
        async () => {
          const encrypted = await encryptApiData((key) => key, value, publicKey)
          const result = await awdpPlayerApi.submitFlag(gameId, encrypted)
          if (!result.accepted) throw new Error(result.message || 'Flag 未通过判定。')
          setFeedback({
            tone: 'success',
            message: `${result.serviceName || '目标服务'} Flag 正确，获得 ${result.points ?? 0} 分。`,
          })
        },
        null
      )
    },
    [gameId, publicKey, run]
  )

  const submitPatch = useCallback(
    async (serviceId: number, file: File) => {
      if (!serviceId || !file) return false
      if (!/\.(?:tgz|tar\.gz)$/i.test(file.name)) {
        setFeedback({ tone: 'danger', message: '补丁包必须使用 .tgz 或 .tar.gz 格式。' })
        return false
      }
      return run(
        `patch:${serviceId}`,
        async () => {
          const result = await awdpPlayerApi.submitPatch(gameId, serviceId, file)
          const defended = result.finalStatus === AwdpPatchStatus.ExpFailed
          setFeedback({
            tone: defended ? 'success' : 'danger',
            message: result.message || (defended ? '补丁验证完成，漏洞已阻断。' : '补丁未通过完整验证。'),
          })
        },
        null
      )
    },
    [gameId, run]
  )

  const runInstanceAction = useCallback(
    async (kind: InstanceAction, instance: AwdpInstance) => {
      const label = kind === 'reset' ? '重置' : '恢复'
      return run(
        `${kind}:${instance.instanceId}`,
        async () => {
          const result =
            kind === 'reset'
              ? await awdpPlayerApi.resetInstance(instance.instanceId)
              : await awdpPlayerApi.recoverInstance(instance.instanceId)
          if (!result.success) throw new Error(result.message || `${label}未成功。`)
        },
        `${instance.serviceName} 已执行${label}，状态正在刷新。`
      )
    },
    [run]
  )

  return {
    snapshot,
    error,
    loading,
    refreshing,
    operation,
    feedback,
    monitorState,
    myTeamId,
    refresh: () => load(false),
    submitFlag,
    submitPatch,
    runInstanceAction,
    clearFeedback: () => setFeedback(null),
  }
}
