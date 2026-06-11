import useSWR from 'swr'

const fetcher = (url: string) => fetch(url).then((r) => r.json())

export function useGamePhases(gameId: number) {
  const { data, error, isLoading, mutate } = useSWR(`/api/v1/phases/${gameId}`, fetcher)
  return { phases: data, error, isLoading, mutate }
}
