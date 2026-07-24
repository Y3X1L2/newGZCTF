import { useNavigate } from 'react-router'
import { useSWRConfig } from 'swr'
import api, { Role } from '@Api'
import { clearAccountSessionCache } from './sessionCache'

const accountRequestOptions = {
  refreshInterval: 0,
  keepPreviousData: false,
  revalidateOnFocus: false,
  shouldRetryOnError: false,
}

export function useCurrentAccount() {
  const { data: user, error, mutate } = api.account.useAccountProfile(accountRequestOptions)

  return {
    user,
    error,
    isAuthenticated: Boolean(user),
    isAdmin: user?.role === Role.Admin || user?.role === Role.SuperAdmin,
    isTeacher: user?.role === Role.Teacher || user?.role === Role.Admin || user?.role === Role.SuperAdmin,
    mutate,
  }
}

export function useAccountLogout() {
  const navigate = useNavigate()
  const { mutate } = useSWRConfig()
  const account = useCurrentAccount()

  return async ({ redirectTo = '/' }: { redirectTo?: string } = {}) => {
    try {
      await api.account.accountLogOut()
    } finally {
      await clearAccountSessionCache(account.mutate, mutate)
      navigate(redirectTo, { replace: true })
    }
  }
}

export function roleLabel(role?: Role) {
  switch (role) {
    case Role.SuperAdmin:
      return '超级管理员'
    case Role.Admin:
      return '管理员'
    case Role.Teacher:
      return '教师'
    case Role.Banned:
      return '已停用'
    default:
      return '学员'
  }
}
