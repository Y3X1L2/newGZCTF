import * as signalR from '@microsoft/signalr'
import { useEffect, useMemo, useRef, useState, useCallback } from 'react'
import { GameStatus } from '@Components/GameCard'
import { useChallengeCategoryLabelMap } from '@Utils/Shared'
import { OnceSWRConfig } from '@Hooks/useConfig'
import { getGameStatus, useAdminGame } from '@Hooks/useGame'
import api, { AnswerResult, EventType, GameEvent, GameType, ParticipationStatus, ScoreboardItem, Submission } from '@Api'
import {
  AwdpAttackLogItem,
  awdpAdminApi,
  AwdpScoreboardItem,
  AwdpServiceViewModel,
} from '../../Api/AwdpApi'
import { theoryAdminApi, theoryPlayerApi, TheoryPaperDetailModel, TheoryScoreboardItemModel } from '../../Api/TheoryApi'

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
  breakdown: TeamCategoryBreakdown[]
}

export interface TeamCategoryBreakdown {
  category: string
  solved: number
  score: number
}

interface ScreenScoreItem {
  id: number
  name: string
  divisionId?: number | null
  score: number
  rank: number
  solvedCount: number
  lastSubmissionTime?: number
  breakdown: TeamCategoryBreakdown[]
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

const MAX_EVENTS = 100
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

const estimateTheorySolvedCount = (
  theory: TheoryScoreboardItemModel | undefined,
  questionCount: number
) => {
  if (!theory?.submittedAt || questionCount <= 0) return 0
  if (theory.maxScore <= 0) return 1
  return Math.max(0, Math.min(questionCount, Math.round((theory.score / theory.maxScore) * questionCount)))
}

// ─── Main Data Hook ────────────────────────────────────────────────────────────

export const useCTFScreenData = (numId: number) => {
  const [now, setNow] = useState(() => Date.now())
  const [liveEvents, setLiveEvents] = useState<GameEvent[]>([])
  const [liveSubmissions, setLiveSubmissions] = useState<Submission[]>([])
  const [theoryScoreboard, setTheoryScoreboard] = useState<TheoryScoreboardItemModel[]>([])
  const [theoryPaper, setTheoryPaper] = useState<TheoryPaperDetailModel | null>(null)
  const [awdpServices, setAwdpServices] = useState<AwdpServiceViewModel[]>([])
  const [awdpScoreboard, setAwdpScoreboard] = useState<AwdpScoreboardItem[]>([])
  const [awdpAttackLogs, setAwdpAttackLogs] = useState<AwdpAttackLogItem[]>([])
  const [prevRankMap, setPrevRankMap] = useState(new Map<number, number>())
  const scoreboardSnapshotRef = useRef(new Map<number, { rank: number; score: number }>())

  const challengeCategoryLabelMap = useChallengeCategoryLabelMap()

  const { game } = useAdminGame(numId)
  const isTestMode = game?.isTest ?? false
  const statusInfo = getGameStatus(game)
  const canLoadScoreboard = numId > 0 && !!game && !isTestMode && statusInfo.status !== GameStatus.Coming
  const canLoadMonitor = numId > 0 && !!game && !isTestMode && statusInfo.status !== GameStatus.Coming
  const canLoadParticipations = numId > 0 && !!game && !isTestMode
  const isTheoryScoreGame = game?.gameType === GameType.Theory || game?.gameType === GameType.Mixed
  const isAwdpScoreGame = game?.gameType === GameType.AWDP || game?.gameType === GameType.Mixed
  const canLoadTheoryScoreboard = canLoadScoreboard && isTheoryScoreGame
  const canLoadTheoryMeta = numId > 0 && !!game && !isTestMode && isTheoryScoreGame
  const canLoadAwdpMeta = numId > 0 && !!game && !isTestMode && isAwdpScoreGame
  const canLoadAwdpScoreboard = canLoadScoreboard && isAwdpScoreGame

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
  const acceptedParticipations = useMemo(
    () => (participations ?? []).filter((item) => item.status === ParticipationStatus.Accepted),
    [participations]
  )
  const theoryScoreMap = useMemo(
    () => new Map(theoryScoreboard.map((item) => [item.teamId, item])),
    [theoryScoreboard]
  )
  const awdpScoreMap = useMemo(
    () => new Map(awdpScoreboard.map((item) => [item.teamId, item])),
    [awdpScoreboard]
  )
  const awdpAttackCountByTeam = useMemo(() => {
    const map = new Map<string, number>()
    awdpAttackLogs.forEach((item) => {
      if (!item.attackerTeam) return
      map.set(item.attackerTeam, (map.get(item.attackerTeam) ?? 0) + 1)
    })
    return map
  }, [awdpAttackLogs])
  const awdpLastTimeByTeam = useMemo(() => {
    const map = new Map<string, number>()
    awdpAttackLogs.forEach((item) => {
      if (!item.attackerTeam || !item.time) return
      map.set(item.attackerTeam, Math.max(map.get(item.attackerTeam) ?? 0, item.time))
    })
    return map
  }, [awdpAttackLogs])
  const theoryQuestionCount = theoryPaper?.questions?.length ?? 0
  const challengeCategoryMap = useMemo(() => {
    const map = new Map<number, string>()
    Object.entries(scoreboard?.challenges ?? {}).forEach(([categoryKey, challengeList]) => {
      const categoryName = challengeCategoryLabelMap.get(categoryKey as never)?.desrc ?? categoryKey
      challengeList.forEach((challenge) => {
        map.set(challenge.id, categoryName)
      })
    })
    return map
  }, [challengeCategoryLabelMap, scoreboard?.challenges])
  const buildTeamBreakdown = useCallback(
    (
      base: ScoreboardItem | undefined,
      theory: TheoryScoreboardItemModel | undefined,
      awdp: AwdpScoreboardItem | undefined,
      awdpAttackCount = 0,
      theorySolvedCount = 0
    ): TeamCategoryBreakdown[] => {
      const categoryMap = new Map<string, TeamCategoryBreakdown>()
      const addCategory = (category: string, score: number, solved = 1) => {
        if (score === 0 && solved <= 0) return
        const current = categoryMap.get(category) ?? { category, solved: 0, score: 0 }
        current.solved += solved
        current.score += score
        categoryMap.set(category, current)
      }

      base?.solvedChallenges?.forEach((challenge) => {
        addCategory(challengeCategoryMap.get(challenge.id) ?? 'Other', challenge.score, 1)
      })

      if (awdp) {
        addCategory('AWDP Attack', awdp.attackScore, awdpAttackCount)
        addCategory('AWDP SLA', awdp.slaScore, 0)
        addCategory('AWDP Patch', awdp.patchScore, 0)
        if (awdp.penaltyScore > 0) {
          addCategory('AWDP Penalty', -awdp.penaltyScore, 0)
        }
      } else if ((base?.awdScore ?? 0) > 0) {
        addCategory('AWDP', base?.awdScore ?? 0, awdpAttackCount)
      }

      if ((theory?.score ?? 0) > 0) {
        addCategory('Theory', theory?.score ?? 0, theorySolvedCount)
      }

      return [...categoryMap.values()].sort((left, right) => {
        if (right.score !== left.score) return right.score - left.score
        return left.category.localeCompare(right.category)
      })
    },
    [challengeCategoryMap]
  )
  const scoreItems = useMemo<ScreenScoreItem[]>(() => {
    const baseItems = scoreboard?.items ?? []
    const baseByTeam = new Map(baseItems.map((item) => [item.id, item]))
    const participationByTeam = new Map(
      acceptedParticipations
        .filter((item) => typeof item.team?.id === 'number')
        .map((item) => [item.team.id!, item])
    )
    const teamIds = new Set<number>([
      ...baseByTeam.keys(),
      ...theoryScoreMap.keys(),
      ...awdpScoreMap.keys(),
      ...participationByTeam.keys(),
    ])

    const merged = [...teamIds].map((teamId) => {
      const base = baseByTeam.get(teamId)
      const theory = theoryScoreMap.get(teamId)
      const awdp = awdpScoreMap.get(teamId)
      const participation = participationByTeam.get(teamId)
      const commonScore = game?.gameType === GameType.Theory ? 0 : base?.score ?? 0
      const theoryScore = theory?.score ?? 0
      const awdpAttackCount = awdpAttackCountByTeam.get(base?.name ?? awdp?.teamName ?? theory?.teamName ?? '') ?? 0
      const theorySolvedCount = estimateTheorySolvedCount(theory, theoryQuestionCount)
      const theorySubmittedAt = theory?.submittedAt ?? 0
      const commonSubmittedAt = base?.lastSubmissionTime ?? 0
      const awdpSubmittedAt = awdpLastTimeByTeam.get(base?.name ?? awdp?.teamName ?? theory?.teamName ?? '') ?? 0

      return {
        id: teamId,
        name: base?.name ?? awdp?.teamName ?? theory?.teamName ?? participation?.team?.name ?? `Team ${teamId}`,
        divisionId: base?.divisionId ?? theory?.divisionId ?? participation?.divisionId,
        score: commonScore + theoryScore,
        rank: 0,
        solvedCount:
          (game?.gameType === GameType.Theory ? 0 : base?.solvedCount ?? 0) +
          (awdp ? awdpAttackCount : 0) +
          theorySolvedCount,
        lastSubmissionTime: Math.max(commonSubmittedAt, theorySubmittedAt, awdpSubmittedAt),
        breakdown: buildTeamBreakdown(
          game?.gameType === GameType.Theory ? undefined : base,
          theory,
          awdp,
          awdpAttackCount,
          theorySolvedCount
        ),
      }
    })

    merged.sort((left, right) => {
      if (right.score !== left.score) return right.score - left.score
      const leftTime = left.lastSubmissionTime || Number.MAX_SAFE_INTEGER
      const rightTime = right.lastSubmissionTime || Number.MAX_SAFE_INTEGER
      if (leftTime !== rightTime) return leftTime - rightTime
      return left.id - right.id
    })

    return merged.map((item, index) => ({ ...item, rank: index + 1 }))
  }, [
    acceptedParticipations,
    awdpAttackCountByTeam,
    awdpLastTimeByTeam,
    awdpScoreMap,
    buildTeamBreakdown,
    game?.gameType,
    scoreboard?.items,
    theoryQuestionCount,
    theoryScoreMap,
  ])

  const loadTheoryScoreboard = useCallback(async () => {
    if (!canLoadTheoryScoreboard) {
      setTheoryScoreboard([])
      return
    }

    try {
      const response = await theoryPlayerApi.getScoreboard(numId)
      setTheoryScoreboard(response.data ?? [])
    } catch {
      setTheoryScoreboard([])
    }
  }, [canLoadTheoryScoreboard, numId])

  const loadTheoryMeta = useCallback(async () => {
    if (!canLoadTheoryMeta) {
      setTheoryPaper(null)
      return
    }

    try {
      const response = await theoryAdminApi.getPaper(numId)
      setTheoryPaper(response.data ?? null)
    } catch {
      setTheoryPaper(null)
    }
  }, [canLoadTheoryMeta, numId])

  const loadAwdpData = useCallback(async () => {
    if (!canLoadAwdpMeta) {
      setAwdpServices([])
      setAwdpScoreboard([])
      setAwdpAttackLogs([])
      return
    }

    const [servicesResult, scoreboardResult, attackLogsResult] = await Promise.allSettled([
      awdpAdminApi.getServices(numId),
      canLoadAwdpScoreboard ? awdpAdminApi.getScoreboard(numId) : Promise.resolve({ data: [] as AwdpScoreboardItem[] }),
      canLoadAwdpScoreboard ? awdpAdminApi.getAttackLogs(numId, MAX_EVENTS, 0) : Promise.resolve({ data: { data: [] as AwdpAttackLogItem[] } }),
    ])

    setAwdpServices(servicesResult.status === 'fulfilled' ? servicesResult.value.data ?? [] : [])
    setAwdpScoreboard(scoreboardResult.status === 'fulfilled' ? scoreboardResult.value.data ?? [] : [])
    setAwdpAttackLogs(
      attackLogsResult.status === 'fulfilled' ? attackLogsResult.value.data?.data ?? [] : []
    )
  }, [canLoadAwdpMeta, canLoadAwdpScoreboard, numId])

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

  useEffect(() => {
    void loadTheoryScoreboard()

    if (!canLoadTheoryScoreboard || statusInfo.status !== GameStatus.OnGoing) return undefined

    const timer = window.setInterval(() => {
      void loadTheoryScoreboard()
    }, 30000)

    return () => window.clearInterval(timer)
  }, [canLoadTheoryScoreboard, loadTheoryScoreboard, statusInfo.status])

  useEffect(() => {
    void loadTheoryMeta()
  }, [loadTheoryMeta])

  useEffect(() => {
    void loadAwdpData()

    if (!canLoadAwdpMeta || statusInfo.status !== GameStatus.OnGoing) return undefined

    const timer = window.setInterval(() => {
      void loadAwdpData()
    }, 30000)

    return () => window.clearInterval(timer)
  }, [canLoadAwdpMeta, loadAwdpData, statusInfo.status])

  // Track rank changes
  useEffect(() => {
    if (scoreItems.length === 0) return

    const nextPrevRank = new Map<number, number>()
    for (const item of scoreItems) {
      const previous = scoreboardSnapshotRef.current.get(item.id)
      nextPrevRank.set(item.id, previous ? previous.rank : item.rank)
    }

    scoreboardSnapshotRef.current = new Map(
      scoreItems.map((item) => [item.id, { rank: item.rank, score: item.score }])
    )
    setPrevRankMap(nextPrevRank)
  }, [scoreItems, scoreboard?.updateTimeUtc])

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
      if (
        event.type === EventType.AwdpFlagSubmit ||
        event.type === EventType.AwdpPatchResult ||
        event.type === EventType.AwdpServiceUp ||
        event.type === EventType.AwdpServiceDown
      ) {
        void mutateScoreboard()
        void loadAwdpData()
      }
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
  }, [isTestMode, loadAwdpData, mutateScoreboard, numId, statusInfo.status])

  // ─── Derived Data (matching Ctfscreen format) ────────────────────────────────

  // Teams for leaderboard
  const teams: Team[] = useMemo(() => {
    return scoreItems
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
        breakdown: item.breakdown,
      }))
  }, [scoreItems, prevRankMap, now])

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
      const topTeams = [...scoreItems]
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
  }, [scoreboard?.timelines, scoreItems, submissionFeed])

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

    if (isAwdpScoreGame && awdpServices.length > 0) {
      const awdpAttackCount = awdpAttackLogs.length
      categoryMap.set('AWDP', {
        total: awdpServices.length,
        solved: awdpAttackCount,
      })
    }

    if (isTheoryScoreGame && theoryQuestionCount > 0) {
      const solved = theoryScoreboard.reduce(
        (sum, item) => sum + estimateTheorySolvedCount(item, theoryQuestionCount),
        0
      )
      categoryMap.set('Theory', {
        total: theoryQuestionCount,
        solved: Math.min(theoryQuestionCount, solved),
      })
    }

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
  }, [
    awdpAttackLogs.length,
    awdpServices.length,
    challengeCategoryLabelMap,
    isAwdpScoreGame,
    isTheoryScoreGame,
    scoreboard?.challenges,
    theoryQuestionCount,
    theoryScoreboard,
  ])

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

    const ctfEvents = submissionFeed
      .filter((s): s is typeof s & { time: number } => s.status === AnswerResult.Accepted && !!s.time)
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
          sortTime: submission.time,
          isFirst: isFirstBlood,
        }
      })

    const awdpEvents = awdpAttackLogs.map((log, index) => {
      const teamIndex = teams.findIndex(t => t.name === log.attackerTeam) ?? index
      return {
        id: `awdp-${log.time}-${log.attackerTeam}-${log.victimTeam}-${log.serviceName}`,
        team: log.attackerTeam || 'Unknown',
        teamColor: TEAM_COLORS[teamIndex % TEAM_COLORS.length],
        challenge: `${log.serviceName}${log.victimTeam ? ` -> ${log.victimTeam}` : ''}`,
        category: 'AWDP',
        points: log.points ?? 0,
        time: formatTimeAgo(log.time, now),
        sortTime: log.time,
        isFirst: false,
      }
    })

    return [...ctfEvents, ...awdpEvents]
      .sort((left, right) => right.sortTime - left.sortTime)
      .slice(0, 12)
      .map(({ sortTime, ...event }) => event)
  }, [awdpAttackLogs, submissionFeed, scoreboard?.challenges, teams, challengeCategoryLabelMap, now])

  // Heatmap data
  const heatmapData: HeatmapData[] = useMemo(() => {
    return generateHeatmapData(submissionFeed, now)
  }, [submissionFeed, now])

  // Stats
  const totalTeams = useMemo(() => acceptedParticipations.length, [acceptedParticipations])
  const totalSolves = useMemo(
    () => scoreItems.reduce((sum, item) => sum + item.solvedCount, 0),
    [scoreItems]
  )
  const totalChallenges = useMemo(() => {
    const ctfCount = game?.gameType === GameType.Theory ? 0 : scoreboard?.challengeCount ?? 0
    const awdpCount = isAwdpScoreGame ? awdpServices.length : 0
    const theoryCount = isTheoryScoreGame ? theoryQuestionCount : 0
    return ctfCount + awdpCount + theoryCount
  }, [
    awdpServices.length,
    game?.gameType,
    isAwdpScoreGame,
    isTheoryScoreGame,
    scoreboard?.challengeCount,
    theoryQuestionCount,
  ])
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
    if (scoreItems.length === 0) return 0
    return Math.round(scoreItems.reduce((sum, item) => sum + item.score, 0) / scoreItems.length)
  }, [scoreItems])

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
