import { Group, GroupProps, LoadingOverlay, Stack } from '@mantine/core'
import {
  mdiAccountCogOutline,
  mdiAccountGroupOutline,
  mdiBookOpenPageVariantOutline,
  mdiClipboardListOutline,
  mdiFileDocumentOutline,
  mdiFlagOutline,
  mdiImageOutline,
  mdiMonitorDashboard,
  mdiServerNetwork,
  mdiSitemapOutline,
} from '@mdi/js'
import { Icon } from '@mdi/react'
import React, { FC, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useLocation, useNavigate } from 'react-router'
import { IconTabs } from '@Components/IconTabs'
import { DEFAULT_LOADING_OVERLAY } from '@Utils/Shared'
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
    { icon: mdiMonitorDashboard, title: '仪表盘', path: 'dashboard' },
    { icon: mdiFlagOutline, title: t('admin.tab.games.index'), path: 'games' },
    { icon: mdiBookOpenPageVariantOutline, title: '题库管理', path: 'theory-bank' },
    { icon: mdiAccountGroupOutline, title: t('admin.tab.teams'), path: 'teams' },
    { icon: mdiAccountCogOutline, title: t('admin.tab.users'), path: 'users' },
    { icon: mdiImageOutline, title: '环境模板', path: 'images' },
    { icon: mdiServerNetwork, title: '节点管理', path: 'nodes' },
    { icon: mdiClipboardListOutline, title: '部署队列', path: 'queue' },
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
      navigate(pages[0].path)
    }
  }, [location])

  usePageTitle(pages[Math.max(0, tabIndex)].title)

  return (
    <Stack gap="xs" align="center" pt="md">
      <IconTabs
        withIcon
        active={activeTab}
        onTabChange={onChange}
        tabs={pages.map((p) => ({
          tabKey: p.path,
          label: p.title,
          icon: <Icon path={p.icon} size={1} />,
        }))}
      />
      {head && (
        <Group wrap="nowrap" justify="space-between" h="40px" w="100%" {...headProps}>
          {head}
        </Group>
      )}
      {children}
      <LoadingOverlay visible={isLoading ?? false} overlayProps={DEFAULT_LOADING_OVERLAY} />
    </Stack>
  )
}
