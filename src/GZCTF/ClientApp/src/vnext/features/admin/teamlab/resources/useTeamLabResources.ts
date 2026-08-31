import { useDeferredValue, useEffect, useMemo, useState } from 'react'
import useSWR from 'swr'
import { teamLabResourceKeys, teamLabResourcesApi } from '../api'
import { useAdminCursorState } from '../../shared/useAdminCursorState'

const pageSize = 30

/** Device package catalog keyed by name search, mirroring the scene-library hook shape. */
export function useDevicePackageCatalog() {
  const [searchInput, setSearchInput] = useState('')
  const [search, setSearch] = useState('')
  const deferredSearch = useDeferredValue(searchInput.trim())
  const cursor = useAdminCursorState(`packages:${search}`)

  useEffect(() => {
    const timeout = window.setTimeout(() => setSearch(deferredSearch), 250)
    return () => window.clearTimeout(timeout)
  }, [deferredSearch])

  const key = useMemo(
    () => [...teamLabResourceKeys.devicePackages(search || null), cursor.cursor ?? '', pageSize] as const,
    [cursor.cursor, search]
  )
  const request = useSWR(
    key,
    () => teamLabResourcesApi.listDevicePackages({ name: search || undefined, after: cursor.cursor || undefined, limit: pageSize }),
    { keepPreviousData: true, revalidateOnFocus: true }
  )

  return {
    page: request.data,
    error: request.error,
    isLoading: !request.data && !request.error,
    isRefreshing: request.isValidating && Boolean(request.data),
    mutate: request.mutate,
    searchInput,
    setSearchInput,
    cursor,
  }
}

export function useConnectorRegistry() {
  const cursor = useAdminCursorState('connectors')
  const request = useSWR(
    useMemo(
      () => [...teamLabResourceKeys.connectors(), cursor.cursor ?? '', pageSize] as const,
      [cursor.cursor]
    ),
    () => teamLabResourcesApi.listConnectors({ after: cursor.cursor || undefined, limit: pageSize }),
    { keepPreviousData: true, revalidateOnFocus: true }
  )

  return {
    page: request.data,
    error: request.error,
    isLoading: !request.data && !request.error,
    isRefreshing: request.isValidating && Boolean(request.data),
    mutate: request.mutate,
    cursor,
  }
}

export function useNodeArtifactCache() {
  const cursor = useAdminCursorState('node-cache')
  const request = useSWR(
    useMemo(() => [...teamLabResourceKeys.nodeCache, cursor.cursor ?? '', pageSize] as const, [cursor.cursor]),
    () => teamLabResourcesApi.listNodeCache({ after: cursor.cursor || undefined, limit: pageSize }),
    { keepPreviousData: true, revalidateOnFocus: true }
  )

  return {
    page: request.data,
    error: request.error,
    isLoading: !request.data && !request.error,
    isRefreshing: request.isValidating && Boolean(request.data),
    mutate: request.mutate,
    cursor,
  }
}
