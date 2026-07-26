import {
  ArrowLeft,
  BookOpenCheck,
  ChartNoAxesColumn,
  Clock3,
  Flag,
  Layers3,
  Megaphone,
  Network,
  Settings2,
  Swords,
  UserCheck,
} from 'lucide-react'
import { NavLink, Outlet, useParams } from 'react-router'
import { GameInfoModel, GameType } from '@Api'
import { DataState } from '../../../shared/Primitives'
import { StatusBadge } from '../shared/AdminWorkbench'
import styles from './GameAdminShell.module.css'
import { gameLifecycle, gameLifecycleMeta, gameTypeLabel } from './gamePresentation'
import { useAdminGame } from './useAdminGames'

const baseTabs = [
  { label: '比赛信息', route: 'info', icon: Settings2 },
  { label: '比赛阶段', route: 'phases', icon: Clock3 },
  { label: '赛区管理', route: 'divisions', icon: Layers3 },
  { label: '报名审核', route: 'review', icon: UserCheck },
  { label: '比赛公告', route: 'notices', icon: Megaphone },
]

export interface GameAdminOutletContext {
  game: GameInfoModel
  mutateGame: () => Promise<unknown>
}

export function supportsTeamLabGame(gameType: GameInfoModel['gameType']) {
  return gameType === GameType.Penetration || gameType === GameType.Mixed
}

export function GameAdminShell() {
  const { gameId } = useParams()
  const id = Number(gameId)
  const request = useAdminGame(id)

  if (!Number.isInteger(id) || id <= 0) return <DataState description="比赛编号不是有效数字。" title="比赛参数错误" />
  if (!request.game) {
    return request.error ? (
      <DataState description="比赛不存在，或当前账户没有管理权限。" title="无法打开比赛管理" />
    ) : (
      <DataState description="正在读取比赛配置。" loading title="比赛管理加载中" />
    )
  }

  const lifecycle = gameLifecycleMeta(gameLifecycle(request.game))
  const tabs = [
    ...baseTabs,
    ...(request.game.gameType === GameType.Jeopardy || request.game.gameType === GameType.Mixed
      ? [{ label: 'CTF 题目', route: 'challenges', icon: Flag }]
      : []),
    ...(request.game.gameType === GameType.Theory || request.game.gameType === GameType.Mixed
      ? [
          { label: '理论试卷', route: 'theory-paper', icon: BookOpenCheck },
          { label: '理论成绩', route: 'theory-results', icon: ChartNoAxesColumn },
        ]
      : []),
    ...(request.game.gameType === GameType.AWDP || request.game.gameType === GameType.Mixed
      ? [{ label: 'AWDP 管理', route: 'awdp-services', icon: Swords }]
      : []),
    ...(supportsTeamLabGame(request.game.gameType)
      ? [{ label: 'TeamLab 编排', route: 'teamlab', icon: Network }]
      : []),
  ]
  return (
    <div className={styles.shell}>
      <header className={styles.header}>
        <NavLink className={styles.backLink} to="/admin/games">
          <ArrowLeft size={16} />
          返回赛事管理
        </NavLink>
        <div className={styles.identity}>
          <div>
            <span>GAME #{request.game.id}</span>
            <strong>{request.game.title}</strong>
          </div>
          <div className={styles.badges}>
            <StatusBadge tone={lifecycle.tone}>{lifecycle.label}</StatusBadge>
            <StatusBadge tone="info">{gameTypeLabel(request.game.gameType)}</StatusBadge>
            <StatusBadge tone={request.game.hidden ? 'warning' : 'success'}>
              {request.game.hidden ? '隐藏' : '公开'}
            </StatusBadge>
          </div>
        </div>
        <nav aria-label="比赛管理页面" className={styles.tabs}>
          {tabs.map((tab) => (
            <NavLink
              className={({ isActive }) => (isActive ? styles.activeTab : styles.tab)}
              key={tab.route}
              to={tab.route}
            >
              <tab.icon size={16} />
              {tab.label}
            </NavLink>
          ))}
        </nav>
      </header>
      <div className={styles.workspace}>
        <Outlet context={{ game: request.game, mutateGame: request.mutate }} />
      </div>
    </div>
  )
}
