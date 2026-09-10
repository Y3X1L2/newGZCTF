import { useEffect, useRef, useState } from 'react'
import { useSearchParams } from 'react-router'
import useSWR, { useSWRConfig } from 'swr'
import { teamLabRuntimeKeys, type TeamLabRuntime } from '../api'
import { assetControlApi, type AssetControlAction } from '../api/teamlabAssetControlApi'

export function useAssetControls(runtime: TeamLabRuntime) {
  const [params, setParams] = useSearchParams()
  const selected = Number(params.get('asset'))
  const asset = runtime.assets.find(item => item.id === selected) ?? runtime.assets[0]
  const ticketId = params.get('assetTask')
  const [reason, setReason] = useState('')
  const [pending, setPending] = useState<AssetControlAction | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<unknown>(null)
  const submitting = useRef(false)
  const { mutate } = useSWRConfig()
  const availability = useSWR(asset ? ['teamlab:asset-control:availability', runtime.id, runtime.generation, asset.id, runtime.status] : null,
    () => assetControlApi.availability(runtime.id, asset!.id))
  const task = useSWR(asset && ticketId ? ['teamlab:asset-control', runtime.id, runtime.generation, asset.id, ticketId] : null,
    () => assetControlApi.task(runtime.id, asset!.id, ticketId!),
    { refreshInterval: latest => latest && ['succeeded', 'failed', 'cancelled'].includes(latest.status) ? 0 : 1500 })
  useEffect(() => {
    if (task.data) void mutate(teamLabRuntimeKeys.runtime(runtime.id))
  }, [task.data?.status, runtime.id, mutate])
  const recordTicket = (id: string) => setParams(current => {
    const next = new URLSearchParams(current)
    next.set('asset', String(asset!.id))
    next.set('assetTask', id)
    return next
  }, { replace: true })
  const run = async (retry: boolean) => {
    if (!asset || submitting.current || !retry && !pending) return false
    submitting.current = true
    setBusy(true)
    setError(null)
    try {
      const id = retry ? await assetControlApi.retry(runtime.id, asset.id, ticketId!)
        : await assetControlApi.submit(runtime.id, asset.id, runtime.generation, pending!, reason.trim())
      recordTicket(id)
      await mutate(teamLabRuntimeKeys.runtime(runtime.id))
      return true
    } catch (failure) { setError(failure); return false }
    finally { submitting.current = false; setBusy(false) }
  }
  return { asset, reason, setReason, pending, setPending, busy, error: error ?? task.error ?? availability.error, task: task.data,
    allowed: availability.data?.allowed === true, unavailableReason: availability.data?.reason,
    active: busy || !!task.data && !['succeeded', 'failed', 'cancelled'].includes(task.data.status),
    submit: () => run(false), retry: () => run(true),
    select: (id: number) => {
      setError(null)
      setPending(null)
      setParams(current => { const next = new URLSearchParams(current); next.set('asset', String(id)); next.delete('assetTask'); return next }, { replace: true })
    },
  }
}
