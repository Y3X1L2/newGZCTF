import { showNotification } from '@mantine/notifications'
import { mdiCheck, mdiClose } from '@mdi/js'
import { Icon } from '@mdi/react'
import { useTranslation } from 'react-i18next'
import { useNavigate } from 'react-router'
import { useSWRConfig } from 'swr'
import api from '@Api'

const shouldClearOnLogout = (key: unknown) =>
  typeof key === 'string' &&
  [
    '/api/account/',
    '/api/team',
    '/api/admin/',
    '/api/training/',
    '/api/game/',
    'game/',
    'team',
    'training',
  ].some((scope) => key.includes(scope))

export const useUser = () => {
  const navigate = useNavigate()
  const { t } = useTranslation()

  const {
    data: user,
    error,
    mutate,
  } = api.account.useAccountProfile({
    refreshInterval: 0,
    shouldRetryOnError: false,
    revalidateOnFocus: false,
    onErrorRetry: async (err, _key, _config, revalidate, { retryCount }) => {
      if (err?.status === 403) {
        await api.account.accountLogOut()
        navigate('/')
        showNotification({
          color: 'red',
          message: t('account.notification.login.banned'),
          icon: <Icon path={mdiClose} size={1} />,
        })
        return
      }

      if (err?.status === 401 || retryCount >= 5) {
        mutate(undefined, false)
        return
      }

      setTimeout(() => revalidate({ retryCount: retryCount }), 10000)
    },
  })

  return { user, error, mutate }
}

export const useUserRole = () => {
  const { user, error } = useUser()
  return { role: user?.role, error }
}

export const useTeams = () => {
  const {
    data: teams,
    error,
    mutate,
  } = api.team.useTeamGetTeamsInfo({
    refreshInterval: 120000,
    shouldRetryOnError: false,
    revalidateOnFocus: false,
  })

  return { teams, error, mutate }
}

export const useLogOut = () => {
  const navigate = useNavigate()
  const { mutate } = useSWRConfig()
  const { mutate: mutateProfile } = useUser()
  const { t } = useTranslation()

  return async () => {
    try {
      await api.account.accountLogOut()
      navigate('/')
      mutate(shouldClearOnLogout, undefined, { revalidate: false })
      mutateProfile(undefined, { revalidate: false })
      showNotification({
        color: 'teal',
        message: t('account.notification.logout'),
        icon: <Icon path={mdiCheck} size={1} />,
      })
    } catch {
      navigate('/')
      mutate(shouldClearOnLogout, undefined, { revalidate: false })
      mutateProfile(undefined, { revalidate: false })
    }
  }
}
