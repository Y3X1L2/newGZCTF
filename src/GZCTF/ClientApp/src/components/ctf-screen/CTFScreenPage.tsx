import { FC, useEffect, useMemo, useState } from 'react'
import yinyuIcon from '../../assets/yinyu-icon-transparent.png'
import { MetalScoreCity } from './MetalScoreCity'
import { SolveEvent, Team, useCTFScreenData } from './useCTFScreenData'
import '../../styles/ctf-screen/fonts.css'
import '../../styles/ctf-screen/metal-screen.css'

interface CTFScreenPageProps {
  gameId: number
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
  return '待同步'
}

const getCountdownLabel = (now: number, start: Date, end: Date) => {
  if (now < start.getTime()) return { label: '距离开始', value: formatDuration(now, start.getTime()) }
  if (now <= end.getTime()) return { label: '剩余时间', value: formatDuration(now, end.getTime()) }
  return { label: '演练状态', value: '已结束' }
}

const rankClass = (rank: number) => (rank <= 3 ? `is-rank-${rank}` : '')

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
                <span>{team.solves} solves</span>
              </div>
            </div>
            <div className="metal-rank-score">
              <strong>{team.score}</strong>
              <span>{delta > 0 ? `+${delta}` : delta < 0 ? `${delta}` : 'stable'}</span>
            </div>
          </article>
        )
      })}

      {teams.length === 0 && (
        <div className="metal-empty-state">
          <span>Awaiting scoreboard data</span>
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
            <strong>{event.points} pts</strong>
          </div>
        </article>
      ))}

      {events.length === 0 && (
        <div className="metal-empty-state">
          <span>Waiting for accepted submissions</span>
          <strong>暂无实时解题记录</strong>
        </div>
      )}
    </div>
  </section>
)

const CTFScreenPage: FC<CTFScreenPageProps> = ({ gameId }) => {
  const data = useCTFScreenData(gameId)
  const [currentTime, setCurrentTime] = useState(() => new Date())

  useEffect(() => {
    const timer = window.setInterval(() => setCurrentTime(new Date()), 1000)
    return () => window.clearInterval(timer)
  }, [])

  const countdown = useMemo(
    () => getCountdownLabel(currentTime.getTime(), data.startTime, data.endTime),
    [currentTime, data.endTime, data.startTime]
  )
  const topTeams = useMemo(() => data.teams.slice(0, Math.min(Math.max(data.teams.length, 1), 48)), [data.teams])
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
            <p>安全综合演练态势大屏</p>
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
              <span className="metal-kicker">Dynamic Score City</span>
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
              <span>Score data stream is not ready</span>
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
        <span>系统状态：{getStatusText(data.statusInfo.status)}</span>
        <span>TOP 3 金色柱体 / 其他队伍银色柱体 / 分数变更实时缓动</span>
        <span>SCU Cyber Range</span>
      </footer>
    </main>
  )
}

export default CTFScreenPage
