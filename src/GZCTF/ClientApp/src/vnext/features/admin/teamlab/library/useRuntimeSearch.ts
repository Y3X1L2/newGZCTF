import { useMemo } from 'react'
import { useSearchParams } from 'react-router'
import useSWR from 'swr'
import { useAdminCursorState } from '../../shared/useAdminCursorState'
import { searchRuntimes, type RuntimeSearchFilters } from '../api/teamlabRuntimeSearchApi'

export const emptyRuntimeSearch: RuntimeSearchFilters = {
  search: '', status: '', node: '', generation: '', releaseId: '', createdById: '', errorsOnly: false,
}
export function useRuntimeSearch() {
  const [params, setParams] = useSearchParams()
  const filters = useMemo(() => ({
    search: params.get('search') ?? '',
    status: params.get('status') ?? '',
    node: params.get('node') ?? '',
    generation: params.get('generation') ?? '',
    releaseId: params.get('releaseId') ?? '',
    createdById: params.get('createdById') ?? '',
    errorsOnly: params.get('errorsOnly') === 'true',
  }), [params])
  const cursor = useAdminCursorState(JSON.stringify(filters))
  const request = useSWR(['teamlab:runtime-search', filters, cursor.cursor], () => searchRuntimes(filters, cursor.cursor),
    { keepPreviousData: false, revalidateOnFocus: false, shouldRetryOnError: false })
  const setFilters = (next: RuntimeSearchFilters) => {
    const query = new URLSearchParams()
    for (const [key, value] of Object.entries(next)) if (value !== '' && value !== false) query.set(key, String(value))
    setParams(query)
  }
  return { ...request, cursor, filters, setFilters }
}
