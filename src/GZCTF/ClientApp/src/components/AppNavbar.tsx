import {
  ActionIcon,
  AppShell,
  Popover,
  Stack,
} from '@mantine/core'
import {
  mdiAccountGroupOutline,
  mdiChevronLeft,
  mdiChevronRight,
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

const navbarExpandedKey = 'gzctf:navbar-expanded'

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
  expanded?: boolean
}

const NavbarLink: FC<NavbarLinkProps> = (props: NavbarLinkProps) => {
  const { t } = useTranslation()

  return (
    <ActionIcon
      onClick={props.onClick}
      component={Link}
      to={props.link ?? '#'}
      aria-label={t(props.label)}
      data-active={props.isActive || undefined}
      data-expanded={props.expanded || undefined}
      className={cx(classes.link, classes.navLink, 'rail-button')}
    >
      <Icon path={props.icon} size={1} />
      <span className={classes.navLabel}>{t(props.label)}</span>
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
  const [expanded, setExpanded] = useState(() => {
    if (typeof window === 'undefined') return false
    return window.localStorage.getItem(navbarExpandedKey) === 'true'
  })

  useEffect(() => {
    if (location.pathname === '/') {
      setActive(items[0].label)
    } else {
      setActive(getLabel(location.pathname) ?? '')
    }
  }, [location.pathname])

  useEffect(() => {
    window.localStorage.setItem(navbarExpandedKey, String(expanded))
  }, [expanded])

  const visibleItems = items
    .filter((m) => !m.admin || user?.role === Role.Admin)
  const links = visibleItems.map((link) => (
    <NavbarLink
      key={link.label}
      {...link}
      isActive={link.label === active}
      expanded={expanded}
    />
  ))

  return (
    <AppShell.Navbar className={classes.navbar} data-expanded={expanded || undefined}>
      <AppShell.Section className={classes.brandSection}>
        <LogoBox size="58px" className={classes.logo} component={Link} to="/" />
      </AppShell.Section>

      <AppShell.Section className={classes.section}>
        {links}
      </AppShell.Section>

      <AppShell.Section className={cx(classes.section, classes.utilitySection, misc.justifyEnd)}>
        <Stack w="100%" align="center" justify="center" gap={5}>
          {/* WebSocket Reflector X Integration */}
          {config.portMapping === ContainerPortMappingType.PlatformProxy && (
            <Popover position="right" offset={24} width={320}>
              <Popover.Target>
                <ActionIcon
                  aria-label="平台代理服务"
                  className={cx(classes.link, classes.navLink, 'rail-button')}
                  data-expanded={expanded || undefined}
                >
                  <Icon path={mdiTransitConnectionVariant} size={1} />
                  <span className={classes.navLabel}>{'代理服务'}</span>
                </ActionIcon>
              </Popover.Target>
              <Popover.Dropdown>
                <WsrxManager />
              </Popover.Dropdown>
            </Popover>
          )}
          <ActionIcon
            aria-label={expanded ? '收起侧边栏' : '展开侧边栏'}
            className={cx(classes.link, classes.navLink, 'rail-button')}
            data-expanded={expanded || undefined}
            onClick={() => setExpanded((value) => !value)}
          >
            <Icon path={expanded ? mdiChevronLeft : mdiChevronRight} size={0.92} />
            <span className={classes.navLabel}>{expanded ? '收起侧栏' : '展开侧栏'}</span>
          </ActionIcon>
        </Stack>
      </AppShell.Section>
    </AppShell.Navbar>
  )
}
