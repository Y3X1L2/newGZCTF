import { useCallback, useEffect, useMemo, useState } from 'react'
import { useParams, useSearchParams } from 'react-router'
import useSWR from 'swr'
import useSWRInfinite from 'swr/infinite'
import { useCurrentAccount } from '../account/useCurrentAccount'
import { userProfileApi, type UserProfileHistoryPage } from './api/userProfileApi'
import {
  historyTypeForTab,
  profileDateRange,
  resolveProfileTab,
  resolveProfileWindow,
  type ProfileTab,
  type ProfileWindow,
} from './profileDomain'

const requestOptions = {
  revalidateOnFocus: false,
  shouldRetryOnError: false,
} as const

function useDeferredSection() {
  const [target, setTarget] = useState<HTMLDivElement | null>(null)
  const [enabled, setEnabled] = useState(false)

  useEffect(() => {
    if (!target || enabled) return undefined
    if (!('IntersectionObserver' in window)) {
      setEnabled(true)
      return undefined
    }
    const observer = new IntersectionObserver(
      (entries) => {
        if (!entries.some((entry) => entry.isIntersecting)) return
        setEnabled(true)
        observer.disconnect()
      },
      { rootMargin: '240px 0px' }
    )
    observer.observe(target)
    return () => observer.disconnect()
  }, [enabled, target])

  return { targetRef: setTarget, enabled }
}

export function useAccountSummary(enabled: boolean) {
  return useSWR(enabled ? 'vnext:account-summary' : null, () => userProfileApi.accountSummary(), requestOptions)
}

export function useUserProfileController() {
  const { userId: routeUserId } = useParams<{ userId: string }>()
  const [searchParams, setSearchParams] = useSearchParams()
  const account = useCurrentAccount()
  const activitySection = useDeferredSection()
  const historySection = useDeferredSection()
  const isMeRoute = routeUserId?.toLowerCase() === 'me'
  const resolvedUserId = isMeRoute ? account.user?.userId : routeUserId
  const tab = resolveProfileTab(searchParams.get('tab'))
  const window = resolveProfileWindow(searchParams.get('window'))
  const range = useMemo(() => profileDateRange(window), [window])
  const historyType = historyTypeForTab(tab)

  useEffect(() => {
    const rawTab = searchParams.get('tab')
    const rawWindow = searchParams.get('window')
    if ((rawTab === null || rawTab === tab) && (rawWindow === null || rawWindow === window)) return
    const next = new URLSearchParams(searchParams)
    next.set('tab', tab)
    next.set('window', window)
    setSearchParams(next, { replace: true })
  }, [searchParams, setSearchParams, tab, window])

  const profileRequest = useSWR(
    resolvedUserId ? ['vnext:user-profile', resolvedUserId] : null,
    () => userProfileApi.profile(resolvedUserId as string),
    requestOptions
  )
  const overviewRequest = useSWR(
    resolvedUserId ? ['vnext:user-profile-overview', resolvedUserId, window] : null,
    () => userProfileApi.overview(resolvedUserId as string, window),
    { ...requestOptions, keepPreviousData: true }
  )
  const activityRequest = useSWR(
    resolvedUserId && activitySection.enabled
      ? ['vnext:user-profile-activity', resolvedUserId, range.from, range.to]
      : null,
    () => userProfileApi.activity(resolvedUserId as string, range.from, range.to),
    { ...requestOptions, keepPreviousData: true }
  )
  const privateOverviewRequest = useSWR(
    resolvedUserId && account.user?.userId === resolvedUserId && activitySection.enabled
      ? 'vnext:user-private-overview'
      : null,
    () => userProfileApi.privateOverview(),
    requestOptions
  )

  const historyRequest = useSWRInfinite<UserProfileHistoryPage>(
    (pageIndex, previousPage) => {
      if (!resolvedUserId || !historySection.enabled) return null
      if (pageIndex > 0 && !previousPage?.nextCursor) return null
      return [
        'vnext:user-profile-history',
        resolvedUserId,
        historyType,
        pageIndex === 0 ? null : previousPage?.nextCursor ?? null,
      ] as const
    },
    (key) => {
      const [, userId, type, cursor] = key as readonly [string, string, string, string | null]
      return userProfileApi.history(userId, type, cursor)
    },
    requestOptions
  )

  useEffect(() => {
    void historyRequest.setSize(1)
  }, [historyType, resolvedUserId, historyRequest.setSize])

  const setTab = useCallback(
    (nextTab: ProfileTab) => {
      const next = new URLSearchParams(searchParams)
      next.set('tab', nextTab)
      setSearchParams(next)
    },
    [searchParams, setSearchParams]
  )
  const setWindow = useCallback(
    (nextWindow: ProfileWindow) => {
      const next = new URLSearchParams(searchParams)
      next.set('window', nextWindow)
      setSearchParams(next, { replace: true })
    },
    [searchParams, setSearchParams]
  )

  const historyItems = useMemo(
    () => historyRequest.data?.flatMap((page) => page.items) ?? [],
    [historyRequest.data]
  )
  const lastHistoryPage = historyRequest.data?.at(-1)

  return {
    account,
    isMeRoute,
    isOwnProfile: Boolean(resolvedUserId && account.user?.userId === resolvedUserId),
    resolvedUserId,
    tab,
    window,
    range,
    setTab,
    setWindow,
    profile: profileRequest.data,
    profileError: profileRequest.error,
    profileLoading: Boolean(resolvedUserId && !profileRequest.data && !profileRequest.error),
    overview: overviewRequest.data,
    overviewError: overviewRequest.error,
    overviewLoading: Boolean(resolvedUserId && !overviewRequest.data && !overviewRequest.error),
    activity: activityRequest.data,
    activityError: activityRequest.error,
    activityLoading: activitySection.enabled && !activityRequest.data && !activityRequest.error,
    activityRef: activitySection.targetRef,
    privateOverview: privateOverviewRequest.data,
    historyItems,
    historyError: historyRequest.error,
    historyLoading: historySection.enabled && !historyRequest.data && !historyRequest.error,
    historyLoadingMore: historyRequest.isValidating && Boolean(historyRequest.data),
    historyRef: historySection.targetRef,
    hasMoreHistory: Boolean(lastHistoryPage?.nextCursor),
    loadMoreHistory: () => void historyRequest.setSize((size) => size + 1),
  }
}
