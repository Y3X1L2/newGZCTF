import * as signalR from '@microsoft/signalr'
import { Clock3, Radio, Wifi, WifiOff } from 'lucide-react'
import { createContext, useContext, useEffect, useMemo, useState } from 'react'
import { Link, NavLink, Outlet, useLocation, useParams } from 'react-router'
import api, { DetailedGameInfoModel, GameNotice, NoticeType } from '@Api'
import { DataState, StatusPill } from '../../../shared/Primitives'
import { useVNextPageTitle } from '../../../shared/useVNextPageTitle'
import { gameModulesFor } from '../gameModules'
import styles from './GameWorkspaceShell.module.css'

type ConnectionState = 'connecting' | 'connected' | 'reconnecting' | 'offline'

interface GameWorkspaceContextValue {
  gameId: number
  game: DetailedGameInfoModel
  notices: GameNotice[]
  connectionState: ConnectionState
  revision: number
}

const GameWorkspaceContext = createContext<GameWorkspaceContextValue | null>(null)

export function useGameWorkspace() {
  const context = useContext(GameWorkspaceContext)
  if (!context) throw new Error('useGameWorkspace must be used inside GameWorkspaceShell')
  return context
}

function durationLabel(target?: number, now = Date.now()) {
  if (!target) return '--'
  const seconds = Math.max(0, Math.floor((target - now) / 1000))
  const days = Math.floor(seconds / 86400)
  const hours = Math.floor((seconds % 86400) / 3600)
  const minutes = Math.floor((seconds % 3600) / 60)
  const rest = seconds % 60
  if (days) return `${days}天 ${hours}小时`
  if (hours) return `${hours}小时 ${minutes}分`
  return `${minutes}分 ${String(rest).padStart(2, '0')}秒`
}

function connectionLabel(state: ConnectionState) {
  if (state === 'connected') return '实时连接正常'
  if (state === 'reconnecting') return '正在重新连接'
  if (state === 'connecting') return '正在连接'
  return '实时连接中断'
}

function noticeKey(notice: GameNotice) {
  return `${notice.id}:${notice.time}:${notice.type}`
}

function mergeNotices(current: GameNotice[], incoming: GameNotice[]) {
  const seen = new Set<string>()
  return [...incoming, ...current]
    .filter((notice) => {
      const key = noticeKey(notice)
      if (seen.has(key)) return false
      seen.add(key)
      return true
    })
    .sort((left, right) => right.time - left.time)
    .slice(0, 100)
}

export function formatWorkspaceNotice(notice: GameNotice) {
  const values = notice.values ?? []
  const last = values.at(-1) || '比赛状态已更新'
  if (notice.type === NoticeType.NewChallenge) return `新题目已开放：${last}`
  if (notice.type === NoticeType.NewHint) return `新提示已发布：${last}`
  if (notice.type === NoticeType.FirstBlood) return `一血产生：${values.join(' · ')}`
  if (notice.type === NoticeType.SecondBlood) return `二血产生：${values.join(' · ')}`
  if (notice.type === NoticeType.ThirdBlood) return `三血产生：${values.join(' · ')}`
  return last
}

