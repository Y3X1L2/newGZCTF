import {
  ActionIcon,
  AppShell,
  Popover,
  Stack,
} from '@mantine/core'
import {
  mdiAccountGroupOutline,
  mdiFlagOutline,
  mdiHomeVariantOutline,
  mdiInformationOutline,
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
import { useConfig } from '@Hooks/useConfig'
import { useUser } from '@Hooks/useUser'
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

  const { user } = useUser()
  const { config } = useConfig()

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
        </Stack>
      </AppShell.Section>
    </AppShell.Navbar>
  )
}
