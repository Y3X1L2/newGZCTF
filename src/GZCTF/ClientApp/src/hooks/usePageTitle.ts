import { useDocumentTitle } from '@mantine/hooks'
import { getPlatformName } from '@Utils/Brand'
import { useConfig } from '@Hooks/useConfig'

export const usePageTitle = (title?: string) => {
  const { config, error } = useConfig()

  const platform = error ? getPlatformName() : getPlatformName(config?.title)

  useDocumentTitle(typeof title === 'string' && title.trim().length > 0 ? `${title} - ${platform}` : platform)
}
