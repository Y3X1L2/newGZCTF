import { FC, useEffect, useMemo, useState } from 'react'
import { useNavigate } from 'react-router'
import { Select } from '@mantine/core'
import { OnceSWRConfig } from '@Hooks/useConfig'
import api from '@Api'
import yinyuIcon from '../../assets/yinyu-icon-transparent.png'
import { MetalScoreCity } from './MetalScoreCity'
import { SolveEvent, Team, useCTFScreenData } from './useCTFScreenData'
import '../../styles/ctf-screen/fonts.css'
import '../../styles/ctf-screen/metal-screen.css'

interface CTFScreenPageProps {
  gameId: number
  demoMode?: boolean
}

interface ScreenData {
  teams: Team[]
  solveEvents: SolveEvent[]
  totalTeams: number
  totalSolves: number
  totalChallenges: number
  eventName: string
  startTime: Date
  endTime: Date
}

const SHANGHAI_TIMEZONE = 'Asia/Shanghai'

const demoTeamNames = [
  '蜀安一队',
  '青城实验室',
  '锦江攻防队',
  '岷山信安',
  '天府蓝队',
  '星火演练组',
  '云盾先锋',
  '凌云战队',
  '银翼安全',
  '沧海行动组',
  '玄武防线',
  '白泽实验组',
  '蜂巢研判',
  '川大靶场',
  '零信任小队',
]

const formatClock = (date: Date) =>
  date.toLocaleTimeString('zh-CN', {
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
    hour12: false,
    timeZone: SHANGHAI_TIMEZONE,
  })

const formatDate = (date: Date) =>
  date.toLocaleDateString('zh-CN', {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    timeZone: SHANGHAI_TIMEZONE,
  })

const formatDuration = (from: number, to: number) => {
  const diff = Math.max(0, to - from)
  const totalSeconds = Math.floor(diff / 1000)
  const hours = Math.floor(totalSeconds / 3600)
  const minutes = Math.floor((totalSeconds % 3600) / 60)
  const seconds = totalSeconds % 60
  return `${hours.toString().padStart(2, '0')}:${minutes.toString().padStart(2, '0')}:${seconds
    .toString()
    .padStart(2, '0')}`
}

const getCountdownLabel = (now: number, start: Date, end: Date) => {
  if (now < start.getTime()) return { label: '距离开始', value: formatDuration(now, start.getTime()) }
  if (now <= end.getTime()) return { label: '剩余时间', value: formatDuration(now, end.getTime()) }
  return { label: '比赛状态', value: '已结束' }
}

const rankClass = (rank: number) => (rank <= 3 ? `is-rank-${rank}` : '')

