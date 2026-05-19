import useSWR from 'swr';

const fetcher = (url: string) => fetch(url).then(r => r.json());

export function useSubmissions(challengeId?: number, userId?: string) {
  const params = new URLSearchParams();
  if (challengeId) params.set('challengeId', String(challengeId));
  if (userId) params.set('userId', userId);
  const url = `/api/v1/submissions?${params}`;
  const { data, error, isLoading, mutate } = useSWR(url, fetcher);
  return { submissions: data, error, isLoading, mutate };
}
