import useSWR from 'swr'
import api, { BasicGameInfoModel } from '@Api'

export type GameListStatus = 'ongoing' | 'upcoming' | 'ended'

export interface GameCatalogItem extends BasicGameInfoModel {
  status: GameListStatus
  startsAt: number
  endsAt: number
}

const PAGE_SIZE = 50

function statusOf(game: BasicGameInfoModel, now = Date.now()): GameListStatus {
  if (now < game.start) return 'upcoming'
  if (now >= game.end) return 'ended'
  return 'ongoing'
}

function sortGames(left: GameCatalogItem, right: GameCatalogItem) {
  const rank: Record<GameListStatus, number> = { ongoing: 0, upcoming: 1, ended: 2 }
  const statusDifference = rank[left.status] - rank[right.status]
  if (statusDifference !== 0) return statusDifference
  if (left.status === 'ended') return right.endsAt - left.endsAt
  return left.startsAt - right.startsAt
}

async function fetchGameCatalog() {
  const first = await api.game.gameGames({ count: PAGE_SIZE, skip: 0 })
  const total = first.data.total ?? first.data.data.length
  const data = [...first.data.data]

  for (let skip = PAGE_SIZE; skip < total; skip += PAGE_SIZE) {
    const response = await api.game.gameGames({ count: PAGE_SIZE, skip })
    data.push(...response.data.data)
  }

  return data
    .map<GameCatalogItem>((game) => ({
      ...game,
      startsAt: game.start,
      endsAt: game.end,
      status: statusOf(game),
    }))
    .sort(sortGames)
}

export function useGameCatalog() {
  const result = useSWR('vnext:game-catalog', fetchGameCatalog, {
    refreshInterval: 5 * 60 * 1000,
    revalidateOnFocus: false,
  })

  return {
    games: result.data,
    error: result.error,
    isLoading: !result.data && !result.error,
    mutate: result.mutate,
  }
}

export function gameStatusLabel(status: GameListStatus) {
  if (status === 'ongoing') return '进行中'
  if (status === 'upcoming') return '即将开始'
  return '已结束'
}

export function gameStatusTone(status: GameListStatus) {
  if (status === 'ongoing') return 'success' as const
  if (status === 'upcoming') return 'info' as const
  return 'neutral' as const
}

export function participationLabel(limit?: number) {
  if (limit === 1) return '个人参赛'
  if (!limit) return '团队参赛'
  return `每队最多 ${limit} 人`
}

export function formatGameTime(value: number) {
  return new Intl.DateTimeFormat('zh-CN', {
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    hour12: false,
  }).format(value)
}

export function formatGameRange(game: GameCatalogItem) {
  return `${formatGameTime(game.startsAt)} - ${formatGameTime(game.endsAt)}`
}
