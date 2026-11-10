import { useInfiniteQuery } from '@tanstack/react-query'
import { useMemo } from 'react'

export const pageSize = 100
export type PageRequest = {
  cursor: string
  limit: number
  signal: AbortSignal
}

export function useCursorList<T extends { id: string }>(
  queryKey: readonly unknown[],
  fetchPage: (request: PageRequest) => Promise<T[]>,
  options: {
    enabled?: boolean
    refetchInterval?: number | false | ((items: T[]) => number | false)
  } = {}
) {
  const query = useInfiniteQuery({
    queryKey,
    queryFn: ({ pageParam, signal }) => fetchPage({ cursor: pageParam, limit: pageSize, signal }),
    initialPageParam: '',
    getNextPageParam: (lastPage) => (lastPage.length === pageSize ? lastPage.at(-1)!.id : undefined),
    enabled: options.enabled,
    refetchInterval: (query) =>
      typeof options.refetchInterval === 'function'
        ? options.refetchInterval(query.state.data?.pages.flat() ?? [])
        : options.refetchInterval,
  })
  const data = useMemo(() => query.data?.pages.flat(), [query.data?.pages])
  return { ...query, data }
}
