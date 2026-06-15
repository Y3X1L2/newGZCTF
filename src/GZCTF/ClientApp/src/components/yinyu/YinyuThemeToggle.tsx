import { ActionIcon, Tooltip } from '@mantine/core'
import { Layers, Sparkles } from 'lucide-react'
import { Dispatch, SetStateAction, useEffect, useState } from 'react'

export type YinyuVisualTheme = 'classic' | 'crystal'

const THEME_STORAGE_KEY = 'yinyu.visualTheme'

const readStoredTheme = (): YinyuVisualTheme => {
  if (typeof window === 'undefined') return 'classic'
  const theme = window.localStorage.getItem(THEME_STORAGE_KEY) === 'crystal' ? 'crystal' : 'classic'
  document.documentElement.dataset.yyTheme = theme
  return theme
}

export const useYinyuVisualTheme = (): [YinyuVisualTheme, Dispatch<SetStateAction<YinyuVisualTheme>>] => {
  const [theme, setTheme] = useState<YinyuVisualTheme>(readStoredTheme)

  useEffect(() => {
    document.documentElement.dataset.yyTheme = theme
    window.localStorage.setItem(THEME_STORAGE_KEY, theme)
  }, [theme])

  return [theme, setTheme]
}

export function YinyuThemeToggle({
  theme,
  onChange,
}: {
  theme: YinyuVisualTheme
  onChange: (theme: YinyuVisualTheme) => void
}) {
  const isCrystal = theme === 'crystal'
  const nextTheme = isCrystal ? 'classic' : 'crystal'

  return (
    <Tooltip label={isCrystal ? '切换为主线主题' : '切换为晶体主题'} position="right" withArrow>
      <ActionIcon
        aria-label={isCrystal ? '切换为主线主题' : '切换为晶体主题'}
        className="yy-theme-toggle"
        data-active={isCrystal}
        onClick={() => onChange(nextTheme)}
        size="lg"
        variant="default"
      >
        {isCrystal ? <Sparkles size={18} /> : <Layers size={18} />}
      </ActionIcon>
    </Tooltip>
  )
}
