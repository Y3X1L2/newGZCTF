import { useEffect } from 'react'
import { getPlatformBrand } from '@Utils/Brand'
import { useConfig } from '@Hooks/useConfig'

export function useVNextPageTitle(page?: string) {
  const { config } = useConfig()

  useEffect(() => {
    const platform = getPlatformBrand(config.title)
    document.title = page ? `${page} - ${platform}` : platform
  }, [config.title, page])
}
