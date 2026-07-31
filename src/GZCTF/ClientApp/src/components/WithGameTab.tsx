import { Stack, Title } from '@mantine/core'
import { showNotification } from '@mantine/notifications'
import {
  mdiChartLine,
  mdiExclamationThick,
  mdiFileDocumentCheckOutline,
  mdiFlagOutline,
  mdiMonitorEye,
  mdiSwordCross,
} from '@mdi/js'
import { Icon } from '@mdi/react'
import dayjs from 'dayjs'
import duration from 'dayjs/plugin/duration'
import React, { FC, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useLocation, useNavigate, useParams } from 'react-router'
import { GameProgress } from '@Components/GameProgress'
import { IconTabs } from '@Components/IconTabs'
import { RequireRole } from '@Components/WithRole'
import { YinyuRouteTransition } from '@Components/yinyu/YinyuUI'
import { getGameStatus, useGame } from '@Hooks/useGame'
import { usePageTitle } from '@Hooks/usePageTitle'
import { useUserRole } from '@Hooks/useUser'
import { DetailedGameInfoModel, GameType, ParticipationStatus, Role } from '@Api'

dayjs.extend(duration)

const GameCountdown: FC<{ game?: DetailedGameInfoModel }> = ({ game }) => {
  const { endTime, progress } = getGameStatus(game)
  const [now, setNow] = useState(dayjs())
  const { t } = useTranslation()

  useEffect(() => {
    if (!game || dayjs() > dayjs(game.end)) return
    const interval = setInterval(() => setNow(dayjs()), 1000)
    return () => clearInterval(interval)
  }, [game])

  const countdown = dayjs.duration(endTime.diff(now))

  return (
    <div className="route-loader yy-game-countdown">
      <div className="yy-game-countdown-copy">
        <span>剩余时间</span>
        <strong>
          {countdown.asHours() > 999
            ? t('game.content.game_lasts_long')
            : countdown.asSeconds() > 0
              ? `${Math.floor(countdown.asHours())}:${countdown.format('mm:ss')}`
              : t('game.content.game_ended')}
        </strong>
      </div>
      <div className="yy-game-countdown-progress">
        <span>赛事进度</span>
        <GameProgress percentage={progress} py={0} />
      </div>
    </div>
  )
}

