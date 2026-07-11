import { useMemo, useRef } from 'react'
import {
  type ChallengeInfo,
  ChallengeCategory,
  type GameEvent,
  type GameInfoModel,
  GamePermission,
  type ParticipationInfoModel,
  ParticipationStatus,
  type ScoreboardItem,
  type ScoreboardModel,
  type Submission,
  AnswerResult,
  EventType,
  SubmissionType,
} from '@Api'

const DEMO_TICK_MS = 15000
const DEMO_EVENT_GAP_MS = 45000
const INITIAL_VISIBLE_SUBMISSIONS = 36
const MAX_VISIBLE_SUBMISSIONS = 180

type DemoGame = Pick<GameInfoModel, 'id' | 'title'>

interface DemoDivision {
  id: number
  name: string
}

interface DemoChallenge {
  id: number
  title: string
  category: ChallengeCategory
  score: number
}

interface DemoTeam {
  id: number
  name: string
  divisionId: number
  members: string[]
}

interface ScheduledSubmission {
  teamId: number
  challengeId: number
  user: string
  status: AnswerResult
  answer: string
}

interface TimedSubmission extends ScheduledSubmission {
  time: number
}

interface DemoScenario {
  title: string
  divisions: DemoDivision[]
  teams: DemoTeam[]
  challenges: DemoChallenge[]
  schedule: ScheduledSubmission[]
}

interface DemoScreenData {
  events: GameEvent[]
  participations: ParticipationInfoModel[]
  scoreboard: ScoreboardModel
  submissions: Submission[]
}

const baseTeamNames = [
  '星炬',
  '白泽',
  '雾隐',
  '长锋',
  '矩阵',
  '巡夜人',
  '赤霄',
  '归零',
  '天衡',
  '流明',
  '苍穹',
  '回声',
]

const baseChallengeSpecs: ReadonlyArray<Pick<DemoChallenge, 'title' | 'category' | 'score'>> = [
  { title: 'Gateway', category: ChallengeCategory.Web, score: 540 },
  { title: 'Memory Forge', category: ChallengeCategory.Pwn, score: 620 },
  { title: 'Cipher Rain', category: ChallengeCategory.Crypto, score: 500 },
  { title: 'Mirror Maze', category: ChallengeCategory.Reverse, score: 560 },
  { title: 'Packet Storm', category: ChallengeCategory.Misc, score: 450 },
  { title: 'Trace Hunter', category: ChallengeCategory.Forensics, score: 520 },
  { title: 'Signal Drift', category: ChallengeCategory.Web, score: 480 },
  { title: 'Kernel Fault', category: ChallengeCategory.Pwn, score: 680 },
  { title: 'Ghost Ledger', category: ChallengeCategory.Crypto, score: 610 },
  { title: 'Binary Bloom', category: ChallengeCategory.Reverse, score: 530 },
]

const nonAcceptedStatuses = [
  AnswerResult.WrongAnswer,
  AnswerResult.NotFound,
  AnswerResult.FlagSubmitted,
] as const

const createHash = (value: string) => {
  let hash = 2166136261
  for (let index = 0; index < value.length; index += 1) {
    hash ^= value.charCodeAt(index)
    hash = Math.imul(hash, 16777619)
  }

  return hash >>> 0
}

const createRandom = (seed: number) => {
  let current = seed >>> 0
  return () => {
    current = (current + 0x6d2b79f5) | 0
    let value = Math.imul(current ^ (current >>> 15), 1 | current)
    value ^= value + Math.imul(value ^ (value >>> 7), 61 | value)
    return ((value ^ (value >>> 14)) >>> 0) / 4294967296
  }
}

const randomInt = (random: () => number, max: number) => Math.floor(random() * max)

const pickOne = <T,>(items: readonly T[], random: () => number) => items[randomInt(random, items.length)]

const createMembers = (name: string, count: number) =>
  Array.from({ length: count }, (_, index) => `${name}${['One', 'Two', 'Tri', 'Quad', 'Penta'][index] ?? index + 1}`)

