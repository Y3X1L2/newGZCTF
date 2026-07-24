import useSWR from 'swr'
import { Role } from '@Api'
import { commonAdminApi, commonAdminKeys } from '../api'

export interface AdminUserFilters {
  page: number
  pageSize: number
  keyword?: string
  role?: Role
  groupId?: number
}

export function useAdminUsers(filters: AdminUserFilters) {
  const request = useSWR(commonAdminKeys.users(filters), () => commonAdminApi.users(filters), {
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

export function useStudentGroupOptions() {
  const request = useSWR(commonAdminKeys.studentGroups('', false), () => commonAdminApi.studentGroups(), {
    revalidateOnFocus: false,
  })

  return {
    groups: request.data ?? [],
    error: request.error,
    isLoading: !request.data && !request.error,
    mutate: request.mutate,
  }
}