const makeDemoScreenData = (now: Date): ScreenData => {
  const baseScores = [
    2380, 2210, 2050, 1860, 1720, 1650, 1510, 1390, 1260, 1180,
    1090, 980, 910, 840, 760, 700, 650, 590, 540, 500,
    460, 420, 380, 350, 320, 290, 260, 230, 210, 190,
  ]
  const tick = Math.floor(now.getTime() / 2200)
  const teams = baseScores
    .map((score, index) => {
      const surge = ((tick + index * 5) % 23 === 0 ? 180 : 0) + ((tick + index * 7) % 41 === 0 ? 320 : 0)
      const drift = Math.round(Math.sin((tick + index * 1.73) * 0.31) * 90 + Math.cos(index * 0.91) * 42)
      return {
        id: index + 1,
        rank: index + 1,
        prevRank: index + 1,
        name: demoTeamNames[index % demoTeamNames.length] + (index >= demoTeamNames.length ? ` ${index + 1}` : ''),
        country: index < 10 ? '正式队伍' : '演练队伍',
        score: Math.max(40, score + drift + surge + tick * (index % 4 === 0 ? 7 : 3)),
        solves: Math.max(0, 28 - index + (index % 5) + Math.floor(tick / 7) % 4),
        lastSolve: index < 8 ? `${index + 1}分钟前` : '候场',
        color: '#d7dde0',
      }
    })
    .sort((left, right) => right.score - left.score)
    .map((team, index) => ({
      ...team,
      rank: index + 1,
      prevRank: Math.max(1, index + 1 + (((tick + team.id) % 9 === 0 ? 1 : 0) - ((tick + team.id) % 13 === 0 ? 1 : 0))),
    }))

  const startTime = new Date(now.getTime() - 2 * 60 * 60 * 1000)
  const endTime = new Date(now.getTime() + 6 * 60 * 60 * 1000)
  const solveEvents = teams.slice(0, 12).map((team, index) => ({
    id: `demo-${tick}-${team.id}`,
    team: team.name,
    teamColor: '#d7dde0',
    challenge: ['边界巡检', '凭证溯源', '协议分析', '权限校验', '日志研判', '流量复盘'][index % 6],
    category: ['Web', 'Crypto', 'Reverse', 'Forensics', 'Misc', 'AWDP'][index % 6],
    points: 100 + (index % 4) * 50,
    time: `${index + 1}分钟前`,
    isFirst: index < 3,
  }))

  return {
    teams,
    solveEvents,
    totalTeams: teams.length,
    totalSolves: teams.reduce((sum, team) => sum + team.solves, 0),
    totalChallenges: 48,
    eventName: '演示效果模式',
    startTime,
    endTime,
  }
}

const LeaderboardPanel: FC<{ teams: Team[] }> = ({ teams }) => {
  const list = teams.length > 8 ? [...teams, ...teams] : teams

  return (
    <section className="metal-screen-panel metal-screen-rank-panel">
      <div className="metal-panel-head">
        <div>
          <span className="metal-kicker">Scoreboard</span>
          <h2>实时排行</h2>
        </div>
        <span className="metal-live-badge">LIVE</span>
      </div>

      <div className={teams.length > 8 ? 'metal-rank-list is-scrolling' : 'metal-rank-list'}>
        <div className="metal-rank-track">
          {list.map((team, index) => {
            const delta = team.prevRank - team.rank
            return (
              <article key={`${team.id}-${index}`} className={`metal-rank-row ${rankClass(team.rank)}`}>
                <div className="metal-rank-number">{team.rank}</div>
                <div className="metal-rank-main">
                  <div className="metal-rank-name">{team.name}</div>
                  <div className="metal-rank-meta">
                    <span>{team.country}</span>
                    <span>{team.solves} 次解题</span>
                  </div>
                </div>
                <div className="metal-rank-score">
                  <strong>{team.score}</strong>
                  <span>{delta > 0 ? `上升 ${delta}` : delta < 0 ? `下降 ${Math.abs(delta)}` : '名次稳定'}</span>
                </div>
              </article>
            )
          })}
        </div>

        {teams.length === 0 && (
          <div className="metal-empty-state">
            <span>计分榜数据待接入</span>
            <strong>暂无排行数据</strong>
          </div>
        )}
      </div>
    </section>
  )
}

const SolveFeedPanel: FC<{ events: SolveEvent[] }> = ({ events }) => (
  <section className="metal-screen-panel metal-feed-panel">
    <div className="metal-panel-head">
      <div>
        <span className="metal-kicker">Live Feed</span>
        <h2>实时解题日志</h2>
      </div>
      <span className="metal-feed-count">{events.length}</span>
    </div>

    <div className="metal-feed-list">
      {events.slice(0, 13).map((event) => (
        <article key={event.id} className={event.isFirst ? 'metal-feed-item is-first' : 'metal-feed-item'}>
          <div className="metal-feed-topline">
            <span className="metal-feed-team">{event.team}</span>
            <span className="metal-feed-time">{event.time}</span>
          </div>
          <div className="metal-feed-challenge">{event.challenge}</div>
          <div className="metal-feed-bottomline">
            <span>{event.category}</span>
            <strong>{event.points} 分</strong>
          </div>
        </article>
      ))}

      {events.length === 0 && (
        <div className="metal-empty-state">
          <span>等待有效提交记录</span>
          <strong>暂无实时解题记录</strong>
        </div>
      )}
    </div>
  </section>
)