export const WithGameTab: FC<React.PropsWithChildren> = ({ children }) => {
  const { id } = useParams()
  const numId = parseInt(id ?? '-1')
  const location = useLocation()
  const navigate = useNavigate()

  const { role } = useUserRole()
  const { game, status } = useGame(numId)
  const { t } = useTranslation()

  const finished = dayjs() > dayjs(game?.end ?? new Date())

  const isAwdGame = game?.gameType === GameType.AWDP || game?.gameType === GameType.Mixed
  const isTheoryGame = game?.gameType === GameType.Theory || game?.gameType === GameType.Mixed
  const isTheoryOnly = game?.gameType === GameType.Theory
  const isPentestOnly = game?.gameType === GameType.Penetration

  const pages = [
    ...(!isTheoryOnly && !isPentestOnly
      ? [
          {
            icon: mdiFlagOutline,
            title: t('game.tab.challenge'),
            path: 'challenges',
            link: 'challenges',
            requireJoin: true,
            requireRole: Role.User,
            requireAwd: false,
          },
        ]
      : []),
    ...(isAwdGame
      ? [
          {
            icon: mdiSwordCross,
            title: t('game.tab.awd'),
            path: 'awdp',
            link: 'awdp',
            requireJoin: true,
            requireRole: Role.User,
            requireAwd: true,
          },
        ]
      : []),
    ...(isTheoryGame
      ? [
          {
            icon: mdiFileDocumentCheckOutline,
            title: '理论考试',
            path: 'theory',
            link: 'theory',
            requireJoin: true,
            requireRole: Role.User,
            requireAwd: false,
          },
        ]
      : []),
    ...(!isTheoryOnly
      ? [
          {
            icon: mdiChartLine,
            title: t('game.tab.scoreboard'),
            path: 'scoreboard',
            link: 'scoreboard',
            requireJoin: false,
            requireRole: Role.User,
            requireAwd: false,
          },
        ]
      : [
          {
            icon: mdiChartLine,
            title: '理论榜单',
            path: 'theory-scoreboard',
            link: 'theory-scoreboard',
            requireJoin: false,
            requireRole: Role.User,
            requireAwd: false,
          },
        ]),
    ...(!isTheoryOnly
      ? [
          {
            icon: mdiMonitorEye,
            title: t('game.tab.monitor.index'),
            path: 'monitor',
            link: 'monitor/events',
            requireJoin: false,
            requireRole: Role.Monitor,
            requireAwd: false,
          },
        ]
      : []),
  ]

  const filteredPages = pages
    .filter((p) => RequireRole(p.requireRole, role))
    .filter((p) => !p.requireJoin || game?.status === ParticipationStatus.Accepted)
    .filter((p) => !p.requireJoin || !finished || game?.practiceMode)

  const tabs = filteredPages.map((p) => ({
    tabKey: p.link,
    label: p.title,
    icon: <Icon path={p.icon} size={1} />,
  }))

  const getTab = (path: string) => {
    const segments = path.split('/').filter(Boolean)
    const gamePathIndex = segments.findIndex(
      (segment, index) => segment === 'games' && segments[index + 1] === String(numId)
    )
    const currentPath =
      gamePathIndex >= 0 ? segments.slice(gamePathIndex + 2).join('/') : (segments[segments.length - 1] ?? '')

    return filteredPages?.findIndex(
      (page) =>
        currentPath === page.path ||
        currentPath === page.link ||
        currentPath.startsWith(`${page.path}/`) ||
        currentPath.startsWith(`${page.link}/`)
    )
  }

  const tabIndex = getTab(location.pathname)
  const [activeTab, setActiveTab] = useState(tabIndex < 0 ? 0 : tabIndex)

  const onChange = (active: number, tabKey: string) => {
    setActiveTab(active)
    navigate(`/games/${numId}/${tabKey}`)
  }

  usePageTitle(game?.title)

  useEffect(() => {
    const tab = getTab(location.pathname)
    if (tab < 0) return
    setActiveTab(tab)
  })

  useEffect(() => {
    if (!game || filteredPages.length === 0) return
    const tab = getTab(location.pathname)
    const gameRoot = `/games/${numId}`
    if (tab < 0 && location.pathname.startsWith(`${gameRoot}/`)) {
      navigate(`${gameRoot}/${filteredPages[0].link}`, { replace: true })
    }
  }, [game, location.pathname, numId, navigate, role, status])

  useEffect(() => {
    if (game) {
      const now = dayjs()
      if (now < dayjs(game.start)) {
        navigate(`/games/${numId}`)
        showNotification({
          id: 'no-access',
          color: 'yellow',
          message: t('game.notification.not_started'),
          icon: <Icon path={mdiExclamationThick} size={1} />,
        })
        return
      }

      if (location.pathname.includes('scoreboard')) {
        return
      }

      if (location.pathname.includes('monitor') && RequireRole(Role.Monitor, role)) {
        return
      }

      if (now < dayjs(game.end)) {
        if (status === ParticipationStatus.Suspended) {
          navigate(`/games/${numId}`)
          showNotification({
            id: 'no-access',
            color: 'yellow',
            message: t('game.notification.suspended'),
            icon: <Icon path={mdiExclamationThick} size={1} />,
          })
        } else if (status !== ParticipationStatus.Accepted) {
          navigate(`/games/${numId}`)
          showNotification({
            id: 'no-access',
            color: 'yellow',
            message: t('game.notification.not_joined'),
            icon: <Icon path={mdiExclamationThick} size={1} />,
          })
        }
      } else if (!game.practiceMode && !RequireRole(Role.Monitor, role)) {
        navigate(`/games/${numId}`)
        showNotification({
          id: 'no-access',
          color: 'yellow',
          message: t('game.notification.ended'),
          icon: <Icon path={mdiExclamationThick} size={1} />,
        })
      }
    }
  }, [game, status, role, location])

  return (
    <Stack pos="relative" mt="md" className="yy-game-tab-shell view-stack">
      {!game ? (
        <div className="yy-game-tab-loading" role="status" aria-live="polite">
          <YinyuRouteTransition title="YINYU MATCH" description="正在读取演练信息" />
        </div>
      ) : null}
      <IconTabs
        active={activeTab}
        onTabChange={onChange}
        tabs={tabs}
        panesClassName="yy-game-tab-panes"
        aside={
          game && (
            <div className="yy-game-tab-aside">
              <Title order={2}>{game?.title}</Title>
              <GameCountdown game={game} />
            </div>
          )
        }
      />
      {children}
    </Stack>
  )
}
