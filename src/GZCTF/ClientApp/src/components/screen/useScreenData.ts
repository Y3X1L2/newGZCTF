import * as signalR from '@microsoft/signalr'
import dayjs from 'dayjs'
import type { EChartsOption } from 'echarts'
import { useEffect, useMemo, useRef, useState } from 'react'
import { GameStatus } from '@Components/GameCard'
import { useChallengeCategoryLabelMap } from '@Utils/Shared'
import { OnceSWRConfig } from '@Hooks/useConfig'
import { getGameStatus, useAdminGame } from '@Hooks/useGame'
import api, { AnswerResult, EventType, GameEvent, ParticipationStatus, ScoreboardItem, Submission } from '@Api'

export type ScreenTone = 'accent' | 'success' | 'warning' | 'neutral'
export type ScreenDisplayMode = 'main' | 'charts' | 'logs'

export interface ScreenMoment {
  id: string
  time: number
  tag: string
  title: string
  detail: string
  tone: ScreenTone
}

export interface ScreenCategoryProgress {
  key: string
  name: string
  total: number
  cracked: number
  attempts: number
  percent: number
}

export interface ScreenDynamicEntry {
  id: string
  time: number | undefined
  team: string
  challenge: string
  status: string
  tone: ScreenTone
}

export interface ScreenModeMeta {
  mode: ScreenDisplayMode
  title: string
  subtitle: string
  description: string
}

const MAX_EVENTS = 18
const MAX_SUBMISSIONS = 60
const FALLBACK_DIMENSIONS = ['Web', 'Pwn', 'Crypto', 'Reverse', 'Misc']
const RECENT_ACTIVITY_WINDOW_MS = 10 * 60 * 1000
const CHART_FONT_FAMILY = 'Fira Code, JetBrains Mono, SFMono-Regular, Consolas, monospace'

export const SCREEN_MODE_META: ScreenModeMeta[] = [
  {
    mode: 'main',
    title: '主屏总览',
    subtitle: '赛事总览、排行榜和实时态势',
    description: '适合主显示器，聚合赛事核心信息与实时动态。',
  },
  {
    mode: 'charts',
    title: '图表副屏',
    subtitle: '图表与趋势分析副屏',
    description: '适合副显示器，突出雷达图、趋势图与分类进度。',
  },
  {
    mode: 'logs',
    title: '日志副屏',
    subtitle: '实时提交与事件日志副屏',
    description: '适合副显示器，持续展示实时日志与命中情况。',
  },
]

export const isScreenDisplayMode = (value?: string | null): value is ScreenDisplayMode =>
  value === 'main' || value === 'charts' || value === 'logs'

export const getScreenDisplayPath = (gameId: number, mode: ScreenDisplayMode) =>
  mode === 'main' ? `/admin/games/${gameId}/screen` : `/admin/games/${gameId}/screen/${mode}`

const trimList = <T>(items: T[], limit: number) => items.slice(0, limit)
const clampPercent = (value: number) => Math.max(0, Math.min(100, value))

export const formatDuration = (from?: number, to?: number) => {
  if (!from || !to) return '--:--:--'

  const diff = Math.max(0, to - from)
  const totalSeconds = Math.floor(diff / 1000)
  const hours = Math.floor(totalSeconds / 3600)
  const minutes = Math.floor((totalSeconds % 3600) / 60)
  const seconds = totalSeconds % 60

  return `${hours.toString().padStart(2, '0')}:${minutes.toString().padStart(2, '0')}:${seconds
    .toString()
    .padStart(2, '0')}`
}

export const formatAnswer = (result?: AnswerResult) => {
  switch (result) {
    case AnswerResult.Accepted:
      return '攻击成功'
    case AnswerResult.WrongAnswer:
      return '攻击失败'
    case AnswerResult.CheatDetected:
      return '疑似异常'
    case AnswerResult.FlagSubmitted:
      return '已提交'
    case AnswerResult.NotFound:
      return '未命中'
    default:
      return '待判定'
  }
}

export const toneFromResult = (result?: AnswerResult): ScreenTone => {
  if (result === AnswerResult.Accepted) return 'success'
  if (result === AnswerResult.CheatDetected) return 'warning'
  if (result === AnswerResult.WrongAnswer) return 'neutral'
  return 'accent'
}

