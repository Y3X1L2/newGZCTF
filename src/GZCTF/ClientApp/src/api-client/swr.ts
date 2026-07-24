import type { Cache, SWRConfiguration } from 'swr'
import { fetcher } from '../generated/Api'

const responseStatus = (error: unknown) => {
  if (!error || typeof error !== 'object') return undefined

  const response = Reflect.get(error, 'response')
  if (response && typeof response === 'object') {
    const status = Reflect.get(response, 'status')
    if (typeof status === 'number') return status
  }

  const status = Reflect.get(error, 'status')
  return typeof status === 'number' ? status : undefined
}

export const createSWRConfig = (): SWRConfiguration => ({
  provider: (): Cache => new Map(),
  fetcher,
  refreshInterval: 0,
  dedupingInterval: 2_000,
  focusThrottleInterval: 10_000,
  errorRetryCount: 3,
  keepPreviousData: false,
  revalidateOnReconnect: true,
  shouldRetryOnError: true,
  onErrorRetry: (error, _key, _config, revalidate, context) => {
    const status = responseStatus(error)
    if (status === 401 || status === 403 || status === 404 || context.retryCount >= 3) return

    const delay = Math.min(1_000 * 2 ** context.retryCount, 8_000)
    window.setTimeout(() => revalidate({ retryCount: context.retryCount }), delay)
  },
})