export function GameWorkspaceShell() {
  const { gameId = '' } = useParams()
  const id = Number(gameId)
  const validId = Number.isInteger(id) && id > 0
  const location = useLocation()
  const { data: game, error } = api.game.useGameGame(id, { revalidateOnFocus: false }, validId)
  const { data: initialNotices, mutate: mutateNotices } = api.game.useGameNotices(
    id,
    { count: 100, skip: 0 },
    { revalidateOnFocus: false },
    validId
  )
  const [notices, setNotices] = useState<GameNotice[]>([])
  const [connectionState, setConnectionState] = useState<ConnectionState>('connecting')
  const [revision, setRevision] = useState(0)
  const [now, setNow] = useState(Date.now())

  const modules = gameModulesFor(game?.gameType)
  const activeModule = modules.find((module) => location.pathname.endsWith(`/${module.id}`))
  const countdownTarget = game && Date.now() < (game.start ?? 0) ? game.start : game?.end
  const countdownPrefix = game && Date.now() < (game.start ?? 0) ? '距离开始' : '距离结束'

  useVNextPageTitle(`${game?.title || '比赛'} · ${activeModule?.label || '工作区'}`)

  useEffect(() => {
    if (initialNotices) setNotices((current) => mergeNotices(current, initialNotices))
  }, [initialNotices])

  useEffect(() => {
    if (!countdownTarget || countdownTarget <= Date.now()) return undefined
    const update = () => {
      if (!document.hidden) setNow(Date.now())
    }
    const timer = window.setInterval(update, 1000)
    document.addEventListener('visibilitychange', update)
    return () => {
      window.clearInterval(timer)
      document.removeEventListener('visibilitychange', update)
    }
  }, [countdownTarget])

  useEffect(() => {
    if (!validId) return undefined
    let disposed = false
    const connection = new signalR.HubConnectionBuilder()
      .withUrl(`/hub/user?game=${id}`)
      .withHubProtocol(new signalR.JsonHubProtocol())
      .withAutomaticReconnect()
      .configureLogging(signalR.LogLevel.None)
      .build()

    connection.serverTimeoutInMilliseconds = 60 * 60 * 1000
    connection.on('ReceivedGameNotice', (notice: GameNotice) => {
      setNotices((current) => mergeNotices(current, [notice]))
      setRevision((current) => current + 1)
    })
    connection.onreconnecting(() => setConnectionState('reconnecting'))
    connection.onreconnected(() => {
      setConnectionState('connected')
      setRevision((current) => current + 1)
      void mutateNotices()
    })
    connection.onclose(() => setConnectionState('offline'))

    setConnectionState('connecting')
    connection
      .start()
      .then(() => {
        if (!disposed) setConnectionState('connected')
      })
      .catch(() => {
        if (!disposed) setConnectionState('offline')
      })

    return () => {
      disposed = true
      void connection.stop()
    }
  }, [id, mutateNotices, validId])

  const contextValue = useMemo<GameWorkspaceContextValue | null>(
    () => (game ? { gameId: id, game, notices, connectionState, revision } : null),
    [connectionState, game, id, notices, revision]
  )

  if (!validId) {
    return (
      <div className={styles.statePage}>
        <DataState description="比赛编号格式不正确。" title="无法识别比赛" />
      </div>
    )
  }
  if (!game && !error) {
    return (
      <div className={styles.statePage}>
        <DataState description="正在建立比赛工作区。" loading title="工作区加载中" />
      </div>
    )
  }
  if (!game || !contextValue) {
    return (
      <div className={styles.statePage}>
        <DataState description="比赛不存在或当前账户无权访问。" title="工作区加载失败" />
      </div>
    )
  }

  return (
    <GameWorkspaceContext.Provider value={contextValue}>
      <div className={styles.workspace}>
        <header className={styles.workspaceBar}>
          <div className={styles.gameIdentity}>
            <Link to={`/games/${id}`}>{game.title || `比赛 ${id}`}</Link>
            <span>{activeModule?.label || '比赛工作区'}</span>
          </div>

          <nav aria-label="比赛模块" className={styles.moduleNav}>
            {modules.map((module) => {
              const Icon = module.icon
              return module.implemented ? (
                <NavLink
                  className={({ isActive }) => (isActive ? styles.moduleLinkActive : styles.moduleLink)}
                  key={module.id}
                  to={`/games/${id}/${module.id}`}
                >
                  <Icon size={16} />
                  {module.shortLabel}
                </NavLink>
              ) : (
                <span className={styles.modulePending} key={module.id} title="本轮尚未重构">
                  <Icon size={16} />
                  {module.shortLabel}
                </span>
              )
            })}
          </nav>

          <div className={styles.liveStatus}>
            {countdownTarget && countdownTarget > now ? (
              <span className={styles.countdown}>
                <Clock3 size={15} />
                <small>{countdownPrefix}</small>
                <strong>{durationLabel(countdownTarget, now)}</strong>
              </span>
            ) : null}
            <span className={styles.connection} title={connectionLabel(connectionState)}>
              {connectionState === 'connected' ? (
                <Wifi size={16} />
              ) : connectionState === 'offline' ? (
                <WifiOff size={16} />
              ) : (
                <Radio size={16} />
              )}
              <StatusPill
                tone={
                  connectionState === 'connected' ? 'success' : connectionState === 'offline' ? 'warning' : 'neutral'
                }
              >
                {connectionState === 'connected' ? '实时' : connectionState === 'offline' ? '离线' : '连接中'}
              </StatusPill>
            </span>
          </div>
        </header>
        <Outlet />
      </div>
    </GameWorkspaceContext.Provider>
  )
}