const toMoment = (event: GameEvent): ScreenMoment | null => {
  if (event.type === EventType.ContainerStart || event.type === EventType.ContainerDestroy) return null

  if (event.type === EventType.FlagSubmit) {
    const result = event.values.at(0) as AnswerResult | undefined
    const challenge = event.values.at(2) ?? '未知题目'
    const team = event.team ?? '未知战队'
    return {
      id: `flag-${event.time}-${team}-${challenge}`,
      time: event.time,
      tag: result === AnswerResult.Accepted ? '攻破' : '提交',
      title: challenge,
      detail: `${team} ${formatAnswer(result)}`,
      tone: toneFromResult(result),
    }
  }

  if (event.type === EventType.CheatDetected) {
    const challenge = event.values.at(0) ?? '未知题目'
    const team = event.values.at(1) ?? event.team ?? '未知战队'
    const source = event.values.at(2) ?? '系统规则'
    return {
      id: `cheat-${event.time}-${team}-${challenge}`,
      time: event.time,
      tag: '告警',
      title: challenge,
      detail: `${team} 触发异常检测，来源 ${source}`,
      tone: 'warning',
    }
  }

  return {
    id: `event-${event.time}-${event.type}-${event.team ?? event.user ?? 'system'}`,
    time: event.time,
    tag: '动态',
    title: event.values.at(-1) ?? '实时事件',
    detail: `${event.team ?? event.user ?? '系统'} 推送了一条动态`,
    tone: 'accent',
  }
}

export const useRotatingWindow = <T>(items: T[], visibleCount: number, clock: number, intervalMs = 9000) =>
  useMemo(() => {
    if (visibleCount <= 0) return []
    if (items.length <= visibleCount) return items

    const pageCount = Math.ceil(items.length / visibleCount)
    const pageIndex = Math.floor(clock / intervalMs) % pageCount
    const start = pageIndex * visibleCount

    return items.slice(start, start + visibleCount)
  }, [clock, intervalMs, items, visibleCount])

export const useVisibleCount = (
  height: number,
  rowHeight: number,
  options?: {
    min?: number
    reserved?: number
    max?: number
  }
) =>
  useMemo(() => {
    const min = options?.min ?? 1
    const reserved = options?.reserved ?? 0
    const max = options?.max
    const available = Math.max(0, height - reserved)
    const count = Math.max(min, Math.floor(available / rowHeight))
    return max ? Math.min(max, count) : count
  }, [height, options?.max, options?.min, options?.reserved, rowHeight])

