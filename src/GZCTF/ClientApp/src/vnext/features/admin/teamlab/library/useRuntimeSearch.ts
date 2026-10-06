import { useMemo } from 'react'
import { useSearchParams } from 'react-router'
import useSWR from 'swr'
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
  const cursorStack = params.getAll('after')
  const cursor = {
    cursor: cursorStack.at(-1) ?? null,
    page: cursorStack.length + 1,
    canGoBack: cursorStack.length > 0,
    next: (nextCursor: string) => {
      setParams(current => {
        const next = new URLSearchParams(current)
        next.append('after', nextCursor)
        return next
      })
      window.scrollTo({ top: 0, behavior: 'smooth' })
    },
    previous: () => {
      setParams(current => {
        const next = new URLSearchParams(current)
        const stack = next.getAll('after')
        next.delete('after')
        stack.slice(0, -1).forEach(value => next.append('after', value))
        return next
      })
      window.scrollTo({ top: 0, behavior: 'smooth' })
    },
  }
  const request = useSWR(['teamlab:runtime-search', filters, cursor.cursor], () => searchRuntimes(filters, cursor.cursor),
    { keepPreviousData: false, revalidateOnFocus: false, shouldRetryOnError: false })
  const setFilters = (next: RuntimeSearchFilters) => {
    const query = new URLSearchParams()
    for (const [key, value] of Object.entries(next)) if (value !== '' && value !== false) query.set(key, String(value))
    setParams(query)
  }
  return { ...request, cursor, filters, setFilters }
}
