import { createContext, FC, PropsWithChildren, useContext, useEffect, useMemo, useState } from 'react'

export type VNextTheme = 'light' | 'dark'

interface VNextThemeContextValue {
  theme: VNextTheme
  setTheme: (theme: VNextTheme) => void
  toggleTheme: () => void
}

const VNextThemeContext = createContext<VNextThemeContextValue | null>(null)
const THEME_STORAGE_KEY = 'yinyu-vnext-theme'

function getInitialTheme(): VNextTheme {
  if (typeof window === 'undefined') return 'light'
  return window.localStorage.getItem(THEME_STORAGE_KEY) === 'dark' ? 'dark' : 'light'
}

export const VNextThemeProvider: FC<PropsWithChildren> = ({ children }) => {
  const [theme, setTheme] = useState<VNextTheme>(getInitialTheme)

  useEffect(() => {
    document.documentElement.dataset.yinyuTheme = theme
    window.localStorage.setItem(THEME_STORAGE_KEY, theme)
  }, [theme])

  const value = useMemo<VNextThemeContextValue>(
    () => ({
      theme,
      setTheme,
      toggleTheme: () => setTheme((current) => (current === 'light' ? 'dark' : 'light')),
    }),
    [theme]
  )

  return <VNextThemeContext.Provider value={value}>{children}</VNextThemeContext.Provider>
}

export function useVNextTheme() {
  const context = useContext(VNextThemeContext)
  if (!context) throw new Error('useVNextTheme must be used inside VNextThemeProvider')
  return context
}
