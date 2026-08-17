import useSWR from 'swr'
import { teamLabRuntimeApi, teamLabRuntimeKeys } from '../api'

export type TeamLabLinkPolicyStatusFilter = '' | 'active' | 'recovered' | 'failed'

export function useRuntimeLinkPolicies(runtimeId: string, status: TeamLabLinkPolicyStatusFilter) {
  const request = useSWR(
    runtimeId
      ? ([...teamLabRuntimeKeys.linkPolicies(runtimeId), status] as const)
      : null,
    () => teamLabRuntimeApi.listLinkPolicies(runtimeId, { status: status || undefined }),
    { keepPreviousData: true, revalidateOnFocus: true }
  )

  return {
    policies: request.data?.items ?? [],
    page: request.data,
    error: request.error,
    isLoading: !request.data && !request.error,
    isRefreshing: request.isValidating && Boolean(request.data),
    mutate: request.mutate,
  }
}
