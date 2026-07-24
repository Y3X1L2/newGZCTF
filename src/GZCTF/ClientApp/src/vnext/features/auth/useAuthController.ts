import { useCallback, useState } from 'react'
import useSWR from 'swr'
import { errorMessage } from '../../shared/errors'
import { authApi } from './api/authApi'

const capabilitiesKey = 'vnext:account-capabilities'

export function useAuthCapabilities() {
  const result = useSWR(capabilitiesKey, authApi.capabilities, {
    revalidateOnFocus: false,
    shouldRetryOnError: false,
  })

  return {
    capabilities: result.data,
    error: result.error,
    loading: !result.data && !result.error,
    retry: result.mutate,
  }
}

export function useAuthAction() {
  const [pending, setPending] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const run = useCallback(async <T,>(action: () => Promise<T>) => {
    if (pending) return null
    setPending(true)
    setError(null)
    try {
      return await action()
    } catch (cause) {
      setError(errorMessage(cause, '请求未能完成，请检查网络后重试。'))
      return null
    } finally {
      setPending(false)
    }
  }, [pending])

  return { pending, error, setError, run }
}
