import { useRef, useState } from 'react'
import { useSearchParams } from 'react-router'
import { differencesApi, type RuntimeDifference } from '../api/teamlabDifferencesApi'
import type { TeamLabRuntime } from '../api'

export function useRuntimeDifferences(runtime: TeamLabRuntime) {
  const [, setParams] = useSearchParams()
  const [preview, setPreview] = useState<Awaited<ReturnType<typeof differencesApi.preview>> | null>(null)
  const [error, setError] = useState<unknown>(null)
  const [busy, setBusy] = useState(false)
  const [pending, setPending] = useState<RuntimeDifference | null>(null)
  const gate = useRef(false)
  const check = async () => {
    if (gate.current) return
    gate.current = true
    setBusy(true)
    setError(null)
    try { setPreview(await differencesApi.preview(runtime.id)) }
    catch (failure) { setError(failure) }
    finally { gate.current = false; setBusy(false) }
  }
  const repair = async () => {
    if (gate.current || !pending?.assetId || !pending.suggestedAction || !preview) return false
    gate.current = true
    setBusy(true)
    setError(null)
    try {
      const ticketId = await differencesApi.repair(runtime.id, pending.assetId, preview.generation, pending.suggestedAction, '修复运行状态差异')
      setParams(current => {
        const next = new URLSearchParams(current)
        next.set('asset', String(pending.assetId))
        next.set('assetTask', ticketId)
        next.set('tab', 'operations')
        return next
      }, { replace: true })
      setPreview(null)
      return true
    } catch (failure) { setError(failure); return false }
    finally { gate.current = false; setBusy(false) }
  }
  return { preview: preview?.generation === runtime.generation ? preview : null, error, busy, pending, setPending, check, repair }
}