const createScenario = (game: DemoGame): DemoScenario => {
  const seed = createHash(`${game.id ?? 0}-${game.title}`)
  const random = createRandom(seed)

  const divisions: DemoDivision[] = [
    { id: 1, name: '进攻组' },
    { id: 2, name: '协同组' },
  ]

  const teams = baseTeamNames.map((name, index) => {
    const memberCount = 3 + ((index + seed) % 3)
    return {
      id: 1001 + index,
      name: `${name}队`,
      divisionId: divisions[index % divisions.length].id,
      members: createMembers(name, memberCount),
    }
  })

  const rotatedTeams = teams
    .map((team, index) => teams[(index + (seed % teams.length)) % teams.length] ?? team)
    .map((team, index) => ({ ...team, id: 1001 + index }))

  const challenges = baseChallengeSpecs.map((challenge, index) => ({
    id: 2001 + index,
    title: challenge.title,
    category: challenge.category,
    score: challenge.score + ((seed + index * 13) % 4) * 20,
  }))

  const acceptedPairs = new Set<string>()
  const schedule: ScheduledSubmission[] = []

  for (let index = 0; index < MAX_VISIBLE_SUBMISSIONS; index += 1) {
    const team = pickOne(rotatedTeams, random)
    const challenge = pickOne(challenges, random)
    const pairKey = `${team.id}-${challenge.id}`

    let status: AnswerResult
    if (index > 12 && index % 17 === 8) {
      status = AnswerResult.CheatDetected
    } else if (!acceptedPairs.has(pairKey) && (index < 18 || random() > 0.4)) {
      acceptedPairs.add(pairKey)
      status = AnswerResult.Accepted
    } else {
      status = pickOne(nonAcceptedStatuses, random)
    }

    schedule.push({
      teamId: team.id,
      challengeId: challenge.id,
      user: pickOne(team.members, random),
      status,
      answer: status === AnswerResult.Accepted ? `flag{demo-${challenge.id}}` : `demo-${index.toString(16)}`,
    })
  }

  return {
    title: game.title,
    divisions,
    teams: rotatedTeams,
    challenges,
    schedule,
  }
}

const createTimedSubmissions = (scenario: DemoScenario, now: number, visibleCount: number): TimedSubmission[] =>
  scenario.schedule.slice(0, visibleCount).map((submission, index, items) => ({
    ...submission,
    time: now - (items.length - 1 - index) * DEMO_EVENT_GAP_MS,
  }))

const buildParticipations = (scenario: DemoScenario): ParticipationInfoModel[] =>
  scenario.teams.map((team, index) => ({
    id: 3001 + index,
    status: ParticipationStatus.Accepted,
    divisionId: team.divisionId,
    registeredMembers: team.members.map((_, memberIndex) => `demo-member-${team.id}-${memberIndex}`),
    team: {
      id: team.id,
      name: team.name,
      bio: 'Screen demo team',
      avatar: null,
      members: [],
    },
  }))

const buildSubmissions = (scenario: DemoScenario, timedSubmissions: TimedSubmission[]): Submission[] =>
  [...timedSubmissions]
    .reverse()
    .map((submission) => {
      const team = scenario.teams.find((item) => item.id === submission.teamId)
      const challenge = scenario.challenges.find((item) => item.id === submission.challengeId)

      return {
        answer: submission.answer,
        challenge: challenge?.title,
        status: submission.status,
        team: team?.name,
        time: submission.time,
        user: submission.user,
      }
    })

const buildEvents = (scenario: DemoScenario, timedSubmissions: TimedSubmission[]): GameEvent[] =>
  [...timedSubmissions]
    .reverse()
    .map((submission) => {
      const team = scenario.teams.find((item) => item.id === submission.teamId)
      const challenge = scenario.challenges.find((item) => item.id === submission.challengeId)

      if (submission.status === AnswerResult.CheatDetected) {
        return {
          time: submission.time,
          team: team?.name,
          type: EventType.CheatDetected,
          user: submission.user,
          values: [challenge?.title ?? '未知题目', team?.name ?? '未知战队', '演示规则'],
        }
      }

      return {
        time: submission.time,
        team: team?.name,
        type: EventType.FlagSubmit,
        user: submission.user,
        values: [submission.status, submission.answer, challenge?.title ?? '未知题目', `${challenge?.id ?? 0}`],
      }
    })

