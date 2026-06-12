import * as signalR from '@microsoft/signalr'
import { useEffect, useMemo, useRef, useState, useCallback } from 'react'
import { GameStatus } from '@Components/GameCard'
import { useChallengeCategoryLabelMap } from '@Utils/Shared'
import { OnceSWRConfig } from '@Hooks/useConfig'
import { getGameStatus, useAdminGame } from '@Hooks/useGame'
import api, { AnswerResult, EventType, GameEvent, ParticipationStatus, Submission } from '@Api'

// ─── Data Types (matching Ctfscreen interfaces) ────────────────────────────────

export interface Team {
  id: number
  rank: number
  prevRank: number
  name: string
  country: string
  score: number
  solves: number
  lastSolve: string
  color: string
}

export interface Category {
  name: string
  total: number
  solved: number
  color: string
  icon: string
}

export interface SolveEvent {
  id: string
  team: string
  teamColor: string
  challenge: string
  category: string
  points: number
  time: string
  isFirst: boolean
}

export interface HeatmapData {
  hour: string
  count: number
}

export interface ScoreData {
  ts: number
  time: string
  [teamName: string]: number | string
}

export interface HeaderProps {
  totalTeams: number
  totalSolves: number
  totalChallenges: number
  eventName: string
  startTime: Date
  endTime: Date
}

// ─── Constants ────────────────────────────────────────────────────────────────

const TEAM_COLORS = [
  '#00d4ff', '#00ff88', '#ff6b35', '#b347ff',
  '#ffd700', '#ff4466', '#00ffcc', '#ff9933',
  '#66ff66', '#4488ff', '#ff44aa', '#44ffee',
  '#ffaa00', '#aa44ff', '#88ff44'
]

const CATEGORY_ICONS: Record<string, string> = {
  Web: '🌐',
  Crypto: '🔐',
  Pwn: '⚡',
  Reverse: '🔄',
  Misc: '🎯',
  Forensics: '🔬',
  Hardware: '🔧',
}

const CATEGORY_COLORS: Record<string, string> = {
  Web: '#00d4ff',
  Crypto: '#b347ff',
  Pwn: '#ff4466',
  Reverse: '#ff6b35',
  Misc: '#00ff88',
  Forensics: '#ffd700',
  Hardware: '#00ffcc',
}

const MAX_EVENTS = 30
const MAX_SUBMISSIONS = 100

// ─── Helper Functions ──────────────────────────────────────────────────────────

const formatTimeAgo = (timestamp: number | undefined, now: number): string => {
  if (!timestamp) return '--'
  const diffMs = now - timestamp
  const diffMins = Math.floor(diffMs / 60000)
  if (diffMins < 1) return 'just now'
  if (diffMins < 60) return `${diffMins}m ago`
  const diffHours = Math.floor(diffMins / 60)
  return `${diffHours}h ${diffMins % 60}m ago`
}

const generateHeatmapData = (submissions: Submission[], now: number): HeatmapData[] => {
  const hourCounts: Record<string, number> = {}

  // Initialize last 12 hours
  for (let i = 11; i >= 0; i--) {
    const hour = new Date(now - i * 3600000)
    const hourStr = `${hour.getHours().toString().padStart(2, '0')}:00`
    hourCounts[hourStr] = 0
  }

  // Count submissions per hour (only accepted)
  submissions
    .filter(s => s.status === AnswerResult.Accepted && s.time)
    .forEach(s => {
      if (!s.time) return
      const hour = new Date(s.time)
      const hourStr = `${hour.getHours().toString().padStart(2, '0')}:00`
      if (hourCounts[hourStr] !== undefined) {
        hourCounts[hourStr]++
      }
    })

  return Object.entries(hourCounts)
    .map(([hour, count]) => ({ hour, count }))
    .sort((a, b) => a.hour.localeCompare(b.hour))
}

// ─── Main Data Hook ────────────────────────────────────────────────────────────

