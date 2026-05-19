import useSWR from 'swr';

const fetcher = (url: string) => fetch(url).then(r => r.json());

export function useScenarios(gameId?: number) {
  const url = gameId ? `/api/v1/scenarios?gameId=${gameId}` : '/api/v1/scenarios';
  const { data, error, isLoading, mutate } = useSWR(url, fetcher);
  return { scenarios: data, error, isLoading, mutate };
}
