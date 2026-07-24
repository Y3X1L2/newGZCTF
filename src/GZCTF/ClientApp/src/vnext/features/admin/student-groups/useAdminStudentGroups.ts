import useSWR from 'swr'
import { commonAdminApi, commonAdminKeys } from '../api'

export function useAdminStudentGroups(keyword: string, includeArchived: boolean) {
  const request = useSWR(
    commonAdminKeys.studentGroups(keyword, includeArchived),
    () => commonAdminApi.studentGroups(keyword, includeArchived),
    { keepPreviousData: true, revalidateOnFocus: false }
  )

  return {
    groups: request.data,
    error: request.error,
    isLoading: !request.data && !request.error,
    isRefreshing: request.isValidating,
    mutate: request.mutate,
  }
}

export function useAdminStudentGroup(groupId: number | null) {
  const request = useSWR(
    groupId ? commonAdminKeys.studentGroup(groupId) : null,
    () => commonAdminApi.studentGroup(groupId!),
    { revalidateOnFocus: false }
  )

  return {
    group: request.data,
    error: request.error,
    isLoading: Boolean(groupId && !request.data && !request.error),
    isRefreshing: request.isValidating,
    mutate: request.mutate,
  }
}