export const useCTFScreenData = (numId: number) => {
  const [now, setNow] = useState(() => Date.now())
  const [liveEvents, setLiveEvents] = useState<GameEvent[]>([])
  const [liveSubmissions, setLiveSubmissions] = useState<Submission[]>([])
  const [prevRankMap, setPrevRankMap] = useState(new Map<number, number>())
  const scoreboardSnapshotRef = useRef(new Map<number, { rank: number; score: number }>())

  const challengeCategoryLabelMap = useChallengeCategoryLabelMap()

  const { game } = useAdminGame(numId)
  const isTestMode = game?.isTest ?? false
  const statusInfo = getGameStatus(game)
  const canLoadScoreboard = numId > 0 && !!game && !isTestMode && statusInfo.status !== GameStatus.Coming
  const canLoadMonitor = numId > 0 && !!game && !isTestMode && statusInfo.status !== GameStatus.Coming
  const canLoadParticipations = numId > 0 && !!game && !isTestMode

  const { data: liveScoreboard, mutate: mutateScoreboard } = api.game.useGameScoreboard(
    numId,
    {
      ...OnceSWRConfig,
      refreshInterval: statusInfo.status === GameStatus.OnGoing ? 30000 : 0,
    },
    canLoadScoreboard
  )
  const { data: liveParticipations } = api.game.useGameParticipations(numId, OnceSWRConfig, canLoadParticipations)
  const { data: initialEvents } = api.game.useGameEvents(
    numId,
    { hideContainer: true, count: MAX_EVENTS },
    OnceSWRConfig,
    canLoadMonitor
  )
  const { data: initialSubmissions } = api.game.useGameSubmissions(
    numId,
    { count: MAX_SUBMISSIONS },
    OnceSWRConfig,
    canLoadMonitor
  )

  const scoreboard = liveScoreboard
  const participations = liveParticipations
  const eventFeed = liveEvents
  const submissionFeed = liveSubmissions

  // Clock update
  useEffect(() => {
    const timer = window.setInterval(() => setNow(Date.now()), 1000)
    return () => window.clearInterval(timer)
  }, [])

  // Initialize events and submissions
  useEffect(() => {
    if (isTestMode) return
    if (initialEvents) setLiveEvents(initialEvents.slice(0, MAX_EVENTS))
  }, [initialEvents, isTestMode])

  useEffect(() => {
    if (isTestMode) return
    if (initialSubmissions) setLiveSubmissions(initialSubmissions.slice(0, MAX_SUBMISSIONS))
  }, [initialSubmissions, isTestMode])

  // Track rank changes
  useEffect(() => {
    if (!scoreboard?.items) return

    const nextPrevRank = new Map<number, number>()
    for (const item of scoreboard.items) {
      const previous = scoreboardSnapshotRef.current.get(item.id)
      nextPrevRank.set(item.id, previous ? previous.rank : item.rank)
    }

    scoreboardSnapshotRef.current = new Map(
      scoreboard.items.map((item) => [item.id, { rank: item.rank, score: item.score }])
    )
    setPrevRankMap(nextPrevRank)
  }, [scoreboard?.items, scoreboard?.updateTimeUtc])

  // SignalR connection (only for non-test mode and ongoing games)
  useEffect(() => {
    if (isTestMode || statusInfo.status !== GameStatus.OnGoing || numId <= 0) return

    const connection = new signalR.HubConnectionBuilder()
      .withUrl(`/hub/monitor?game=${numId}`)
      .withHubProtocol(new signalR.JsonHubProtocol())
      .withAutomaticReconnect()
      .configureLogging(signalR.LogLevel.None)
      .build()

    connection.serverTimeoutInMilliseconds = 60 * 1000 * 60 * 2

    connection.on('ReceivedGameEvent', (event: GameEvent) => {
      if (event.type === EventType.ContainerStart || event.type === EventType.ContainerDestroy) return
      setLiveEvents(current => [event, ...current].slice(0, MAX_EVENTS))
    })

    connection.on('ReceivedSubmissions', (submission: Submission) => {
      setLiveSubmissions(current => [submission, ...current].slice(0, MAX_SUBMISSIONS))

      if (submission.status === AnswerResult.Accepted) {
        void mutateScoreboard()
      }
    })

    void connection.start().catch(() => undefined)
    return () => {
      void connection.stop()
    }
  }, [isTestMode, mutateScoreboard, numId, statusInfo.status])

  // ─── Derived Data (matching Ctfscreen format) ────────────────────────────────

  const acceptedParticipations = useMemo(
    () => (participations ?? []).filter((item) => item.status === ParticipationStatus.Accepted),
    [participations]
  )

  // Teams for leaderboard
  const teams: Team[] = useMemo(() => {
    const items = scoreboard?.items ?? []
    return items
      .sort((a, b) => a.rank - b.rank)
      .map((item, index) => ({
        id: item.id,
        rank: item.rank,
        prevRank: prevRankMap.get(item.id) ?? item.rank,
        name: item.name,
        country: item.divisionId ? `Division ${item.divisionId}` : 'Official',
        score: item.score,
        solves: item.solvedCount,
        lastSolve: formatTimeAgo(item.lastSubmissionTime, now),
        color: TEAM_COLORS[index % TEAM_COLORS.length],
      }))
  }, [scoreboard?.items, prevRankMap, now])

  // Score history for chart - with forward-fill to show cumulative scores
  const scoreHistory: ScoreData[] = useMemo(() => {
    // Step 1: Build team timelines from API data or fallback to scoreboard items
    interface TimelineTeam { name: string; items: Array<{ time: number; score: number }> }

    let timelineTeams: TimelineTeam[] = []

    const rawTimelines = scoreboard?.timelines?.find(t => !t.divisionId || t.divisionId === 0)?.teams
      ?? scoreboard?.timelines?.[0]?.teams

    if (rawTimelines && rawTimelines.length > 0 && rawTimelines.some(t => t.items && t.items.length > 0)) {
      timelineTeams = rawTimelines.slice(0, 5).map(t => ({ name: t.name, items: t.items }))
    } else {
      // Fallback: derive cumulative score history from scoreboard.items
      // Each accepted submission in the event feed becomes a score point
      const teamScores = new Map<string, Array<{ time: number; score: number }>>()
      const acceptedSubs = submissionFeed
        .filter((s): s is typeof s & { time: number } => s.status === AnswerResult.Accepted && !!s.time && !!s.team)
        .sort((a, b) => a.time - b.time)

      // Use scoreboard items to get each team's current total score
      const teamTotals = new Map<string, number>()
      for (const item of (scoreboard?.items ?? [])) {
        teamTotals.set(item.name, item.score)
      }

      // Build cumulative score per team from submissions
      for (const sub of acceptedSubs) {
        if (!sub.time || !sub.team) continue
        const entry = teamScores.get(sub.team) ?? []
        const prevScore = entry.length > 0 ? entry[entry.length - 1].score : 0
        // Estimate challenge score from total - we use the team's total as a reference
        entry.push({ time: sub.time, score: 0 }) // placeholder, will be fixed below
        teamScores.set(sub.team, entry)
      }

      // Since we can't easily derive individual challenge scores from submissions alone,
      // use scoreboard.items rank-ordered top 5 teams with their current scores as single points
      const topTeams = [...(scoreboard?.items ?? [])]
        .sort((a, b) => a.rank - b.rank)
        .slice(0, 5)

      timelineTeams = topTeams.map(item => ({
        name: item.name,
        items: item.lastSubmissionTime
          ? [{ time: item.lastSubmissionTime, score: item.score }]
          : [{ time: Date.now(), score: item.score }],
      }))
    }

    if (timelineTeams.length === 0) return []

    const top5 = timelineTeams

    // Collect all unique timestamps
    const allTimestamps = new Set<number>()
    top5.forEach(team => {
      team.items.forEach(item => allTimestamps.add(item.time))
    })

    const sortedTimestamps = Array.from(allTimestamps).sort((a, b) => a - b)
    if (sortedTimestamps.length === 0) return []

    // Build a sorted map for each team's score history
    const teamScoreMap = new Map<string, Array<{ time: number; score: number }>>()
    top5.forEach(team => {
      const sorted = team.items.slice().sort((a, b) => a.time - b.time)
      teamScoreMap.set(team.name, sorted)
    })

    // Generate chart data with forward-fill: each team carries forward their last known score
    return sortedTimestamps.map(timestamp => {
      const point: ScoreData = {
        ts: timestamp,
        time: new Date(timestamp).toLocaleTimeString('zh-CN', { hour: '2-digit', minute: '2-digit', hour12: false })
      }

      top5.forEach(team => {
        const history = teamScoreMap.get(team.name) ?? []
        let lastScore = 0
        for (const entry of history) {
          if (entry.time <= timestamp) {
            lastScore = entry.score
          } else {
            break
          }
        }
        point[team.name] = lastScore
      })

      return point
    })
  }, [scoreboard?.timelines, scoreboard?.items, submissionFeed])

  // Categories for stats
  const categories: Category[] = useMemo(() => {
    const challenges = scoreboard?.challenges ?? {}
    const categoryMap = new Map<string, { total: number; solved: number }>()

    Object.entries(challenges).forEach(([categoryKey, challengeList]) => {
      const categoryName = challengeCategoryLabelMap.get(categoryKey as never)?.desrc ?? categoryKey
      const current = categoryMap.get(categoryName) ?? { total: 0, solved: 0 }
      current.total += challengeList.length
      current.solved += challengeList.filter(c => c.solved > 0).length
      categoryMap.set(categoryName, current)
    })

    return Array.from(categoryMap.entries())
      .map(([name, data]) => ({
        name,
        total: data.total,
        solved: data.solved,
        color: CATEGORY_COLORS[name] ?? '#00d4ff',
        icon: CATEGORY_ICONS[name] ?? '🎯',
      }))
      .sort((a, b) => b.solved - a.solved)
      .slice(0, 6)
  }, [scoreboard?.challenges, challengeCategoryLabelMap])

  // Solve events for recent solves feed
  const solveEvents: SolveEvent[] = useMemo(() => {
    // Get first bloods from challenges
    const firstBloods = new Map<string, { team: string; time: number }>()
    Object.values(scoreboard?.challenges ?? {})
      .flat()
      .forEach(challenge => {
        if (challenge.bloods?.[0]) {
          firstBloods.set(challenge.title, {
            team: challenge.bloods[0].name,
            time: challenge.bloods[0].submitTimeUtc ?? 0,
          })
        }
      })

    return submissionFeed
      .filter(s => s.status === AnswerResult.Accepted && s.time)
      .slice(0, 12)
      .map((submission, index) => {
        const teamIndex = teams.findIndex(t => t.name === submission.team) ?? index
        const categoryKey = Object.entries(scoreboard?.challenges ?? {})
          .find(([_, challenges]) => challenges.some(c => c.title === submission.challenge))
          ?.[0] ?? 'Misc'
        const categoryName = challengeCategoryLabelMap.get(categoryKey as never)?.desrc ?? categoryKey
        const challengeInfo = Object.values(scoreboard?.challenges ?? {})
          .flat()
          .find(c => c.title === submission.challenge)

        const firstBlood = firstBloods.get(submission.challenge ?? '')
        const isFirstBlood = firstBlood?.team === submission.team && firstBlood?.time === submission.time

        return {
          id: `${submission.time}-${submission.team}-${submission.challenge}`,
          team: submission.team ?? 'Unknown',
          teamColor: TEAM_COLORS[teamIndex % TEAM_COLORS.length],
          challenge: submission.challenge ?? 'Unknown',
          category: categoryName,
          points: challengeInfo?.score ?? 0,
          time: formatTimeAgo(submission.time, now),
          isFirst: isFirstBlood,
        }
      })
  }, [submissionFeed, scoreboard?.challenges, teams, challengeCategoryLabelMap, now])

  // Heatmap data
  const heatmapData: HeatmapData[] = useMemo(() => {
    return generateHeatmapData(submissionFeed, now)
  }, [submissionFeed, now])

  // Stats
  const totalTeams = useMemo(() => acceptedParticipations.length, [acceptedParticipations])
  const totalSolves = useMemo(
    () => (scoreboard?.items ?? []).reduce((sum, item) => sum + item.solvedCount, 0),
    [scoreboard?.items]
  )
  const totalChallenges = scoreboard?.challengeCount ?? 0
  const eventName = game?.title ?? 'CTF Competition'
  const startTime = useMemo(() => {
    if (!game?.start) return new Date(Date.now() - 3600000)
    return new Date(game.start)
  }, [game?.start])
  const endTime = useMemo(() => {
    if (!game?.end) return new Date(Date.now() + 3600000)
    return new Date(game.end)
  }, [game?.end])

  // Additional stats for heatmap panel
  const totalBlood = useMemo(() => {
    return Object.values(scoreboard?.challenges ?? {})
      .flat()
      .filter(c => c.bloods?.length > 0)
      .length
  }, [scoreboard?.challenges])

  const avgScore = useMemo(() => {
    const items = scoreboard?.items ?? []
    if (items.length === 0) return 0
    return Math.round(items.reduce((sum, item) => sum + item.score, 0) / items.length)
  }, [scoreboard?.items])

  const activeTeams = useMemo(() => {
    const recentActive = submissionFeed
      .filter(s => s.time && now - s.time <= 10 * 60 * 1000)
      .map(s => s.team)
    return new Set(recentActive).size
  }, [submissionFeed, now])

  // Top 5 teams for chart
  const top5Teams = useMemo(() => {
    return teams.slice(0, 5).map(t => ({ name: t.name, color: t.color }))
  }, [teams])

  return {
    teams,
    scoreHistory,
    categories,
    solveEvents,
    heatmapData,
    totalTeams,
    totalSolves,
    totalChallenges,
    eventName,
    startTime,
    endTime,
    totalBlood,
    avgScore,
    activeTeams,
    top5Teams,
    game,
    statusInfo,
  }
}
