import useSWR from 'swr'
import { commonAdminApi, commonAdminKeys, type AdminTeamListQuery } from '../api'

export function useAdminTeams(filters: AdminTeamListQuery) {
  const request = useSWR(commonAdminKeys.teams(filters), () => commonAdminApi.teams(filters), {
    keepPreviousData: true,
    revalidateOnFocus: false,
  })

  return {
    page: request.data,
    error: request.error,
    isLoading: !request.data && !request.error,
    isRefreshing: request.isValidating,
    mutate: request.mutate,
  }
}
