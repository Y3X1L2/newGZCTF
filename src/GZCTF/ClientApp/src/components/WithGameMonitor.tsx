import { Button, Stack } from '@mantine/core'
import { mdiExclamationThick, mdiFlag, mdiLightningBolt, mdiPackageVariant, mdiTableArrowDown } from '@mdi/js'
import { Icon } from '@mdi/react'
import React, { FC, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useLocation, useNavigate, useParams } from 'react-router'
import { WithGameTab } from '@Components/WithGameTab'
import { WithNavBar } from '@Components/WithNavbar'
import { WithRole } from '@Components/WithRole'
import { YinyuPanel, YinyuRouteTransition } from '@Components/yinyu/YinyuUI'
import { downloadBlob } from '@Utils/ApiHelper'
import api, { Role } from '@Api'

interface WithGameMonitorProps extends React.PropsWithChildren {
  isLoading?: boolean
}

export const WithGameMonitor: FC<WithGameMonitorProps> = ({ children, isLoading }) => {
  const { id } = useParams()
  const numId = parseInt(id ?? '-1')

  const navigate = useNavigate()
  const location = useLocation()
  const { t } = useTranslation()

  const pages = [
    { icon: mdiLightningBolt, title: t('game.tab.monitor.events'), path: 'events' },
    { icon: mdiFlag, title: t('game.tab.monitor.submissions'), path: 'submissions' },
    { icon: mdiExclamationThick, title: t('game.tab.monitor.cheatinfo'), path: 'cheatinfo' },
    { icon: mdiPackageVariant, title: t('game.tab.monitor.traffic'), path: 'traffic' },
  ]

  const getTab = (path: string) => pages.find((page) => path.endsWith(page.path))

  const [activeTab, setActiveTab] = useState(getTab(location.pathname)?.path ?? pages[0].path)
  const [disabled, setDisabled] = useState(false)

  useEffect(() => {
    const tab = getTab(location.pathname)
    if (tab) {
      setActiveTab(tab.path)
    } else {
      navigate(pages[0].path)
    }
  }, [location])

  const onDownloadScoreboardSheet = () =>
    downloadBlob(
      api.game.gameScoreboardSheet(numId, { format: 'blob' }),
      setDisabled,
      t,
      `Scoreboard_${numId}_${Date.now()}.xlsx`
    )

  return (
    <WithNavBar width="min(100%, calc(100vw - 7.25rem))">
      <WithRole requiredRole={Role.Monitor}>
        <WithGameTab>
          <div className="yy-monitor-layout">
            <YinyuPanel p="sm" cells={24} className="yy-monitor-sidebar">
              <Button
                disabled={disabled}
                fullWidth
                className="yy-monitor-download"
                leftSection={<Icon path={mdiTableArrowDown} size={1} />}
                onClick={onDownloadScoreboardSheet}
              >
                {t('game.button.download.scoreboard')}
              </Button>
              <nav className="yy-monitor-nav-list" aria-label={t('game.tab.monitor.index')}>
                {pages.map((page) => {
                  const isActive = activeTab === page.path

                  return (
                    <button
                      key={page.path}
                      type="button"
                      className="yy-monitor-nav-button"
                      data-active={isActive ? 'true' : undefined}
                      aria-current={isActive ? 'page' : undefined}
                      onClick={() => navigate(`/games/${id}/monitor/${page.path}`)}
                    >
                      <Icon path={page.icon} size={1} />
                      <span>{page.title}</span>
                    </button>
                  )
                })}
              </nav>
            </YinyuPanel>
            <Stack pos="relative" className="yy-monitor-content">
              {isLoading ? (
                <div className="yy-game-tab-loading" role="status" aria-live="polite">
                  <YinyuRouteTransition title={t('game.tab.monitor.index')} description="正在读取监控数据" />
                </div>
              ) : null}
              {children}
            </Stack>
          </div>
        </WithGameTab>
      </WithRole>
    </WithNavBar>
  )
}
