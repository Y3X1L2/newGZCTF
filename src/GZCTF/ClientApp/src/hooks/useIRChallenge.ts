import useSWR from 'swr';
import type { IRChallengeSummary } from '../types/ir';

const fetcher = (url: string) => fetch(url).then(r => r.json());

export function useIRChallenges(gameId?: number) {
  const url = gameId ? `/api/v1/ir-challenges?gameId=${gameId}` : '/api/v1/ir-challenges';
  const { data, error, isLoading, mutate } = useSWR<IRChallengeSummary[]>(url, fetcher);
  return { challenges: data, error, isLoading, mutate };
}
