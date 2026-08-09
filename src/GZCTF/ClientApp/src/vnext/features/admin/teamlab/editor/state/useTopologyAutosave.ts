import { useCallback, useEffect, useRef, useState } from 'react'
import type { TeamLabTopologyDetail } from '../../api'
import type { TopologyDocument } from '../../model/topologyDocument'
import { isTopologyRevisionConflict, type TopologySaveConflict } from './useSaveConflict'

export type TopologySaveStatus = 'saved' | 'dirty' | 'saving' | 'error' | 'conflict'

interface UseTopologyAutosaveOptions {
  initialRevision: number
  initialDocument?: TopologyDocument
  delay?: number
  save: (document: TopologyDocument, revision: number) => Promise<TeamLabTopologyDetail>
  onSaved?: (detail: TeamLabTopologyDetail) => void
}

export function useTopologyAutosave({
  initialRevision,
  initialDocument,
  delay = 900,
  save,
  onSaved,
}: UseTopologyAutosaveOptions) {
  const revision = useRef(initialRevision)
  const latestDocument = useRef<TopologyDocument | null>(initialDocument ?? null)
  const pendingVersion = useRef(0)
  const savedVersion = useRef(0)
  const timer = useRef<number | null>(null)
  const inFlight = useRef<Promise<void> | null>(null)
  const blocked = useRef(false)
  const lastSaveSucceeded = useRef(true)
  const [status, setStatus] = useState<TopologySaveStatus>('saved')
  const [error, setError] = useState<unknown>(null)
  const [conflict, setConflict] = useState<TopologySaveConflict | null>(null)

  const clearTimer = useCallback(() => {
    if (timer.current === null) return
    window.clearTimeout(timer.current)
    timer.current = null
  }, [])

  const drain = useCallback(async () => {
    clearTimer()
    if (blocked.current || !latestDocument.current || savedVersion.current === pendingVersion.current) return
    if (inFlight.current) return inFlight.current

    const work = (async () => {
      while (!blocked.current && latestDocument.current && savedVersion.current !== pendingVersion.current) {
        const documentToSave = latestDocument.current
        const versionToSave = pendingVersion.current
        const expectedRevision = revision.current
        setStatus('saving')
        setError(null)
        try {
          const detail = await save(documentToSave, expectedRevision)
          lastSaveSucceeded.current = true
          revision.current = detail.revision
          savedVersion.current = versionToSave
          onSaved?.(detail)
        } catch (reason) {
          lastSaveSucceeded.current = false
          setError(reason)
          if (isTopologyRevisionConflict(reason)) {
            blocked.current = true
            setConflict({ localDocument: latestDocument.current, expectedRevision })
            setStatus('conflict')
          } else {
            setStatus('error')
          }
          return
        }
      }
      setStatus('saved')
    })().finally(() => {
      inFlight.current = null
    })

    inFlight.current = work
    return work
  }, [clearTimer, onSaved, save])

  const schedule = useCallback(
    (document: TopologyDocument) => {
      latestDocument.current = document
      pendingVersion.current += 1
      if (blocked.current) {
        setConflict((current) =>
          current ? { ...current, localDocument: document } : { localDocument: document, expectedRevision: revision.current }
        )
        return
      }
      setStatus('dirty')
      setError(null)
      clearTimer()
      timer.current = window.setTimeout(() => void drain(), delay)
    },
    [clearTimer, delay, drain]
  )

  const flush = useCallback(
    async (document?: TopologyDocument) => {
      if (document && document !== latestDocument.current) schedule(document)
      await drain()
      return lastSaveSucceeded.current && !blocked.current && savedVersion.current === pendingVersion.current
    },
    [drain, schedule]
  )

  useEffect(() => () => clearTimer(), [clearTimer])

  useEffect(() => {
    if (status === 'saved') return undefined
    const warn = (event: BeforeUnloadEvent) => event.preventDefault()
    window.addEventListener('beforeunload', warn)
    return () => window.removeEventListener('beforeunload', warn)
  }, [status])

  return { status, error, conflict, schedule, flush }
}
