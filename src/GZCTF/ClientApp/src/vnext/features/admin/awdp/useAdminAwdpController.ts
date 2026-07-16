import { useCallback, useEffect, useRef, useState } from 'react'
import { errorMessage } from '../../../shared/errors'
import { AwdpAdminSnapshot, AwdpInstance } from '../../awdp/awdpDomain'
import { useAwdpMonitor } from '../../awdp/useAwdpMonitor'
import { awdpAdminApi } from './api/awdpAdminApi'
import { AwdpServiceDraft, toAwdpServiceWriteModel } from './awdpServiceForm'

type Feedback = { tone: 'danger' | 'success'; message: string }
type InstanceAction = 'recover' | 'reset'

export function useAdminAwdpController(gameId: number) {
  const [snapshot, setSnapshot] = useState<AwdpAdminSnapshot | null>(null)
  const [images, setImages] = useState<Array<{ id: number; name: string; registryUrl: string }>>([])
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
        const next = await awdpAdminApi.snapshot(gameId)
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
    void awdpAdminApi.readyDockerImages().then(setImages, () => setImages([]))
  }, [load])

  useEffect(() => {
    const timer = window.setInterval(() => {
      if (!document.hidden) void load(false)
    }, 30_000)
    return () => window.clearInterval(timer)
  }, [load])

  const monitorState = useAwdpMonitor(gameId, gameId > 0, () => void load(false))

  const run = useCallback(
    async (key: string, action: () => Promise<void>, success: string) => {
      setOperation(key)
      setFeedback(null)
      try {
        await action()
        setFeedback({ tone: 'success', message: success })
        await load(false)
        return true
      } catch (requestError) {
        setFeedback({ tone: 'danger', message: errorMessage(requestError, 'AWDP 管理操作失败。') })
        return false
      } finally {
        setOperation(null)
      }
    },
    [load]
  )

  const saveService = useCallback(
    async (serviceId: number | null, draft: AwdpServiceDraft) =>
      run(
        `service:${serviceId ?? 'new'}`,
        async () => {
          const model = toAwdpServiceWriteModel(draft)
          if (serviceId) await awdpAdminApi.updateService(serviceId, model)
          else await awdpAdminApi.createService(gameId, model)
        },
        serviceId ? 'AWDP 服务配置已更新。' : 'AWDP 服务已创建。'
      ),
    [gameId, run]
  )

  const deleteService = useCallback(
    (serviceId: number, name: string) =>
      run(`delete:${serviceId}`, () => awdpAdminApi.deleteService(serviceId), `${name} 已删除。`),
    [run]
  )

  const setRunning = useCallback(
    (running: boolean) =>
      run(
        running ? 'start' : 'stop',
        () => (running ? awdpAdminApi.start(gameId) : awdpAdminApi.stop(gameId)),
        running ? 'AWDP 已开始部署和运行。' : '当前 AWDP 轮次已停止。'
      ),
    [gameId, run]
  )

  const runInstanceAction = useCallback(
    (kind: InstanceAction, instance: AwdpInstance) => {
      const label = kind === 'reset' ? '重置' : '恢复'
      return run(
        `${kind}:${instance.instanceId}`,
        async () => {
          const result =
            kind === 'reset'
              ? await awdpAdminApi.resetInstance(instance.instanceId)
              : await awdpAdminApi.recoverInstance(instance.instanceId)
          if (!result.success) throw new Error(result.message || `${label}未成功。`)
        },
        `${instance.teamName} / ${instance.serviceName} 已执行${label}。`
      )
    },
    [run]
  )

  return {
    snapshot,
    images,
    error,
    loading,
    refreshing,
    operation,
    feedback,
    monitorState,
    refresh: () => load(false),
    saveService,
    deleteService,
    setRunning,
    runInstanceAction,
  }
}
