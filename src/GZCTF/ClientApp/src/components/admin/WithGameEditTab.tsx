import { Button, Group, GroupProps, Stack, Tabs } from '@mantine/core'
import {
  mdiAccountGroupOutline,
  mdiBullhornOutline,
  mdiFileDocumentCheckOutline,
  mdiFlagOutline,
  mdiKeyboardBackspace,
  mdiMonitorDashboard,
  mdiNetworkOutline,
  mdiSwordCross,
  mdiTagOutline,
  mdiTextBoxOutline,
} from '@mdi/js'
import { Icon } from '@mdi/react'
import React, { FC, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useLocation, useNavigate, useParams } from 'react-router'
import { AdminPage } from '@Components/admin/AdminPage'
import { useAdminGame } from '@Hooks/useGame'
import api, { GameType } from '@Api'
import misc from '@Styles/Misc.module.css'

export interface GameEditTabProps extends React.PropsWithChildren {
  head?: React.ReactNode
  headProps?: GroupProps
  contentPos?: React.CSSProperties['justifyContent']
  isLoading?: boolean
  backUrl?: string
}

export const WithGameEditTab: FC<GameEditTabProps> = ({ children, contentPos, head, backUrl, ...others }) => {
  const navigate = useNavigate()
  const location = useLocation()
  const { id } = useParams()
  const { t } = useTranslation()
  const numId = parseInt(id ?? '-1')
  const { game } = useAdminGame(numId)

  const isAwdGame = game?.gameType === GameType.AWDP || game?.gameType === GameType.Mixed
  const isTheoryGame = game?.gameType === GameType.Theory || game?.gameType === GameType.Mixed
  const isPentestGame = game?.gameType === GameType.Penetration || game?.gameType === GameType.Mixed
  const isTheoryOnly = game?.gameType === GameType.Theory
  const isPentestOnly = game?.gameType === GameType.Penetration

  const pages = [
    { icon: mdiTextBoxOutline, title: t('admin.tab.games.info'), path: 'info' },
    { icon: mdiBullhornOutline, title: t('admin.tab.games.notices'), path: 'notices' },
    ...(!isTheoryOnly && !isPentestOnly
      ? [{ icon: mdiFlagOutline, title: t('admin.tab.games.challenges'), path: 'challenges' }]
      : []),
    ...(isAwdGame ? [{ icon: mdiSwordCross, title: t('admin.tab.games.awd'), path: 'awdp-services' }] : []),
    ...(isPentestGame ? [{ icon: mdiNetworkOutline, title: '渗透编排', path: 'pentest' }] : []),
    ...(isTheoryGame
      ? [
          { icon: mdiFileDocumentCheckOutline, title: '理论试卷', path: 'theory-paper' },
          { icon: mdiAccountGroupOutline, title: '理论成绩', path: 'theory-results' },
        ]
      : []),
    { icon: mdiTagOutline, title: t('admin.tab.games.divisions'), path: 'divisions' },
    { icon: mdiAccountGroupOutline, title: t('admin.tab.games.review'), path: 'review' },
    ...(!isTheoryOnly
      ? [
          { icon: mdiFileDocumentCheckOutline, title: t('admin.tab.games.writeups'), path: 'writeups' },
          { icon: mdiMonitorDashboard, title: t('admin.tab.games.screen'), path: 'screen/control' },
        ]
      : []),
  ]
  const getTab = (path: string) => pages.find((page) => path.includes(page.path))

  const [activeTab, setActiveTab] = useState(getTab(location.pathname)?.path ?? pages[0].path)

  useEffect(() => {
    if (!game) return

    const tab = getTab(location.pathname)
    if (tab) {
      setActiveTab(tab.path)
    } else {
      navigate(pages[0].path)
    }
  }, [location, game])

  return (
    <AdminPage
      {...others}
      head={
        <>
          <Button
            w="10rem"
            component={Link}
            classNames={{ inner: misc.justifyBetween }}
            leftSection={<Icon path={mdiKeyboardBackspace} size={1} />}
            to={backUrl ?? '/admin/games'}
          >
            {t('admin.button.back')}
          </Button>
          <Group wrap="nowrap" justify={contentPos ?? 'space-between'} w="calc(100% - 11rem)">
            {head}
          </Group>
        </>
      }
    >
      <div className="yy-game-edit-grid" style={{ width: '100%', paddingBottom: 'var(--mantine-spacing-xl)' }}>
        <Tabs
          orientation="vertical"
          value={activeTab}
          onChange={(value) => value && navigate(`/admin/games/${id}/${value}`)}
          className="panel-card admin-panel yy-game-edit-nav"
          classNames={{
            root: misc.w10rem,
            list: misc.w10rem,
          }}
        >
          <Tabs.List>
            {pages.map((page) => (
              <Tabs.Tab key={page.path} leftSection={<Icon path={page.icon} size={1} />} value={page.path}>
                {page.title}
              </Tabs.Tab>
            ))}
          </Tabs.List>
        </Tabs>
        <Stack pos="relative" className="panel-card admin-panel large yy-game-edit-panel">
          <div key={activeTab} className="yy-admin-route-stage yy-game-edit-route-stage">
            {children}
          </div>
        </Stack>
      </div>
    </AdminPage>
  )
}
