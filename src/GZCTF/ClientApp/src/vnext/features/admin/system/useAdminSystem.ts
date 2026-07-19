import useSWR from 'swr'
import { commonAdminApi, commonAdminKeys } from '../api'

export function useAdminSystem() {
  const request = useSWR(commonAdminKeys.systemConfig, () => commonAdminApi.systemConfig(), {
    revalidateOnFocus: false,
  })

  return {
    config: request.data,
    error: request.error,
    isLoading: !request.data && !request.error,
    isRefreshing: request.isValidating,
    mutate: request.mutate,
  }
}
