import { useEffect, useRef, useState } from 'react'
import useSWR from 'swr'
import { teamLabRuntimeApi, teamLabRuntimeKeys, type TeamLabRuntimeEvent, type TeamLabRuntimeStatus } from '../api'
import { runtimeRefreshInterval } from './runtimePresentation'

const eventLimit = 200
// Bound accumulated events: the panel renders the newest slice; keeping unbounded
// history would grow memory and request chains linearly on long-running runtimes.
const maxEvents = 500

export interface TeamLabEventFilters {
  generation: number | null
  stage: string
}

export const emptyTeamLabEventFilters = (): TeamLabEventFilters => ({ generation: null, stage: '' })

export function useRuntimeEvents(
  runtimeId: string,
  status: TeamLabRuntimeStatus | undefined,
  filters: TeamLabEventFilters
) {
  const identity = `${runtimeId}:${filters.generation ?? ''}:${filters.stage}`
  const identityRef = useRef(identity)
  identityRef.current = identity
  // Cursor state is keyed by identity: a filter switch derives cursor 0 in the same
  // render, so no request is ever issued with a stale (new filter, old cursor) pair.
  const [page, setPage] = useState({ identity, cursor: 0 })
  const cursor = page.identity === identity ? page.cursor : 0
  const [events, setEvents] = useState<readonly TeamLabRuntimeEvent[]>([])

  useEffect(() => {
    setEvents([])
  }, [identity])

  const request = useSWR(
    runtimeId
      ? [...teamLabRuntimeKeys.events(runtimeId), filters.generation ?? 0, filters.stage, cursor, eventLimit]
      : null,
    () =>
      teamLabRuntimeApi.listEvents(runtimeId, cursor, eventLimit, {
        generation: filters.generation,
        stage: filters.stage,
      }),
    {
      // No keepPreviousData: the previous filter's page must never be merged into
      // the current identity's event list.
      revalidateOnFocus: true,
      refreshInterval: () => runtimeRefreshInterval(status),
    }
  )

  useEffect(() => {
    const pageData = request.data
    if (!pageData?.length) return
    // Double guard: keepPreviousData is off, but never merge or advance for an
    // identity that changed while the request was in flight.
    if (identityRef.current !== identity) return
    setEvents((current) => {
      const merged = new Map(current.map((event) => [event.cursor, event]))
      pageData.forEach((event) => merged.set(event.cursor, event))
      return [...merged.values()].sort((left, right) => left.cursor - right.cursor).slice(-maxEvents)
    })
    // A full page means more history exists: advance the cursor so the next page
    // is fetched with a new cache key instead of an unbounded mutate chain.
    if (pageData.length === eventLimit) {
      const nextCursor = Math.max(cursor, ...pageData.map((event) => event.cursor))
      setPage((current) => (current.identity === identity && current.cursor === nextCursor ? current : { identity, cursor: nextCursor }))
    }
  }, [cursor, identity, request.data])

  return {
    events,
    error: request.error,
    isLoading: !request.data && !request.error,
    isRefreshing: request.isValidating && Boolean(request.data),
    mutate: request.mutate,
  }
}
