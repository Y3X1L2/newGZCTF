import { useLocalStorage } from '@mantine/hooks'
import { useEffect } from 'react'
import { SWRConfiguration } from 'swr'
import { PLATFORM_SLOGAN, PLATFORM_TITLE } from '@Utils/Brand'
import api, { ClientConfig, ContainerPortMappingType } from '@Api'

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

  const [clientConfig, setClientConfig] = useLocalStorage<ClientConfig>({
    key: 'client-config',
    defaultValue: {
      title: PLATFORM_TITLE,
      slogan: PLATFORM_SLOGAN,
      portMapping: ContainerPortMappingType.Default,
      footerInfo: null,
      customTheme: null,
      defaultLifetime: 120,
      extensionDuration: 120,
      renewalWindow: 10,
    },
  })

  useEffect(() => {
    if (config) {
      setClientConfig(config)
    }
  }, [config])

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
