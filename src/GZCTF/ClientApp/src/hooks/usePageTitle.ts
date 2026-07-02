import { useDocumentTitle } from '@mantine/hooks'
import { getPlatformBrand } from '@Utils/Brand'
import { useConfig } from '@Hooks/useConfig'

export const usePageTitle = (title?: string) => {
  const { config, error } = useConfig()

  const platform = error ? getPlatformBrand() : getPlatformBrand(config?.title)

  useDocumentTitle(typeof title === 'string' && title.trim().length > 0 ? `${title} - ${platform}` : platform)
}
