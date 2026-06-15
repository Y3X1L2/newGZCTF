import {
  ActionIcon,
  AppShell,
  Avatar,
  Menu,
  MenuDivider,
  Popover,
  Stack,
} from '@mantine/core'
import {
  mdiAccountCircleOutline,
  mdiAccountGroupOutline,
  mdiCached,
  mdiFlagOutline,
  mdiHomeVariantOutline,
  mdiInformationOutline,
  mdiLogin,
  mdiLogout,
  mdiNoteTextOutline,
  mdiWrenchOutline,
  mdiTransitConnectionVariant,
} from '@mdi/js'
import { Icon } from '@mdi/react'
import cx from 'clsx'
import React, { FC, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useLocation } from 'react-router'
import { LogoBox } from '@Components/LogoBox'
import { WsrxManager } from '@Components/WsrxManager'
import { clearLocalCache } from '@Utils/Cache'
import { useConfig } from '@Hooks/useConfig'
import { useLogOut, useUser } from '@Hooks/useUser'
import { ContainerPortMappingType, Role } from '@Api'
import classes from '@Styles/AppNavbar.module.css'
import misc from '@Styles/Misc.module.css'

interface NavbarItem {
  icon: string
  label: string
  link: string
  admin?: boolean
}

export interface NavbarLinkProps {
  icon: string
  label: string
  link?: string
  onClick?: () => void
  isActive?: boolean
  drawerActive?: boolean
  onHover?: () => void
}

const NavbarLink: FC<NavbarLinkProps> = (props: NavbarLinkProps) => {
  const { t } = useTranslation()

  return (
    <ActionIcon
      onClick={props.onClick}
      onPointerEnter={props.onHover}
      component={Link}
      to={props.link ?? '#'}
      data-active={props.isActive || undefined}
      data-drawer={props.drawerActive || undefined}
      className={cx(classes.link, classes.navLink, 'rail-button')}
    >
      <Icon path={props.icon} size={1} />
      <span className={classes.drawerLabel}>{t(props.label)}</span>
    </ActionIcon>
  )
}

export const AppNavbar: FC = () => {
  const location = useLocation()

  const logout = useLogOut()
  const { user, error } = useUser()
  const { config } = useConfig()
  const { t } = useTranslation()

  const items: NavbarItem[] = [
    { icon: mdiHomeVariantOutline, label: 'common.tab.home', link: '/' },
    { icon: mdiNoteTextOutline, label: 'common.tab.post', link: '/posts' },
    { icon: mdiFlagOutline, label: 'common.tab.game', link: '/games' },
    { icon: mdiAccountGroupOutline, label: 'common.tab.team', link: '/teams' },
    { icon: mdiInformationOutline, label: 'common.tab.about', link: '/about' },
    { icon: mdiWrenchOutline, label: 'common.tab.admin', link: '/admin/games', admin: true },
  ]

  const getLabel = (path: string) =>
    items.find((item) =>
      item.link === '/'
        ? path === '/'
        : item.link.startsWith('/admin')
          ? path.startsWith('/admin')
          : path.startsWith(item.link)
    )?.label

  const [active, setActive] = useState(getLabel(location.pathname) ?? '')
  const [hoverIndex, setHoverIndex] = useState<number | null>(null)

  useEffect(() => {
    if (location.pathname === '/') {
      setActive(items[0].label)
    } else {
      setActive(getLabel(location.pathname) ?? '')
    }
  }, [location.pathname])

  const visibleItems = items
    .filter((m) => !m.admin || user?.role === Role.Admin)
  const links = visibleItems.map((link, index) => (
    <NavbarLink
      key={link.label}
      {...link}
      isActive={link.label === active}
      drawerActive={hoverIndex !== null && Math.abs(index - hoverIndex) <= 1}
      onHover={() => setHoverIndex(index)}
    />
  ))

  const loggedIn = user && !error

  return (
    <AppShell.Navbar className={classes.navbar}>
      <AppShell.Section className={classes.brandSection}>
        <LogoBox size="58px" className={classes.logo} component={Link} to="/" />
      </AppShell.Section>

      <AppShell.Section className={classes.section} onPointerLeave={() => setHoverIndex(null)}>
        {links}
      </AppShell.Section>

      <AppShell.Section className={cx(classes.section, classes.utilitySection, misc.justifyEnd)}>
        <Stack w="100%" align="center" justify="center" gap={5}>
          {/* WebSocket Reflector X Integration */}
          {config.portMapping === ContainerPortMappingType.PlatformProxy && (
            <Popover position="right" offset={24} width={320}>
              <Popover.Target>
                <ActionIcon className={cx(classes.link, 'rail-button')}>
                  <Icon path={mdiTransitConnectionVariant} size={1} />
                </ActionIcon>
              </Popover.Target>
              <Popover.Dropdown>
                <WsrxManager />
              </Popover.Dropdown>
            </Popover>
          )}

          {/* User Info */}
          <Menu position="right-end" offset={24}>
            <Menu.Target>
              <ActionIcon className={cx(classes.link, 'rail-button')}>
                {user?.avatar ? (
                  <Avatar alt="avatar" src={user?.avatar} radius="md" size="md">
                    {user.userName?.slice(0, 1) ?? 'U'}
                  </Avatar>
                ) : (
                  <Icon path={mdiAccountCircleOutline} size={1} />
                )}
              </ActionIcon>
            </Menu.Target>
            <Menu.Dropdown>
              {loggedIn && (
                <>
                  <Menu.Label>{user?.userName}</Menu.Label>
                  <Menu.Item
                    component={Link}
                    to="/account/profile"
                    leftSection={<Icon path={mdiAccountCircleOutline} size={1} />}
                  >
                    {t('common.tab.account.profile')}
                  </Menu.Item>
                </>
              )}
              <Menu.Item onClick={clearLocalCache} leftSection={<Icon path={mdiCached} size={1} />}>
                {t('common.tab.account.clean_cache')}
              </Menu.Item>
              <MenuDivider />
              {loggedIn ? (
                <Menu.Item color="red" onClick={logout} leftSection={<Icon path={mdiLogout} size={1} />}>
                  {t('common.tab.account.logout')}
                </Menu.Item>
              ) : (
                <Menu.Item
                  component={Link}
                  to={`/account/login?from=${location.pathname}`}
                  leftSection={<Icon path={mdiLogin} size={1} />}
                >
                  {t('common.tab.account.login')}
                </Menu.Item>
              )}
            </Menu.Dropdown>
          </Menu>
        </Stack>
      </AppShell.Section>
    </AppShell.Navbar>
  )
}
