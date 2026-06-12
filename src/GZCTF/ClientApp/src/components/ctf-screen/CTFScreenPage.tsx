import { FC, useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react'
import { useNavigate } from 'react-router'
import { Select } from '@mantine/core'
import gsap from 'gsap'
import { OnceSWRConfig } from '@Hooks/useConfig'
import api from '@Api'
import yinyuIcon from '../../assets/yinyu-icon-transparent.png'
import { MetalScoreCity } from './MetalScoreCity'
import { useCTFScreenData } from './useCTFScreenData'
import type { SolveEvent, Team, TeamCategoryBreakdown } from './useCTFScreenData'
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

const formatScore = (score: number) => Math.round(score).toLocaleString('zh-CN')

const makeDemoBreakdown = (teamId: number, score: number, solves: number): TeamCategoryBreakdown[] => {
  const weights = [
    { category: 'Web', weight: 0.28 },
    { category: 'Reverse', weight: 0.22 },
    { category: 'Crypto', weight: 0.18 },
    { category: 'Forensics', weight: 0.16 },
    { category: 'AWDP', weight: 0.16 },
  ]

  return weights
    .map((item, index) => {
      const factor = 0.78 + (((teamId * 13 + index * 7) % 21) / 100)
      return {
        category: item.category,
        score: Math.max(0, Math.round(score * item.weight * factor)),
        solved: Math.max(0, Math.round(solves * item.weight * factor)),
      }
    })
    .filter((item) => item.score > 0 || item.solved > 0)
}

const makeDemoScreenData = (now: Date): ScreenData => {
  const baseScores = [
    2380, 2210, 2050, 1860, 1720, 1650, 1510, 1390, 1260, 1180,
    1090, 980, 910, 840, 760, 700, 650, 590, 540, 500,
    460, 420, 380, 350, 320, 290, 260, 230, 210, 190,
  ]
  const tick = Math.floor((now.getTime() % (90 * 60 * 1000)) / 2800)
  const teams = baseScores
    .map((score, index) => {
      const surge = ((tick + index * 5) % 23 === 0 ? 180 : 0) + ((tick + index * 7) % 41 === 0 ? 300 : 0)
      const drift = Math.round(Math.sin((tick + index * 1.73) * 0.22) * 64 + Math.cos(index * 0.91) * 34)
      const stagedGain = (Math.floor(tick / 8) % 24) * (index % 4 === 0 ? 9 : index % 3 === 0 ? 6 : 3)
      const finalScore = Math.max(40, score + drift + surge + stagedGain)
      const finalSolves = Math.max(0, 28 - index + (index % 5) + Math.floor(tick / 7) % 4)
      return {
        id: index + 1,
        rank: index + 1,
        prevRank: index + 1,
        name: demoTeamNames[index % demoTeamNames.length] + (index >= demoTeamNames.length ? ` ${index + 1}` : ''),
        country: index < 10 ? '正式队伍' : '演练队伍',
        score: finalScore,
        solves: finalSolves,
        lastSolve: index < 8 ? `${index + 1}分钟前` : '候场',
        color: '#d7dde0',
        breakdown: makeDemoBreakdown(index + 1, finalScore, finalSolves),
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
  const feedTick = Math.floor(now.getTime() / 6000)
  const demoChallenges = [
    { category: 'Web', challenge: '边界巡检', points: 100 },
    { category: 'Crypto', challenge: '凭证溯源', points: 150 },
    { category: 'Reverse', challenge: '协议分析', points: 200 },
    { category: 'Forensics', challenge: '日志研判', points: 250 },
    { category: 'Misc', challenge: '流量复盘', points: 100 },
    { category: 'AWDP', challenge: '服务加固', points: 300 },
  ]
  const solveEvents = Array.from({ length: 14 }, (_, index) => {
    const eventTick = feedTick - index
    const teamIndex = Math.abs(eventTick * 7 + index * 3) % baseScores.length
    const challenge = demoChallenges[Math.abs(eventTick + index) % demoChallenges.length]

    return {
      id: `demo-feed-${eventTick}`,
      team:
        demoTeamNames[teamIndex % demoTeamNames.length] +
        (teamIndex >= demoTeamNames.length ? ` ${teamIndex + 1}` : ''),
      teamColor: '#d7dde0',
      challenge: challenge.challenge,
      category: challenge.category,
      points: challenge.points,
      time: index === 0 ? '刚刚' : `${Math.max(1, Math.ceil((index * 6) / 60))}分钟前`,
      isFirst: eventTick % 17 === 0,
    }
  })

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

const RollingScore: FC<{ value: number }> = ({ value }) => {
  const scoreRef = useRef<HTMLElement | null>(null)
  const currentRef = useRef(value)

  useLayoutEffect(() => {
    const node = scoreRef.current
    if (!node) return undefined

    const from = currentRef.current
    if (from === value) {
      node.textContent = formatScore(value)
      return undefined
    }

    const proxy = { score: from }
    const tween = gsap.to(proxy, {
      score: value,
      duration: 0.9,
      ease: 'power3.out',
      onUpdate: () => {
        node.textContent = formatScore(proxy.score)
      },
      onComplete: () => {
        currentRef.current = value
        node.textContent = formatScore(value)
      },
    })

    currentRef.current = value
    return () => {
      tween.kill()
    }
  }, [value])

  return <strong ref={scoreRef}>{formatScore(value)}</strong>
}

const LeaderboardPanel: FC<{ teams: Team[] }> = ({ teams }) => {
  const visibleTeams = teams.slice(0, 20)
  const rowRefs = useRef(new Map<number, HTMLElement>())
  const rowPositionsRef = useRef(new Map<number, number>())
  const rankSignature = visibleTeams
    .map((team) => `${team.id}:${team.rank}:${team.prevRank}:${team.score}:${team.solves}:${team.name}`)
    .join('|')

  useLayoutEffect(() => {
    const previousPositions = rowPositionsRef.current
    const nextPositions = new Map<number, number>()

    rowRefs.current.forEach((node, id) => {
      const top = node.getBoundingClientRect().top
      const previousTop = previousPositions.get(id)

      if (previousTop === undefined) {
        gsap.fromTo(
          node,
          { autoAlpha: 0, y: 12, scale: 0.985 },
          { autoAlpha: 1, y: 0, scale: 1, duration: 0.45, ease: 'power3.out', clearProps: 'transform' }
        )
      } else {
        const delta = previousTop - top
        if (Math.abs(delta) > 1) {
          gsap.fromTo(
            node,
            { y: delta },
            { y: 0, duration: 0.62, ease: 'power3.out', clearProps: 'transform' }
          )
        }
      }

      nextPositions.set(id, top)
    })

    rowPositionsRef.current = nextPositions
  }, [rankSignature])

  return (
    <section className="metal-screen-panel metal-screen-rank-panel">
      <div className="metal-panel-head">
        <div>
          <span className="metal-kicker">Scoreboard</span>
          <h2>实时排行</h2>
        </div>
        <span className="metal-live-badge">LIVE</span>
      </div>

      <div className="metal-rank-list">
        <div className="metal-rank-track">
          {visibleTeams.map((team) => {
            const delta = team.prevRank - team.rank
            return (
              <article
                key={team.id}
                ref={(node) => {
                  if (node) rowRefs.current.set(team.id, node)
                  else rowRefs.current.delete(team.id)
                }}
                className={`metal-rank-row ${rankClass(team.rank)}`}
              >
                <div className="metal-rank-number">{team.rank}</div>
                <div className="metal-rank-main">
                  <div className="metal-rank-name">{team.name}</div>
                  <div className="metal-rank-meta">
                    <span>{team.country}</span>
                    <span>{team.solves} 次解题</span>
                  </div>
                </div>
                <div className="metal-rank-score">
                  <RollingScore value={team.score} />
                  <span>{delta > 0 ? `上升 ${delta}` : delta < 0 ? `下降 ${Math.abs(delta)}` : '名次稳定'}</span>
                </div>
              </article>
            )
          })}
        </div>

        {visibleTeams.length === 0 && (
          <div className="metal-empty-state">
            <span>计分榜数据待接入</span>
            <strong>暂无排行数据</strong>
          </div>
        )}
      </div>
    </section>
  )
}

const SolveFeedPanel: FC<{ events: SolveEvent[] }> = ({ events }) => {
  const maxEvents = 14
  const incomingEvents = useMemo(() => events.slice(0, maxEvents).reverse(), [events])
  const [visibleEvents, setVisibleEvents] = useState<SolveEvent[]>(() => incomingEvents)
  const itemRefs = useRef(new Map<string, HTMLElement>())
  const itemPositionsRef = useRef(new Map<string, number>())
  const newEventIdsRef = useRef<string[]>([])
  const incomingEventsRef = useRef(incomingEvents)
  const eventIdsSignature = incomingEvents.map((event) => event.id).join('|')

  incomingEventsRef.current = incomingEvents

  useEffect(() => {
    const nextEvents = incomingEventsRef.current
    setVisibleEvents((current) => {
      const currentIds = new Set(current.map((event) => event.id))
      const newEvents = nextEvents.filter((event) => !currentIds.has(event.id))
      newEventIdsRef.current = newEvents.map((event) => event.id)
      return nextEvents
    })
  }, [eventIdsSignature])

  useLayoutEffect(() => {
    const previousPositions = itemPositionsRef.current
    const nextPositions = new Map<string, number>()
    const newIds = new Set(newEventIdsRef.current)

    itemRefs.current.forEach((node, id) => {
      const top = node.getBoundingClientRect().top
      const previousTop = previousPositions.get(id)

      if (previousTop !== undefined) {
        const delta = previousTop - top
        if (Math.abs(delta) > 1) {
          gsap.fromTo(
            node,
            { y: delta },
            { y: 0, duration: 0.56, ease: 'power3.out', clearProps: 'transform' }
          )
        }
      } else if (newIds.has(id)) {
        gsap.fromTo(
          node,
          { autoAlpha: 0, y: 14, scale: 0.982 },
          { autoAlpha: 1, y: 0, scale: 1, duration: 0.48, ease: 'power3.out', clearProps: 'transform' }
        )
      }

      nextPositions.set(id, top)
    })

    itemPositionsRef.current = nextPositions
    newEventIdsRef.current = []
  }, [visibleEvents])

  return (
    <section className="metal-screen-panel metal-feed-panel">
      <div className="metal-panel-head">
        <div>
          <span className="metal-kicker">Live Feed</span>
          <h2>实时解题日志</h2>
        </div>
        <span className="metal-feed-count">{events.length}</span>
      </div>

      <div className="metal-feed-list">
        <div className="metal-feed-track">
          {visibleEvents.map((event) => (
            <article
              key={event.id}
              ref={(node) => {
                if (node) itemRefs.current.set(event.id, node)
                else itemRefs.current.delete(event.id)
              }}
              className={event.isFirst ? 'metal-feed-item is-first' : 'metal-feed-item'}
            >
              <div className="metal-feed-topline">
                <span className="metal-feed-team">{event.team}</span>
                <span className="metal-feed-time">{event.time}</span>
              </div>
              <div className="metal-feed-challenge">解题 [{event.category}] {event.challenge}</div>
              <div className="metal-feed-bottomline">
                <span>得分记录</span>
                <strong>+{event.points} 分</strong>
              </div>
            </article>
          ))}
        </div>

        {visibleEvents.length === 0 && (
          <div className="metal-empty-state">
            <span>等待有效提交记录</span>
            <strong>暂无实时解题记录</strong>
          </div>
        )}
      </div>
    </section>
  )
}

const TeamDetailPanel: FC<{ team: Team; onBack: () => void }> = ({ team, onBack }) => {
  const maxCategoryScore = Math.max(1, ...team.breakdown.map((item) => item.score), 1)

  return (
    <section className="metal-screen-panel metal-team-detail-panel">
      <div className="metal-panel-head metal-team-detail-head">
        <div>
          <span className="metal-kicker">Team Detail</span>
          <h2>队伍详情</h2>
        </div>
        <button className="metal-detail-back-button" type="button" onClick={onBack}>
          返回总览
        </button>
      </div>
      <div className="metal-team-detail-title">
        <div>
          <strong>{team.name}</strong>
          <span>{team.country}</span>
        </div>
        <b>#{team.rank.toString().padStart(2, '0')}</b>
      </div>

      <div className="metal-team-detail-stats">
        <div>
          <span>总分</span>
          <strong>{formatScore(team.score)}</strong>
        </div>
        <div>
          <span>解题</span>
          <strong>{team.solves}</strong>
        </div>
      </div>

      <div className="metal-team-breakdown">
        {team.breakdown.length > 0 ? (
          team.breakdown.map((item) => (
            <div className="metal-team-breakdown-row" key={item.category}>
              <div>
                <span>{item.category}</span>
                <strong>{item.solved} 道</strong>
              </div>
              <div className="metal-team-breakdown-bar">
                <i style={{ width: `${Math.max(8, (item.score / maxCategoryScore) * 100)}%` }} />
              </div>
              <em>{formatScore(item.score)}</em>
            </div>
          ))
        ) : (
          <div className="metal-team-breakdown-empty">暂无分类得分记录</div>
        )}
      </div>
    </section>
  )
}

const CTFScreenPage: FC<CTFScreenPageProps> = ({ gameId, demoMode = false }) => {
  const navigate = useNavigate()
  const liveData = useCTFScreenData(gameId)
  const { data: games } = api.edit.useEditGetGames({ count: 100, skip: 0 }, OnceSWRConfig)
  const [currentTime, setCurrentTime] = useState(() => new Date())
  const [selectorOpen, setSelectorOpen] = useState(false)
  const [selectedTeamId, setSelectedTeamId] = useState<number | null>(null)

  useEffect(() => {
    const timer = window.setInterval(() => setCurrentTime(new Date()), 1000)
    return () => window.clearInterval(timer)
  }, [])

  const demoTick = Math.floor((currentTime.getTime() % (90 * 60 * 1000)) / 2800)
  const demoData = useMemo(() => makeDemoScreenData(currentTime), [demoTick])
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
  const scoreTeamSignature = data.teams
    .slice(0, 64)
    .map((team) => {
      const breakdownSignature = team.breakdown
        .map((item) => `${item.category}:${item.solved}:${item.score}`)
        .join(',')
      return `${team.id}:${team.rank}:${team.prevRank}:${team.name}:${team.country}:${team.score}:${team.solves}:${team.color}:${breakdownSignature}`
    })
    .join('|')
  const scoreTeams = useMemo(
    () =>
      data.teams.slice(0, 64).map((team) => ({
        ...team,
        lastSolve: '',
      })),
    [scoreTeamSignature]
  )
  const rankTeams = useMemo(() => scoreTeams.slice(0, 20), [scoreTeams])
  const topTeams = useMemo(() => scoreTeams.slice(0, Math.min(Math.max(scoreTeams.length, 1), 64)), [scoreTeams])
  const leadingTeam = data.teams[0]
  const selectedTeam = useMemo(
    () => topTeams.find((team) => team.id === selectedTeamId) ?? null,
    [selectedTeamId, topTeams]
  )

  useEffect(() => {
    if (selectedTeamId === null) return
    if (!topTeams.some((team) => team.id === selectedTeamId)) {
      setSelectedTeamId(null)
    }
  }, [selectedTeamId, topTeams])

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
        {selectedTeam ? (
          <TeamDetailPanel team={selectedTeam} onBack={() => setSelectedTeamId(null)} />
        ) : (
          <LeaderboardPanel teams={rankTeams} />
        )}

        <section className="metal-city-stage">
          <div className="metal-city-hud">
            <div />
            <div className="metal-city-leader">
              <span>当前领先</span>
              <strong>{leadingTeam?.name ?? '--'}</strong>
            </div>
          </div>

          <MetalScoreCity
            teams={topTeams}
            selectedTeamId={selectedTeamId}
            onSelectTeam={(team) => setSelectedTeamId(team.id)}
          />

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
