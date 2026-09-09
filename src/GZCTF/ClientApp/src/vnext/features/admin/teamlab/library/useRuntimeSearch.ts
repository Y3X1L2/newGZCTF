import { useState } from 'react'
import useSWR from 'swr'
import { useAdminCursorState } from '../../shared/useAdminCursorState'
import { searchRuntimes, type RuntimeSearchFilters } from '../api/teamlabRuntimeSearchApi'

export const emptyRuntimeSearch: RuntimeSearchFilters = {
  search: '', status: '', node: '', generation: '', releaseId: '', createdById: '', errorsOnly: false,
}
export function useRuntimeSearch() {
  const [filters, setFilters] = useState(emptyRuntimeSearch)
  const cursor = useAdminCursorState(JSON.stringify(filters))
  const request = useSWR(['teamlab:runtime-search', filters, cursor.cursor], () => searchRuntimes(filters, cursor.cursor),
    { keepPreviousData: false, revalidateOnFocus: false, shouldRetryOnError: false })
  return { ...request, cursor, setFilters }
}