const buildScoreboard = (scenario: DemoScenario, timedSubmissions: TimedSubmission[], now: number): ScoreboardModel => {
  const divisionRankMap = new Map<number, number>()
  const challengeSolveMap = new Map<number, TimedSubmission[]>()
  const teamEntries = new Map<number, ScoreboardItem>(
    scenario.teams.map((team) => [
      team.id,
      {
        avatar: null,
        bio: 'Screen demo team',
        divisionId: team.divisionId,
        divisionRank: null,
        id: team.id,
        lastSubmissionTime: now - timedSubmissions.length * DEMO_EVENT_GAP_MS,
        name: team.name,
        rank: 0,
        ctfScore: 0,
        awdScore: 0,
        pentestScore: 0,
        score: 0,
        solvedChallenges: [],
        solvedCount: 0,
      },
    ])
  )

  for (const submission of timedSubmissions) {
    if (submission.status !== AnswerResult.Accepted) continue

    const challenge = scenario.challenges.find((item) => item.id === submission.challengeId)
    const team = teamEntries.get(submission.teamId)
    if (!challenge || !team) continue

    const solves = challengeSolveMap.get(challenge.id) ?? []
    const submissionType =
      solves.length === 0
        ? SubmissionType.FirstBlood
        : solves.length === 1
          ? SubmissionType.SecondBlood
          : solves.length === 2
            ? SubmissionType.ThirdBlood
            : SubmissionType.Normal

    solves.push(submission)
    challengeSolveMap.set(challenge.id, solves)

    team.score += challenge.score
    team.solvedCount += 1
    team.lastSubmissionTime = submission.time
    team.solvedChallenges.push({
      id: challenge.id,
      flagId: challenge.id,
      score: challenge.score,
      time: submission.time,
      type: submissionType,
      userName: submission.user,
    })
  }

  const sortedItems = [...teamEntries.values()].sort(
    (left, right) => right.score - left.score || left.lastSubmissionTime - right.lastSubmissionTime || left.id - right.id
  )

  sortedItems.forEach((item, index) => {
    item.rank = index + 1
    const nextDivisionRank = (divisionRankMap.get(item.divisionId ?? 0) ?? 0) + 1
    divisionRankMap.set(item.divisionId ?? 0, nextDivisionRank)
    item.divisionRank = nextDivisionRank
  })

  const challengeGroups = scenario.challenges.reduce<Record<string, ChallengeInfo[]>>((groups, challenge) => {
    const accepted = challengeSolveMap.get(challenge.id) ?? []
    const bloods = accepted.slice(0, 3).map((submission) => {
      const team = scenario.teams.find((item) => item.id === submission.teamId)
      return {
        avatar: null,
        id: submission.teamId,
        name: team?.name ?? '未知战队',
        submitTimeUtc: submission.time,
      }
    })

    const entry: ChallengeInfo = {
      bloods,
      category: challenge.category,
      deadline: null,
      disableBloodBonus: false,
      id: challenge.id,
      score: challenge.score,
      solved: accepted.length,
      title: challenge.title,
      totalFlags: 1,
    }

    groups[challenge.category] ??= []
    groups[challenge.category].push(entry)
    return groups
  }, {})

  const topTimelineTeams = sortedItems.slice(0, 5).map((team) => {
    const teamSolves = team.solvedChallenges
      .slice()
      .sort((left, right) => left.time - right.time)
      .reduce<{ items: { score: number; time: number }[]; score: number }>(
        (state, solve) => {
          state.score += solve.score
          state.items.push({ score: state.score, time: solve.time })
          return state
        },
        { items: [], score: 0 }
      )

    return {
      id: team.id,
      items: teamSolves.items.length > 0 ? teamSolves.items : [{ score: 0, time: now - DEMO_EVENT_GAP_MS }],
      name: team.name,
    }
  })

  return {
    bloodBonus: 10,
    challengeCount: scenario.challenges.length,
    challenges: challengeGroups,
    divisions: scenario.divisions.map((division) => ({
      challengeConfigs: {},
      defaultPermissions: GamePermission.All,
      id: division.id,
      name: division.name,
    })),
    items: sortedItems,
    timelines: [{ divisionId: 0, teams: topTimelineTeams }],
    updateTimeUtc: now,
  }
}

export const useDemoScreenData = (game?: DemoGame, now = Date.now()): DemoScreenData | undefined => {
  const initialPhaseRef = useRef<number | undefined>(undefined)
  const currentGameIdRef = useRef<number | undefined>(undefined)
  const currentPhase = Math.floor(now / DEMO_TICK_MS)

  if (currentGameIdRef.current !== game?.id) {
    currentGameIdRef.current = game?.id
    initialPhaseRef.current = currentPhase
  }

  if (initialPhaseRef.current === undefined) {
    initialPhaseRef.current = currentPhase
  }

  const scenario = useMemo(() => {
    if (!game?.id || !game.title) return
    return createScenario(game)
  }, [game?.id, game?.title])

  return useMemo(() => {
    if (!scenario) return

    const phaseProgress = Math.max(0, currentPhase - (initialPhaseRef.current ?? currentPhase))
    const visibleCount = Math.min(INITIAL_VISIBLE_SUBMISSIONS + phaseProgress, scenario.schedule.length)
    const tickNow = currentPhase * DEMO_TICK_MS
    const timedSubmissions = createTimedSubmissions(scenario, tickNow, visibleCount)

    return {
      events: buildEvents(scenario, timedSubmissions),
      participations: buildParticipations(scenario),
      scoreboard: buildScoreboard(scenario, timedSubmissions, tickNow),
      submissions: buildSubmissions(scenario, timedSubmissions),
    }
  }, [currentPhase, scenario])
}
