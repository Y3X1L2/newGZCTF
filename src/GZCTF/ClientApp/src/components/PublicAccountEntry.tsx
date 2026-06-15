import { ActionIcon, Avatar, Menu, MenuDivider } from '@mantine/core'
import {
  mdiAccountCircleOutline,
  mdiAccountOffOutline,
  mdiCached,
  mdiLogout,
} from '@mdi/js'
import { Icon } from '@mdi/react'
import { FC } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useLocation } from 'react-router'
import { clearLocalCache } from '@Utils/Cache'
import { useLogOut, useUser } from '@Hooks/useUser'

export const PublicAccountEntry: FC = () => {
  const { user, error } = useUser()
  const logout = useLogOut()
  const location = useLocation()
  const { t } = useTranslation()
  const loggedIn = !!user && !error

  const avatar = user?.avatar ? (
    <Avatar alt="avatar" src={user.avatar} radius="md" size="md">
      {user.userName?.slice(0, 1) ?? 'U'}
    </Avatar>
  ) : (
    <Icon path={loggedIn ? mdiAccountCircleOutline : mdiAccountOffOutline} size={1.05} />
  )

  if (!loggedIn) {
    return (
      <ActionIcon
        component={Link}
        to={`/account/login?from=${location.pathname}`}
        aria-label={t('common.tab.account.login')}
        className="yy-public-account-entry"
      >
        {avatar}
      </ActionIcon>
    )
  }

  return (
    <Menu position="bottom-end" offset={12} width={220}>
      <Menu.Target>
        <ActionIcon
          aria-label={t('common.tab.account.profile')}
          className="yy-public-account-entry"
          data-authenticated="true"
          data-has-avatar={!!user?.avatar || undefined}
        >
          {avatar}
        </ActionIcon>
      </Menu.Target>
      <Menu.Dropdown>
        <Menu.Label>{user?.userName}</Menu.Label>
        <Menu.Item component={Link} to="/account/profile" leftSection={<Icon path={mdiAccountCircleOutline} size={1} />}>
          {t('common.tab.account.profile')}
        </Menu.Item>
        <Menu.Item onClick={clearLocalCache} leftSection={<Icon path={mdiCached} size={1} />}>
          {t('common.tab.account.clean_cache')}
        </Menu.Item>
        <MenuDivider />
        <Menu.Item color="red" onClick={logout} leftSection={<Icon path={mdiLogout} size={1} />}>
          {t('common.tab.account.logout')}
        </Menu.Item>
      </Menu.Dropdown>
    </Menu>
  )
}
