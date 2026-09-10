import { useState } from 'react'
import useSWR from 'swr'
import { readTaskHistory } from '../api/teamlabTaskHistoryApi'

export function useTaskHistory(runtimeId: string, generation: number) {
  const [currentOnly, setCurrentOnly] = useState(false)
  const [cursors, setCursors] = useState<string[]>([])
  const query = useSWR(['teamlab:task-history', runtimeId, currentOnly ? generation : null, cursors.at(-1)],
    () => readTaskHistory(runtimeId, currentOnly ? generation : null, cursors.at(-1)),
    { keepPreviousData: false, refreshInterval: cursors.length ? 0 : 5000, shouldRetryOnError: false })
  return {
    ...query, currentOnly, page: cursors.length + 1,
    setCurrentOnly: (value: boolean) => { setCurrentOnly(value); setCursors([]) },
    previous: () => setCursors(value => value.slice(0, -1)),
    next: () => { if (query.data?.nextCursor) setCursors(value => [...value, query.data!.nextCursor!]) },
  }
}
