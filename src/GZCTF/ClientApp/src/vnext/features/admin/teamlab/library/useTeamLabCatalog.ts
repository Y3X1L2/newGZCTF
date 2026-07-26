import { useDeferredValue, useEffect, useMemo, useState } from 'react'
import useSWR from 'swr'
import { teamLabAdminApi, teamLabAdminKeys } from '../api'
import { useAdminCursorState } from '../../shared/useAdminCursorState'

export type TeamLabSceneStatusFilter = '' | 'draft' | 'published' | 'running' | 'failed'
export type TeamLabSceneOwnerFilter = '' | 'mine'

const pageSize = 30

export function useTeamLabCatalog() {
  const [searchInput, setSearchInput] = useState('')
  const [search, setSearch] = useState('')
  const [status, setStatus] = useState<TeamLabSceneStatusFilter>('')
  const [owner, setOwner] = useState<TeamLabSceneOwnerFilter>('')
  const deferredSearch = useDeferredValue(searchInput.trim())
  const scopeKey = `${search}|${status}|${owner}`
  const cursor = useAdminCursorState(scopeKey)

  useEffect(() => {
    const timeout = window.setTimeout(() => setSearch(deferredSearch), 250)
    return () => window.clearTimeout(timeout)
  }, [deferredSearch])

  const key = useMemo(
    () => [...teamLabAdminKeys.topologies, search, status, owner, cursor.cursor ?? '', pageSize] as const,
    [cursor.cursor, owner, search, status]
  )
  const request = useSWR(
    key,
    () =>
      teamLabAdminApi.listTopologies({
        search: search || undefined,
        status: status || undefined,
        owner: owner || undefined,
        cursor: cursor.cursor || undefined,
        limit: pageSize,
      }),
    {
      keepPreviousData: true,
      revalidateOnFocus: true,
      refreshInterval: 0,
    }
  )

  return {
    page: request.data,
    error: request.error,
    isLoading: !request.data && !request.error,
    isRefreshing: request.isValidating && Boolean(request.data),
    mutate: request.mutate,
    searchInput,
    setSearchInput,
    status,
    setStatus,
    owner,
    setOwner,
    cursor,
  }
}
