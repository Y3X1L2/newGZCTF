import { Group, GroupProps, ScrollArea, Tooltip } from '@mantine/core'
import {
  mdiAccountCogOutline,
  mdiAccountGroupOutline,
  mdiBookOpenPageVariantOutline,
  mdiClipboardListOutline,
  mdiFileDocumentOutline,
  mdiFlagOutline,
  mdiImageOutline,
  mdiServerNetwork,
  mdiSitemapOutline,
} from '@mdi/js'
import { Icon } from '@mdi/react'
import React, { FC, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useLocation, useNavigate } from 'react-router'
import { YinyuHexField, YinyuRouteProgress, YinyuRouteTransition } from '@Components/yinyu/YinyuUI'
import { usePageTitle } from '@Hooks/usePageTitle'

export interface AdminTabProps extends React.PropsWithChildren {
  head?: React.ReactNode
  isLoading?: boolean
  headProps?: GroupProps
}

export const WithAdminTab: FC<AdminTabProps> = ({ head, headProps, isLoading, children }) => {
  const navigate = useNavigate()
  const location = useLocation()
  const { t } = useTranslation()

  const pages = [
    { icon: mdiFlagOutline, title: t('admin.tab.games.index'), path: 'games' },
    { icon: mdiBookOpenPageVariantOutline, title: '\u9898\u5e93\u7ba1\u7406', path: 'theory-bank' },
    { icon: mdiAccountGroupOutline, title: t('admin.tab.teams'), path: 'teams' },
    { icon: mdiAccountCogOutline, title: t('admin.tab.users'), path: 'users' },
    { icon: mdiImageOutline, title: '\u73af\u5883\u6a21\u677f', path: 'images' },
    { icon: mdiServerNetwork, title: '\u8282\u70b9\u7ba1\u7406', path: 'nodes' },
    { icon: mdiClipboardListOutline, title: '\u90e8\u7f72\u961f\u5217', path: 'queue' },
    { icon: mdiFileDocumentOutline, title: t('admin.tab.logs'), path: 'logs' },
    { icon: mdiSitemapOutline, title: t('admin.tab.settings'), path: 'settings' },
  ]

  const getTab = (path: string) => pages.findIndex((page) => path.startsWith(`/admin/${page.path}`))
  const tabIndex = getTab(location.pathname)
  const [activeTab, setActiveTab] = useState(tabIndex < 0 ? 0 : tabIndex)

  const onChange = (active: number, tabKey: string) => {
    setActiveTab(active)
    navigate(`/admin/${tabKey}`)
  }

  useEffect(() => {
    const tab = getTab(location.pathname)
    if (tab >= 0) {
      setActiveTab(tab)
    } else {
      navigate(`/admin/${pages[0].path}`)
    }
  }, [location])

  const activePage = pages[Math.max(0, tabIndex)] ?? pages[0]

  usePageTitle(activePage.title)

  return (
    <div className="admin-shell yy-admin-shell">
      <section className="admin-main">
        <ScrollArea type="hover" offsetScrollbars scrollbarSize={4} className="yy-admin-tab-scroll">
          <div className="admin-tab-grid yy-admin-tab-grid" aria-label="\u7ba1\u7406\u5bfc\u822a">
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
        <div key={activePage.path} className="yy-admin-route-stage">
          <YinyuRouteProgress className="yy-admin-route-pulse" />
          {children}
        </div>
        {isLoading ? (
          <div className="yy-admin-transition-overlay" role="status" aria-live="polite">
            <YinyuRouteTransition title="YINYU ADMIN" description="\u6b63\u5728\u8bfb\u53d6\u7ba1\u7406\u6570\u636e" />
          </div>
        ) : null}
      </section>
    </div>
  )
}
