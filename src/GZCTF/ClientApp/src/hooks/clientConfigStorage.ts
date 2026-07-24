import { PLATFORM_TITLE, joinPlatformSlogans } from '@Utils/Brand'
import { ClientConfig, ContainerPortMappingType } from '@Api'

export const clientConfigStorageKey = 'client-config'

export const defaultClientConfig: ClientConfig = {
  title: PLATFORM_TITLE,
  slogan: joinPlatformSlogans([]),
  portMapping: ContainerPortMappingType.Default,
  footerInfo: null,
  customTheme: null,
  defaultLifetime: 120,
  extensionDuration: 120,
  renewalWindow: 10,
}

export function parseStoredClientConfig(value: string | null) {
  if (!value) return defaultClientConfig

  try {
    return { ...defaultClientConfig, ...(JSON.parse(value) as Partial<ClientConfig>) }
  } catch {
    return defaultClientConfig
  }
}

export function readStoredClientConfig() {
  if (typeof window === 'undefined') return defaultClientConfig
  return parseStoredClientConfig(window.localStorage.getItem(clientConfigStorageKey))
}

export function storeClientConfig(config: ClientConfig) {
  if (typeof window === 'undefined') return

  try {
    window.localStorage.setItem(clientConfigStorageKey, JSON.stringify(config))
  } catch {
    // The live API response remains authoritative when storage is unavailable.
  }
}
