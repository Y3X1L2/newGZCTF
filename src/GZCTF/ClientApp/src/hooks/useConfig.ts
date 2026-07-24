import { useEffect, useState } from 'react'
import { SWRConfiguration } from 'swr'
import { PLATFORM_BRAND, joinPlatformSlogans } from '@Utils/Brand'
import api from '@Api'
import {
  clientConfigStorageKey,
  parseStoredClientConfig,
  readStoredClientConfig,
  storeClientConfig,
} from './clientConfigStorage'

export const OnceSWRConfig: SWRConfiguration = {
  refreshInterval: 0,
  revalidateOnFocus: false,
}

export const useConfig = () => {
  const {
    data: config,
    error,
    mutate,
  } = api.info.useInfoGetClientConfig({
    refreshInterval: 0,
    revalidateOnFocus: false,
    revalidateOnReconnect: false,
    refreshWhenHidden: false,
    shouldRetryOnError: false,
    refreshWhenOffline: false,
  })

  const [clientConfig, setClientConfig] = useState(readStoredClientConfig)

  useEffect(() => {
    if (!config) return

    setClientConfig(config)
    storeClientConfig(config)
  }, [config])

  useEffect(() => {
    const syncStoredConfig = (event: StorageEvent) => {
      if (event.key !== clientConfigStorageKey || !event.newValue) return
      setClientConfig(parseStoredClientConfig(event.newValue))
    }

    window.addEventListener('storage', syncStoredConfig)
    return () => window.removeEventListener('storage', syncStoredConfig)
  }, [])

  return { config: config ?? clientConfig, error, mutate }
}

export const useCaptchaConfig = () => {
  const { data, error, mutate } = api.info.useInfoGetClientCaptchaInfo({
    refreshInterval: 0,
    revalidateOnFocus: false,
    revalidateOnReconnect: false,
    refreshWhenHidden: false,
    shouldRetryOnError: false,
    refreshWhenOffline: false,
  })

  return { info: data, error, mutate }
}

export const useBanner = () => {
  useEffect(() => {
    if (typeof window === 'undefined') return

    console.info(`%c${PLATFORM_BRAND}`, 'color:#6beeb1;font-weight:800;font-size:16px;')
    console.info(joinPlatformSlogans([]))
  }, [])
}
