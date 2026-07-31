import { useEffect, useRef, useState } from 'react'
import useSWR from 'swr'
import { teamLabRuntimeApi, teamLabRuntimeKeys, type TeamLabRuntimeEvent, type TeamLabRuntimeStatus } from '../api'
import { runtimeRefreshInterval } from './runtimePresentation'

const eventLimit = 200

export function useRuntimeEvents(runtimeId: string, status: TeamLabRuntimeStatus | undefined, generation = 0) {
  const identity = `${runtimeId}:${generation}`
  const cursor = useRef({ identity, value: 0 })
  const [events, setEvents] = useState<readonly TeamLabRuntimeEvent[]>([])
  if (cursor.current.identity !== identity) cursor.current = { identity, value: 0 }
  const request = useSWR(
    runtimeId ? [...teamLabRuntimeKeys.events(runtimeId), generation, eventLimit] : null,
    () => teamLabRuntimeApi.listEvents(runtimeId, cursor.current.value, eventLimit),
    {
      keepPreviousData: true,
      revalidateOnFocus: true,
      refreshInterval: () => runtimeRefreshInterval(status),
    }
  )

  useEffect(() => {
    setEvents([])
  }, [identity])

  useEffect(() => {
    if (!request.data?.length) return
    const page = request.data
    cursor.current.value = Math.max(cursor.current.value, ...page.map((event) => event.cursor))
    setEvents((current) => {
      const merged = new Map(current.map((event) => [event.cursor, event]))
      page.forEach((event) => merged.set(event.cursor, event))
      return [...merged.values()].sort((left, right) => left.cursor - right.cursor)
    })
    if (page.length === eventLimit) void request.mutate()
  }, [request.data, request.mutate])

  return {
    events,
    error: request.error,
    isLoading: !request.data && !request.error,
    isRefreshing: request.isValidating && Boolean(request.data),
    mutate: request.mutate,
  }
}
