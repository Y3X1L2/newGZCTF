import {
  ActionIcon,
  AppShell,
  Popover,
  Stack,
} from '@mantine/core'
import {
  mdiAccountGroupOutline,
  mdiBookOpenPageVariantOutline,
  mdiFlagOutline,
  mdiHomeVariantOutline,
  mdiInformationOutline,
  mdiNoteTextOutline,
  mdiTransitConnectionVariant,
  mdiWrenchOutline,
} from '@mdi/js'
import { Icon } from '@mdi/react'
import cx from 'clsx'
import React, { FC, useEffect, useRef, useState } from 'react'
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
  auth?: boolean
  teacher?: boolean
}

export interface NavbarLinkProps {
  icon: string
  label: string
  link?: string
  onClick?: () => void
  isActive?: boolean
  drawerActive?: boolean
  navIndex?: number
}

const NavbarLink: FC<NavbarLinkProps> = (props: NavbarLinkProps) => {
  const { t } = useTranslation()

  return (
    <ActionIcon
      onClick={props.onClick}
      component={Link}
      to={props.link ?? '#'}
      aria-label={t(props.label)}
      data-nav-index={props.navIndex}
      data-active={props.isActive || undefined}
      data-drawer={props.drawerActive || undefined}
      className={cx(classes.link, classes.navLink, 'rail-button')}
    >
      <span className={classes.navIcon}>
        <Icon path={props.icon} size={1} />
      </span>
      <span className={classes.drawerLabel}>{t(props.label)}</span>
    </ActionIcon>
  )
}

export const AppNavbar: FC = () => {
  const location = useLocation()

  const { user } = useUser()
  const { config } = useConfig()
  const isTeacherOrAbove =
    user?.role === Role.Teacher ||
    user?.role === Role.Monitor ||
    user?.role === Role.Admin ||
    user?.role === Role.SuperAdmin

  const items: NavbarItem[] = [
    { icon: mdiHomeVariantOutline, label: 'common.tab.home', link: '/' },
    { icon: mdiNoteTextOutline, label: 'common.tab.post', link: '/posts' },
    { icon: mdiFlagOutline, label: 'common.tab.game', link: '/games' },
    { icon: mdiAccountGroupOutline, label: 'common.tab.team', link: '/teams' },
    { icon: mdiBookOpenPageVariantOutline, label: '培训', link: '/training', auth: true },
    { icon: mdiInformationOutline, label: 'common.tab.about', link: '/about' },
    { icon: mdiWrenchOutline, label: 'common.tab.admin', link: '/admin/games', teacher: true },
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
  const hoverFrameRef = useRef<number | null>(null)

  useEffect(() => {
    if (location.pathname === '/') {
      setActive(items[0].label)
    } else {
      setActive(getLabel(location.pathname) ?? '')
    }
  }, [location.pathname])

  const visibleItems = items
    .filter((m) => !m.auth || !!user)
    .filter((m) => !m.teacher || isTeacherOrAbove)

  useEffect(() => {
    const updateNearbyItem = (pointerX: number, pointerY: number) => {
      const buttons = Array.from(
        document.querySelectorAll<HTMLElement>(`.${classes.navItemSection} [data-nav-index]`)
      )
      let nextIndex: number | null = null
      let nearestDistance = Number.POSITIVE_INFINITY

      for (const button of buttons) {
        const rect = button.getBoundingClientRect()
        const centerY = rect.top + rect.height / 2
        const yDistance = Math.abs(pointerY - centerY)
        const isInsideHotX = pointerX >= rect.left - 36 && pointerX <= rect.right + 168

        if (!isInsideHotX || yDistance > 68) {
          continue
        }

        if (yDistance < nearestDistance) {
          nearestDistance = yDistance
          nextIndex = Number(button.dataset.navIndex)
        }
      }

      setHoverIndex((index) => (index === nextIndex ? index : nextIndex))
    }

    const onPointerMove = (event: PointerEvent) => {
      if (hoverFrameRef.current) {
        window.cancelAnimationFrame(hoverFrameRef.current)
      }

      hoverFrameRef.current = window.requestAnimationFrame(() => {
        hoverFrameRef.current = null
        updateNearbyItem(event.clientX, event.clientY)
      })
    }

    window.addEventListener('pointermove', onPointerMove, { passive: true })

    return () => {
      window.removeEventListener('pointermove', onPointerMove)

      if (hoverFrameRef.current) {
        window.cancelAnimationFrame(hoverFrameRef.current)
        hoverFrameRef.current = null
      }
    }
  }, [visibleItems.length])

  const links = visibleItems.map((link, index) => (
    <NavbarLink
      key={link.label}
      {...link}
      isActive={link.label === active}
      drawerActive={hoverIndex !== null && Math.abs(index - hoverIndex) <= 1}
      navIndex={index}
    />
  ))

  return (
    <AppShell.Navbar className={classes.navbar}>
      <AppShell.Section className={classes.brandSection}>
        <LogoBox size="58px" className={classes.logo} component={Link} to="/" />
      </AppShell.Section>

      <AppShell.Section className={cx(classes.section, classes.navItemSection)}>
        {links}
      </AppShell.Section>

      <AppShell.Section className={cx(classes.section, classes.utilitySection, misc.justifyEnd)}>
        <Stack w="100%" align="center" justify="center" gap={5}>
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