const CTFScreenPage: FC<CTFScreenPageProps> = ({ gameId, demoMode = false }) => {
  const navigate = useNavigate()
  const liveData = useCTFScreenData(gameId)
  const { data: games } = api.edit.useEditGetGames({ count: 100, skip: 0 }, OnceSWRConfig)
  const [currentTime, setCurrentTime] = useState(() => new Date())
  const [selectorOpen, setSelectorOpen] = useState(false)

  useEffect(() => {
    const timer = window.setInterval(() => setCurrentTime(new Date()), 1000)
    return () => window.clearInterval(timer)
  }, [])

  const demoData = useMemo(() => makeDemoScreenData(currentTime), [currentTime])
  const data: ScreenData = demoMode ? demoData : liveData
  const countdown = useMemo(
    () => getCountdownLabel(currentTime.getTime(), data.startTime, data.endTime),
    [currentTime, data.endTime, data.startTime]
  )
  const gameOptions = useMemo(
    () =>
      (games?.data ?? []).map((item) => ({
        value: String(item.id),
        label: item.title ?? `赛事 #${item.id}`,
      })),
    [games?.data]
  )
  const topTeams = useMemo(() => data.teams.slice(0, Math.min(Math.max(data.teams.length, 1), 64)), [data.teams])
  const leadingTeam = data.teams[0]

  return (
    <main className="metal-screen">
      <div className="metal-screen-ambient" />
      <div className="metal-screen-grid" />

      <header className="metal-screen-header">
        <div className="metal-brand-cluster">
          <div className="metal-brand-orb">
            <img src={yinyuIcon} alt="" draggable="false" />
          </div>
          <div className="metal-title-block">
            <div className="metal-brand-kicker">YINYU SECURITY RANGE</div>
            <button className="metal-title-button" type="button" onClick={() => setSelectorOpen((open) => !open)}>
              {data.eventName}
            </button>
            {selectorOpen && (
              <Select
                className="metal-game-switcher"
                data={gameOptions}
                dropdownOpened
                searchable
                value={String(gameId)}
                onChange={(value) => {
                  if (!value) return
                  navigate(`/admin/games/${value}/screen${demoMode ? '/demo' : ''}`)
                  setSelectorOpen(false)
                }}
                onBlur={() => window.setTimeout(() => setSelectorOpen(false), 120)}
              />
            )}
          </div>
        </div>

        <div className="metal-screen-stats">
          <div className="metal-stat">
            <span>队伍</span>
            <strong>{data.totalTeams}</strong>
          </div>
          <div className="metal-stat">
            <span>解题</span>
            <strong>{data.totalSolves}</strong>
          </div>
          <div className="metal-stat">
            <span>题目</span>
            <strong>{data.totalChallenges}</strong>
          </div>
          <div className="metal-stat is-wide">
            <span>{countdown.label}</span>
            <strong>{countdown.value}</strong>
          </div>
        </div>

        <div className="metal-clock-block">
          <span>{formatDate(currentTime)}</span>
          <strong>{formatClock(currentTime)}</strong>
        </div>
      </header>

      <section className="metal-screen-stage">
        <LeaderboardPanel teams={data.teams} />

        <section className="metal-city-stage">
          <div className="metal-city-hud">
            <div />
            <div className="metal-city-leader">
              <span>当前领先</span>
              <strong>{leadingTeam?.name ?? '--'}</strong>
            </div>
          </div>

          <MetalScoreCity teams={topTeams} />

          {topTeams.length === 0 && (
            <div className="metal-city-empty">
              <span>计分榜数据待接入</span>
              <strong>等待记分榜数据接入</strong>
            </div>
          )}
        </section>

        <SolveFeedPanel events={data.solveEvents} />
      </section>
    </main>
  )
}

export default CTFScreenPage