export const useGameScreenData = (numId: number) => {
  const [now, setNow] = useState(() => Date.now())
  const [liveEvents, setLiveEvents] = useState<GameEvent[]>([])
  const [liveSubmissions, setLiveSubmissions] = useState<Submission[]>([])
  const [rankDeltaMap, setRankDeltaMap] = useState(new Map<number, number>())
  const [scoreDeltaMap, setScoreDeltaMap] = useState(new Map<number, number>())

  const challengeCategoryLabelMap = useChallengeCategoryLabelMap()
  const scoreboardSnapshotRef = useRef(new Map<number, { rank: number; score: number }>())
  const scoreboardRefreshRef = useRef(0)

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

  useEffect(() => {
    const timer = window.setInterval(() => setNow(Date.now()), 1000)
    return () => window.clearInterval(timer)
  }, [])

  useEffect(() => {
    scoreboardSnapshotRef.current = new Map()
    setRankDeltaMap(new Map())
    setScoreDeltaMap(new Map())
  }, [isTestMode, numId])

  useEffect(() => {
    if (isTestMode) return
    if (initialEvents) setLiveEvents(trimList(initialEvents, MAX_EVENTS))
  }, [initialEvents, isTestMode])

  useEffect(() => {
    if (isTestMode) return
    if (initialSubmissions) setLiveSubmissions(trimList(initialSubmissions, MAX_SUBMISSIONS))
  }, [initialSubmissions, isTestMode])

  useEffect(() => {
    if (!scoreboard?.items) return

    const nextRankDelta = new Map<number, number>()
    const nextScoreDelta = new Map<number, number>()

    for (const item of scoreboard.items) {
      const previous = scoreboardSnapshotRef.current.get(item.id)
      nextRankDelta.set(item.id, previous ? previous.rank - item.rank : 0)
      nextScoreDelta.set(item.id, previous ? item.score - previous.score : 0)
    }

    scoreboardSnapshotRef.current = new Map(
      scoreboard.items.map((item) => [item.id, { rank: item.rank, score: item.score }])
    )
    setRankDeltaMap(nextRankDelta)
    setScoreDeltaMap(nextScoreDelta)
  }, [scoreboard?.items, scoreboard?.updateTimeUtc])

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
      setLiveEvents((current) => trimList([event, ...current], MAX_EVENTS))
    })

    connection.on('ReceivedSubmissions', (submission: Submission) => {
      setLiveSubmissions((current) => trimList([submission, ...current], MAX_SUBMISSIONS))

      if (submission.status === AnswerResult.Accepted) {
        const currentTime = Date.now()
        if (currentTime - scoreboardRefreshRef.current > 10000) {
          scoreboardRefreshRef.current = currentTime
          void mutateScoreboard()
        }
      }
    })

    void connection.start().catch(() => undefined)
    return () => {
      void connection.stop()
    }
  }, [isTestMode, mutateScoreboard, numId, statusInfo.status])

  const acceptedParticipations = useMemo(
    () => (participations ?? []).filter((item) => item.status === ParticipationStatus.Accepted),
    [participations]
  )
  const activeTeamCount = scoreboard?.items?.length ?? acceptedParticipations.length
  const playerCount = useMemo(
    () => acceptedParticipations.reduce((count, item) => count + (item.registeredMembers?.length ?? 0), 0),
    [acceptedParticipations]
  )
  const totalSolveCount = useMemo(
    () => (scoreboard?.items ?? []).reduce((count, item) => count + item.solvedCount, 0),
    [scoreboard?.items]
  )
  const rankedTeams = useMemo(
    () => [...(scoreboard?.items ?? [])].sort((left, right) => left.rank - right.rank),
    [scoreboard?.items]
  )
  const topTeam = rankedTeams[0]
  const runnerUp = rankedTeams[1]
  const leaderGap = Math.max(0, (topTeam?.score ?? 0) - (runnerUp?.score ?? 0))
  const challengeList = useMemo(
    () =>
      Object.values(scoreboard?.challenges ?? {})
        .flat()
        .sort((left, right) => right.solved - left.solved),
    [scoreboard?.challenges]
  )
  const challengeMetaMap = useMemo(() => new Map(challengeList.map((item) => [item.title, item])), [challengeList])
  const hotChallenge = challengeList[0]

  const categoryHeat = useMemo(() => {
    const map = new Map<string, number>()
    for (const challenge of challengeList) {
      map.set(challenge.category, (map.get(challenge.category) ?? 0) + challenge.solved)
    }

    return [...map.entries()]
      .map(([category, solved]) => ({
        category,
        solved,
        name: challengeCategoryLabelMap.get(category as never)?.desrc ?? category,
      }))
      .sort((left, right) => right.solved - left.solved)
      .slice(0, 6)
  }, [challengeCategoryLabelMap, challengeList])
  const hotCategory = categoryHeat[0]

  const moments = useMemo(
    () =>
      eventFeed
        .map((event) => toMoment(event))
        .filter((item): item is ScreenMoment => !!item)
        .sort((left, right) => right.time - left.time)
        .slice(0, 12),
    [eventFeed]
  )

  const submissionSummary = useMemo(() => {
    const total = submissionFeed.length
    const accepted = submissionFeed.filter((item) => item.status === AnswerResult.Accepted).length
    const solveRate = total > 0 ? (accepted / total) * 100 : 0
    const coverage =
      activeTeamCount > 0 && (scoreboard?.challengeCount ?? 0) > 0
        ? (totalSolveCount / (activeTeamCount * Math.max(scoreboard?.challengeCount ?? 1, 1))) * 100
        : 0

    return {
      accepted,
      total,
      solveRate: clampPercent(Math.round(solveRate)),
      coverage: clampPercent(Math.round(coverage)),
    }
  }, [activeTeamCount, scoreboard?.challengeCount, submissionFeed, totalSolveCount])

  const recentSubmissions = useMemo(
    () => submissionFeed.filter((item) => !!item.time && now - item.time <= RECENT_ACTIVITY_WINDOW_MS),
    [now, submissionFeed]
  )

  const recentAcceptedCount = useMemo(
    () => recentSubmissions.filter((item) => item.status === AnswerResult.Accepted).length,
    [recentSubmissions]
  )

  const recentAlertCount = useMemo(
    () =>
      eventFeed.filter((item) => item.type === EventType.CheatDetected && now - item.time <= RECENT_ACTIVITY_WINDOW_MS)
        .length,
    [eventFeed, now]
  )

  const activityLevel = useMemo(() => {
    const heat = recentSubmissions.length
    if (heat >= 16) return '高压态势'
    if (heat >= 8) return '高速活跃'
    if (heat >= 3) return '持续扫描'
    return '低频巡检'
  }, [recentSubmissions.length])

  const threatLevel = useMemo(() => {
    if (recentAlertCount >= 2) return '告警提升'
    if (recentAlertCount === 1) return '重点监控'
    if (recentAcceptedCount >= 6) return '攻势增强'
    return '链路稳定'
  }, [recentAcceptedCount, recentAlertCount])

  const threatIndex = useMemo(
    () =>
      clampPercent(
        Math.round(Math.min(100, recentSubmissions.length * 6 + recentAcceptedCount * 8 + recentAlertCount * 22))
      ),
    [recentAcceptedCount, recentAlertCount, recentSubmissions.length]
  )

  const categoryProgress = useMemo(() => {
    const map = new Map<string, ScreenCategoryProgress>()

    for (const challenge of challengeList) {
      const current = map.get(challenge.category) ?? {
        key: challenge.category,
        name: challengeCategoryLabelMap.get(challenge.category as never)?.desrc ?? challenge.category,
        total: 0,
        cracked: 0,
        attempts: 0,
        percent: 0,
      }
      current.total += 1
      if (challenge.solved > 0) current.cracked += 1
      map.set(challenge.category, current)
    }

    for (const submission of submissionFeed.slice(0, 40)) {
      const meta = submission.challenge ? challengeMetaMap.get(submission.challenge) : undefined
      if (!meta) continue
      const current = map.get(meta.category)
      if (current) current.attempts += 1
    }

    return [...map.values()]
      .map((item) => ({ ...item, percent: clampPercent(Math.round((item.cracked / Math.max(item.total, 1)) * 100)) }))
      .sort((left, right) => right.percent - left.percent || right.attempts - left.attempts)
      .slice(0, 8)
  }, [challengeCategoryLabelMap, challengeList, challengeMetaMap, submissionFeed])

  const timelineTeams = useMemo(() => {
    const overall = scoreboard?.timelines?.find((item) => item.divisionId === undefined || item.divisionId === 0)?.teams
    return (overall ?? scoreboard?.timelines?.[0]?.teams ?? []).slice(0, 5)
  }, [scoreboard?.timelines])

  const radarMetrics = useMemo(() => {
    const source = categoryHeat.slice(0, 5).map((item) => ({ name: item.name, value: item.solved }))
    const normalized = (source.length > 0 ? source : FALLBACK_DIMENSIONS.map((name) => ({ name, value: 0 }))).slice(
      0,
      5
    )
    while (normalized.length < 5) normalized.push({ name: `维度${normalized.length + 1}`, value: 0 })
    const maxValue = Math.max(1, ...normalized.map((item) => item.value))
    return normalized.map((item) => ({ ...item, max: Math.max(1, maxValue, item.value) }))
  }, [categoryHeat])

  const radarSeriesData = useMemo(
    () => [{ name: game?.title ?? 'GZCTF', value: radarMetrics.map((item) => item.value) }],
    [game?.title, radarMetrics]
  )

  const liveDynamics = useMemo<ScreenDynamicEntry[]>(
    () =>
      submissionFeed.map((submission, index) => ({
        id: `${submission.time}-${submission.team ?? submission.user ?? index}-${submission.challenge ?? index}`,
        time: submission.time,
        team: submission.team ?? submission.user ?? '未知战队',
        challenge: submission.challenge ?? '未知题目',
        status: formatAnswer(submission.status),
        tone: toneFromResult(submission.status),
      })),
    [submissionFeed]
  )

  const roundInfo = useMemo(() => {
    const total = 8
    if (!game?.start || !game?.end) return { current: 0, total }
    if (statusInfo.status === GameStatus.Coming) return { current: 0, total }
    if (statusInfo.status === GameStatus.Ended) return { current: total, total }
    const duration = Math.max(game.end - game.start, 1)
    const progress = clampPercent(Math.round(((now - game.start) / duration) * 100))
    return { current: Math.max(1, Math.ceil((progress / 100) * total)), total }
  }, [game?.end, game?.start, now, statusInfo.status])

  const missionProgress = useMemo(() => {
    if (!game?.start || !game?.end) return 0
    if (statusInfo.status === GameStatus.Coming) return 0
    if (statusInfo.status === GameStatus.Ended) return 100
    return clampPercent(Math.round(((now - game.start) / Math.max(game.end - game.start, 1)) * 100))
  }, [game?.end, game?.start, now, statusInfo.status])

  const scorePulse = useMemo(
    () =>
      Array.from(scoreDeltaMap.values())
        .filter((value) => value > 0)
        .reduce((sum, value) => sum + value, 0),
    [scoreDeltaMap]
  )

  const phaseLabel =
    statusInfo.status === GameStatus.Coming ? '未开始' : statusInfo.status === GameStatus.Ended ? '已结束' : '进行中'
  const countdownLabel = statusInfo.status === GameStatus.Coming ? '开赛倒计时' : '比赛倒计时'
  const countdownValue =
    statusInfo.status === GameStatus.Coming
      ? formatDuration(now, game?.start)
      : statusInfo.status === GameStatus.Ended
        ? '00:00:00'
        : formatDuration(now, game?.end)

  const radarOption = useMemo<EChartsOption>(
    () => ({
      backgroundColor: 'transparent',
      animationDuration: 900,
      textStyle: {
        fontFamily: CHART_FONT_FAMILY,
      },
      radar: {
        center: ['50%', '52%'],
        radius: '74%',
        splitNumber: 5,
        axisName: {
          color: '#d4f7ff',
          fontSize: 12,
          fontWeight: 700,
        },
        splitArea: {
          areaStyle: {
            color: [
              'rgba(22, 71, 150, 0.06)',
              'rgba(18, 42, 101, 0.04)',
              'rgba(22, 71, 150, 0.05)',
              'rgba(18, 42, 101, 0.03)',
              'rgba(22, 71, 150, 0.05)',
            ],
          },
        },
        axisLine: { lineStyle: { color: 'rgba(123, 215, 255, 0.24)' } },
        splitLine: { lineStyle: { color: 'rgba(123, 215, 255, 0.2)' } },
        indicator: radarMetrics.map((item) => ({ name: item.name, max: item.max })),
      },
      series: [
        {
          type: 'radar',
          symbol: 'circle',
          symbolSize: 8,
          data: radarSeriesData.map((item) => ({
            name: item.name,
            value: item.value,
            areaStyle: { color: 'rgba(74, 224, 255, 0.24)' },
            lineStyle: {
              color: '#7eeaff',
              width: 2.4,
              shadowBlur: 12,
              shadowColor: 'rgba(74, 224, 255, 0.35)',
            },
            itemStyle: { color: '#f1fbff', borderColor: '#7eeaff', borderWidth: 2 },
          })),
        },
      ],
    }),
    [radarMetrics, radarSeriesData]
  )

  const trendOption = useMemo<EChartsOption>(() => {
    if (timelineTeams.length > 0) {
      return {
        backgroundColor: 'transparent',
        animationDuration: 900,
        textStyle: {
          fontFamily: CHART_FONT_FAMILY,
        },
        tooltip: {
          trigger: 'axis',
          backgroundColor: 'rgba(6, 16, 37, 0.92)',
          borderColor: 'rgba(104, 222, 255, 0.28)',
          textStyle: { color: '#e6f8ff' },
        },
        legend: {
          top: 6,
          right: 12,
          textStyle: { color: '#d8f3ff', fontSize: 11 },
          itemWidth: 12,
          itemHeight: 8,
        },
        grid: { left: 46, right: 16, top: 42, bottom: 24 },
        xAxis: {
          type: 'time',
          axisLabel: { color: '#7fb7de' },
          axisLine: { lineStyle: { color: 'rgba(104, 166, 210, 0.24)' } },
          splitLine: { show: false },
        },
        yAxis: {
          type: 'value',
          axisLabel: { color: '#7fb7de' },
          axisLine: { show: false },
          splitLine: { lineStyle: { color: 'rgba(104, 166, 210, 0.14)' } },
        },
        color: ['#73f0ff', '#f8d76b', '#65a7ff', '#b088ff', '#ff8ea1'],
        series: timelineTeams.map((team, index) => ({
          type: 'line',
          name: team.name,
          smooth: true,
          showSymbol: false,
          lineStyle: {
            width: index === 0 ? 2.8 : 1.7,
            shadowBlur: index === 0 ? 10 : 0,
            shadowColor: index === 0 ? 'rgba(115, 240, 255, 0.22)' : 'transparent',
          },
          areaStyle:
            index === 0
              ? {
                  color: {
                    type: 'linear',
                    x: 0,
                    y: 0,
                    x2: 0,
                    y2: 1,
                    colorStops: [
                      { offset: 0, color: 'rgba(115, 240, 255, 0.20)' },
                      { offset: 1, color: 'rgba(115, 240, 255, 0.02)' },
                    ],
                  },
                }
              : undefined,
          data: team.items.map((item) => [item.time, item.score]),
        })),
      }
    }

    return {
      backgroundColor: 'transparent',
      animationDuration: 900,
      textStyle: {
        fontFamily: CHART_FONT_FAMILY,
      },
      tooltip: {
        trigger: 'axis',
        backgroundColor: 'rgba(6, 16, 37, 0.92)',
        borderColor: 'rgba(104, 222, 255, 0.28)',
        textStyle: { color: '#e6f8ff' },
      },
      grid: { left: 46, right: 16, top: 24, bottom: 24 },
      xAxis: {
        type: 'category',
        axisLabel: { color: '#7fb7de' },
        axisLine: { lineStyle: { color: 'rgba(104, 166, 210, 0.24)' } },
        data: submissionFeed
          .slice(0, 8)
          .reverse()
          .map((item) => dayjs(item.time).format('HH:mm')),
      },
      yAxis: {
        type: 'value',
        axisLabel: { color: '#7fb7de' },
        splitLine: { lineStyle: { color: 'rgba(104, 166, 210, 0.14)' } },
      },
      series: [
        {
          type: 'line',
          smooth: true,
          symbolSize: 8,
          data: submissionFeed
            .slice(0, 8)
            .reverse()
            .map((item) => (item.status === AnswerResult.Accepted ? 1 : 0)),
          lineStyle: { color: '#73f0ff', width: 2.4 },
          areaStyle: {
            color: {
              type: 'linear',
              x: 0,
              y: 0,
              x2: 0,
              y2: 1,
              colorStops: [
                { offset: 0, color: 'rgba(115, 240, 255, 0.24)' },
                { offset: 1, color: 'rgba(115, 240, 255, 0.03)' },
              ],
            },
          },
          itemStyle: { color: '#d4fcff' },
        },
      ],
    }
  }, [submissionFeed, timelineTeams])

  const rankingOption = useMemo<EChartsOption>(() => {
    const rankingItems = rankedTeams.slice(0, 8)

    return {
      backgroundColor: 'transparent',
      animationDuration: 900,
      textStyle: {
        fontFamily: CHART_FONT_FAMILY,
      },
      tooltip: {
        trigger: 'axis',
        axisPointer: { type: 'shadow' },
        backgroundColor: 'rgba(6, 16, 37, 0.92)',
        borderColor: 'rgba(104, 222, 255, 0.28)',
        textStyle: { color: '#e6f8ff' },
      },
      grid: { left: 18, right: 30, top: 18, bottom: 10, containLabel: true },
      xAxis: {
        type: 'value',
        axisLabel: { color: '#7fb7de' },
        splitLine: { lineStyle: { color: 'rgba(104, 166, 210, 0.12)' } },
      },
      yAxis: {
        type: 'category',
        inverse: false,
        axisTick: { show: false },
        axisLine: { show: false },
        axisLabel: {
          color: '#d8f3ff',
          fontSize: 11,
          overflow: 'truncate',
          width: 110,
        },
        data: rankingItems.map((item) => item.name).reverse(),
      },
      series: [
        {
          type: 'bar',
          barWidth: '42%',
          showBackground: true,
          backgroundStyle: { color: 'rgba(255,255,255,0.04)' },
          label: {
            show: true,
            position: 'right',
            color: '#eefbff',
            formatter: (params: any) => `${params.value}`,
          },
          itemStyle: {
            borderRadius: [0, 6, 6, 0],
            color: {
              type: 'linear',
              x: 0,
              y: 0,
              x2: 1,
              y2: 0,
              colorStops: [
                { offset: 0, color: '#2f7bff' },
                { offset: 0.55, color: '#47dfff' },
                { offset: 1, color: '#7effb2' },
              ],
            },
          },
          data: rankingItems.map((item) => item.score).reverse(),
        },
      ],
    }
  }, [rankedTeams])

  const progressChartOption = useMemo<EChartsOption>(() => {
    const progressItems = categoryProgress.slice(0, 6)

    return {
      backgroundColor: 'transparent',
      animationDuration: 900,
      textStyle: {
        fontFamily: CHART_FONT_FAMILY,
      },
      tooltip: {
        trigger: 'axis',
        axisPointer: { type: 'shadow' },
        backgroundColor: 'rgba(6, 16, 37, 0.92)',
        borderColor: 'rgba(104, 222, 255, 0.28)',
        textStyle: { color: '#e6f8ff' },
        formatter: (params: any) => {
          const item = progressItems.find((entry) => entry.name === params[0]?.name)
          if (!item) return ''
          return `${item.name}<br/>渗透率 ${item.percent}%<br/>已攻破 ${item.cracked}/${item.total}<br/>近期流量 ${item.attempts}`
        },
      },
      grid: { left: 18, right: 26, top: 18, bottom: 10, containLabel: true },
      xAxis: {
        type: 'value',
        max: 100,
        axisLabel: { color: '#7fb7de', formatter: '{value}%' },
        splitLine: { lineStyle: { color: 'rgba(104, 166, 210, 0.12)' } },
      },
      yAxis: {
        type: 'category',
        axisTick: { show: false },
        axisLine: { show: false },
        axisLabel: {
          color: '#d8f3ff',
          fontSize: 11,
          overflow: 'truncate',
          width: 96,
        },
        data: progressItems.map((item) => item.name).reverse(),
      },
      series: [
        {
          type: 'bar',
          barWidth: '42%',
          showBackground: true,
          backgroundStyle: { color: 'rgba(255,255,255,0.04)' },
          label: {
            show: true,
            position: 'right',
            color: '#eefbff',
            formatter: (params: any) => `${params.value}%`,
          },
          itemStyle: {
            borderRadius: [0, 6, 6, 0],
            color: {
              type: 'linear',
              x: 0,
              y: 0,
              x2: 1,
              y2: 0,
              colorStops: [
                { offset: 0, color: '#4d72ff' },
                { offset: 0.5, color: '#4ce2ff' },
                { offset: 1, color: '#86ffcb' },
              ],
            },
          },
          data: progressItems.map((item) => item.percent).reverse(),
        },
      ],
    }
  }, [categoryProgress])

  return {
    game,
    now,
    isTestMode,
    scoreboard,
    phaseLabel,
    countdownLabel,
    countdownValue,
    roundInfo,
    missionProgress,
    playerCount,
    activeTeamCount,
    totalSolveCount,
    rankedTeams,
    topTeam,
    runnerUp,
    leaderGap,
    moments,
    liveDynamics,
    rankDeltaMap,
    scoreDeltaMap,
    scorePulse,
    hotChallenge,
    hotCategory,
    recentAcceptedCount,
    recentAlertCount,
    activityLevel,
    threatLevel,
    threatIndex,
    submissionSummary,
    categoryProgress,
    radarOption,
    trendOption,
    rankingOption,
    progressChartOption,
    scoreboardUpdatedAt: scoreboard?.updateTimeUtc ?? now,
  }
}

export const getRankChange = (item: ScoreboardItem, rankDeltaMap: Map<number, number>) => rankDeltaMap.get(item.id) ?? 0

export const getScoreChange = (item: ScoreboardItem, scoreDeltaMap: Map<number, number>) =>
  scoreDeltaMap.get(item.id) ?? 0
