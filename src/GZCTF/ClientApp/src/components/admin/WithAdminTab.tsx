import { Group, GroupProps, ScrollArea, Tooltip } from '@mantine/core'
import {
  mdiAccountCogOutline,
  mdiAccountGroupOutline,
  mdiBookOpenPageVariantOutline,
  mdiClipboardListOutline,
  mdiFileDocumentOutline,
  mdiFlagOutline,
  mdiImageOutline,
  mdiSchoolOutline,
  mdiServerNetwork,
  mdiSitemapOutline,
} from '@mdi/js'
import { Icon } from '@mdi/react'
import React, { FC, useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useLocation, useNavigate } from 'react-router'
import { RequireRole } from '@Components/WithRole'
import { YinyuHexField } from '@Components/yinyu/YinyuUI'
import { usePageTitle } from '@Hooks/usePageTitle'
import { useUserRole } from '@Hooks/useUser'
import { Role } from '@Api'

export interface AdminTabProps extends React.PropsWithChildren {
  head?: React.ReactNode
  isLoading?: boolean
  headProps?: GroupProps
}

interface AdminNavPage {
  icon: string
  title: string
  path: string
  requiredRole: Role
}

export const WithAdminTab: FC<AdminTabProps> = ({ head, headProps, children }) => {
  const navigate = useNavigate()
  const location = useLocation()
  const { t } = useTranslation()
  const { role } = useUserRole()

  const allPages = useMemo<AdminNavPage[]>(
    () => [
      { icon: mdiFlagOutline, title: t('admin.tab.games.index'), path: 'games', requiredRole: Role.Teacher },
      { icon: mdiBookOpenPageVariantOutline, title: '题库管理', path: 'theory-bank', requiredRole: Role.Teacher },
      { icon: mdiImageOutline, title: '环境模板', path: 'images', requiredRole: Role.Teacher },
      { icon: mdiSchoolOutline, title: '培训管理', path: 'training', requiredRole: Role.Teacher },
      { icon: mdiAccountCogOutline, title: t('admin.tab.users'), path: 'users', requiredRole: Role.Teacher },
      { icon: mdiAccountGroupOutline, title: t('admin.tab.teams'), path: 'teams', requiredRole: Role.Admin },
      { icon: mdiServerNetwork, title: '节点管理', path: 'nodes', requiredRole: Role.Admin },
      { icon: mdiClipboardListOutline, title: '部署队列', path: 'queue', requiredRole: Role.Admin },
      { icon: mdiFileDocumentOutline, title: t('admin.tab.logs'), path: 'logs', requiredRole: Role.Admin },
      { icon: mdiSitemapOutline, title: t('admin.tab.settings'), path: 'settings', requiredRole: Role.Admin },
    ],
    [t]
  )

  const pages = useMemo(() => allPages.filter((page) => RequireRole(page.requiredRole, role)), [allPages, role])

  const getTab = (path: string) => pages.findIndex((page) => path.startsWith(`/admin/${page.path}`))
  const tabIndex = getTab(location.pathname)
  const [activeTab, setActiveTab] = useState(tabIndex < 0 ? 0 : tabIndex)

  const onChange = (active: number, tabKey: string) => {
    setActiveTab(active)
    navigate(`/admin/${tabKey}`)
  }

  useEffect(() => {
    if (pages.length === 0) return

    const tab = getTab(location.pathname)
    if (tab >= 0) {
      setActiveTab(tab)
    } else {
      navigate(`/admin/${pages[0].path}`, { replace: true })
    }
  }, [location.pathname, pages.length])

  const activePage = pages[Math.max(0, tabIndex)] ?? pages[0]

  usePageTitle(activePage?.title ?? t('admin.tab.games.index'))

  return (
    <div className="admin-shell yy-admin-shell">
      <section className="admin-main">
        <ScrollArea type="hover" offsetScrollbars scrollbarSize={4} className="yy-admin-tab-scroll">
          <div className="admin-tab-grid yy-admin-tab-grid" aria-label="管理导航">
            {pages.map((page, index) => (
              <Tooltip key={page.path} label={page.title} position="bottom">
                <button
                  type="button"
                  className={`admin-tab-card ${index === activeTab ? 'is-active' : ''}`}
                  onClick={() => onChange(index, page.path)}
                  aria-label={page.title}
                >
                  <YinyuHexField cells={16} />
                  <Icon path={page.icon} size={1} />
                  <strong>{page.title}</strong>
                </button>
              </Tooltip>
            ))}
          </div>
        </ScrollArea>
        {head ? (
          <Group
            wrap="nowrap"
            justify="space-between"
            mih="3.5rem"
            w="100%"
            p="xs"
            className="admin-toolbar panel-card"
            {...headProps}
          >
            {head}
          </Group>
        ) : null}
        <div key={activePage?.path ?? 'admin'} className="yy-admin-route-stage">
          {children}
        </div>
      </section>
    </div>
  )
}
