import { FC, useEffect, useMemo, useState } from 'react'
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
  statusInfo: {
    status: unknown
  }
}

const SHANGHAI_TIMEZONE = 'Asia/Shanghai'

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

const formatRange = (start: Date, end: Date) =>
  `${start.toLocaleString('zh-CN', {
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    hour12: false,
    timeZone: SHANGHAI_TIMEZONE,
  })} - ${end.toLocaleString('zh-CN', {
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    hour12: false,
    timeZone: SHANGHAI_TIMEZONE,
  })}`

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

const getStatusText = (status: unknown) => {
  const value = String(status).toLowerCase()
  if (value.includes('coming')) return '待启动'
  if (value.includes('ended')) return '已结束'
  if (value.includes('ongoing') || value.includes('running')) return '演练中'
  return '准备中'
}

const getCountdownLabel = (now: number, start: Date, end: Date) => {
  if (now < start.getTime()) return { label: '距离开始', value: formatDuration(now, start.getTime()) }
  if (now <= end.getTime()) return { label: '剩余时间', value: formatDuration(now, end.getTime()) }
  return { label: '演练状态', value: '已结束' }
}

const rankClass = (rank: number) => (rank <= 3 ? `is-rank-${rank}` : '')

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

const makeDemoScreenData = (now: Date): ScreenData => {
  const baseScores = [
    2380, 2210, 2050, 1860, 1720, 1650, 1510, 1390, 1260, 1180,
    1090, 980, 910, 840, 760, 700, 650, 590, 540, 500,
    460, 420, 380, 350, 320, 290, 260, 230, 210, 190,
  ]
  const pulse = Math.floor(now.getTime() / 2600)
  const teams = baseScores.map((score, index) => {
    const drift = Math.round(Math.sin((pulse + index * 1.73) * 0.31) * 34 + Math.cos(index * 0.9) * 18)
    return {
      id: index + 1,
      rank: index + 1,
      prevRank: index + 1 + (index > 2 && index % 7 === 0 ? 1 : 0),
      name: demoTeamNames[index % demoTeamNames.length] + (index >= demoTeamNames.length ? ` ${index + 1}` : ''),
      country: index < 10 ? '正式队伍' : '展示队伍',
      score: Math.max(40, score + drift),
      solves: Math.max(0, 28 - index + (index % 5)),
      lastSolve: index < 6 ? `${index + 2}分钟前` : '候场',
      color: '#d7dde0',
    }
  })
  const startTime = new Date(now.getTime() - 2 * 60 * 60 * 1000)
  const endTime = new Date(now.getTime() + 6 * 60 * 60 * 1000)
  const solveEvents = teams.slice(0, 10).map((team, index) => ({
    id: `demo-${index}-${pulse}`,
    team: team.name,
    teamColor: '#d7dde0',
    challenge: ['边界巡检', '凭证溯源', '协议分析', '权限校验', '日志研判'][index % 5],
    category: ['Web', 'Crypto', 'Reverse', 'Forensics', 'Misc'][index % 5],
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
    statusInfo: { status: 'ongoing' },
  }
}

const LeaderboardPanel: FC<{ teams: Team[] }> = ({ teams }) => (
  <section className="metal-screen-panel metal-screen-rank-panel">
    <div className="metal-panel-head">
      <div>
        <span className="metal-kicker">Scoreboard</span>
        <h2>实时排行</h2>
      </div>
      <span className="metal-live-badge">LIVE</span>
    </div>

    <div className="metal-rank-list">
      {teams.slice(0, 12).map((team) => {
        const delta = team.prevRank - team.rank
        return (
          <article key={team.id} className={`metal-rank-row ${rankClass(team.rank)}`}>
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

      {teams.length === 0 && (
        <div className="metal-empty-state">
          <span>计分榜数据待接入</span>
          <strong>暂无排行数据</strong>
        </div>
      )}
    </div>
  </section>
)

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
  const liveData = useCTFScreenData(gameId)
  const [currentTime, setCurrentTime] = useState(() => new Date())

  useEffect(() => {
    const timer = window.setInterval(() => setCurrentTime(new Date()), 1000)
    return () => window.clearInterval(timer)
  }, [])

  const demoData = useMemo(() => makeDemoScreenData(currentTime), [currentTime])
  const data = demoMode ? demoData : liveData

  const countdown = useMemo(
    () => getCountdownLabel(currentTime.getTime(), data.startTime, data.endTime),
    [currentTime, data.endTime, data.startTime]
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
          <div>
            <div className="metal-brand-kicker">YINYU SECURITY RANGE</div>
            <h1>{data.eventName}</h1>
            <p>{demoMode ? '安全综合演练展示模式' : '安全综合演练竞赛态势大屏'}</p>
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
            <div>
              <span className="metal-kicker">Score Projection</span>
              <h2>分数金属城市</h2>
            </div>
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

          <div className="metal-city-footer">
            <span>{formatRange(data.startTime, data.endTime)}</span>
            <span>{getStatusText(data.statusInfo.status)}</span>
          </div>
        </section>

        <SolveFeedPanel events={data.solveEvents} />
      </section>

      <footer className="metal-screen-footer">
        <span>展示状态：{demoMode ? '演示效果模式' : getStatusText(data.statusInfo.status)}</span>
        <span>前三名金色柱体 / 其他队伍银色柱体 / 分数变化实时缓动</span>
        <span>SCU Cyber Range</span>
      </footer>
    </main>
  )
}

export default CTFScreenPage
