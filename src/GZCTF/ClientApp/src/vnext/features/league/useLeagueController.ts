import { useRef, useState } from 'react'
import useSWR from 'swr'
import type { LeagueMatchDetail } from '@Api'
import { useCurrentAccount } from '../account/useCurrentAccount'
import { leagueApi } from './api/leagueApi'
import { leagueCatalogApi } from './api/leagueCatalogApi'
import { leagueEnabled } from './leagueFeature'
import { leagueError } from './leaguePresentation'

const queryOptions = { shouldRetryOnError: false, revalidateOnFocus: false }

export function useLeagueIdentity() {
  const account = useCurrentAccount()
  const identity = account.user?.userId ? `${account.user.userId}:${account.user.role}` : null
  return { ...account, identity: leagueEnabled ? identity : null }
}

export function useLeagueList(after?: string) {
  const account = useLeagueIdentity()
  const request = useSWR(
    account.identity ? ['league', account.identity, 'list', after] : null,
    () => leagueApi.list(after),
    queryOptions
  )
  return { account, request }
}

export function useLeagueDetail(matchId: string) {
  const account = useLeagueIdentity()
  const request = useSWR(
    account.identity ? ['league', account.identity, matchId] : null,
    () => leagueApi.detail(matchId),
    queryOptions
  )
  const teams = useSWR(
    account.identity && request.data?.allowedActions?.includes('register')
      ? ['league', account.identity, 'teams']
      : null,
    leagueCatalogApi.teams,
    queryOptions
  )
  const action = useLeagueMutation()
  async function write(operation: () => Promise<LeagueMatchDetail>) {
    return action.run(async () => {
      let detail: LeagueMatchDetail
      try {
        detail = await operation()
      } catch (error) {
        if ((error as { response?: { status?: number } })?.response?.status === 409) {
          await request.mutate().catch(() => undefined)
        }
        throw error
      }
      await request.mutate(detail, { revalidate: false })
      return detail
    })
  }
  return { account, request, teams, action, write }
}

export function useLeagueMutation() {
  const lock = useRef(false)
  const [busy, setBusy] = useState(false)
  const [feedback, setFeedback] = useState<{ text: string; failed: boolean } | null>(null)
  async function run<T>(operation: () => Promise<T>): Promise<T | undefined> {
    if (lock.current) return undefined
    lock.current = true
    setBusy(true)
    setFeedback(null)
    try {
      const result = await operation()
      setFeedback({ text: '操作已保存，页面已读取服务器返回的最新结果。', failed: false })
      return result
    } catch (error) {
      setFeedback({ text: leagueError(error), failed: true })
      return undefined
    } finally {
      lock.current = false
      setBusy(false)
    }
  }
  return { busy, feedback, run }
}

export function useLeagueSceneCatalog(identity: string, topologyId: string) {
  const [cursors, setCursors] = useState<string[]>([])
  const after = cursors.at(-1)
  const scenes = useSWR(['league', identity, 'scenes', after], () => leagueCatalogApi.scenes(after), queryOptions)
  const releases = useSWR(
    topologyId ? ['league', identity, 'releases', topologyId] : null,
    () => leagueCatalogApi.releases(topologyId),
    queryOptions
  )
  return {
    scenes,
    releases,
    page: cursors.length + 1,
    previous: () => setCursors((values) => values.slice(0, -1)),
    next: () => {
      if (scenes.data?.nextCursor) setCursors((values) => [...values, scenes.data!.nextCursor!])
    },
  }
}
