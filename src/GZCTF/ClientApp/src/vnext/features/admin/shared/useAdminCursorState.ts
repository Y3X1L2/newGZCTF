import { useCallback, useEffect, useState } from 'react'

export function useAdminCursorState(resetKey: string) {
  const [cursorStack, setCursorStack] = useState<string[]>([])

  useEffect(() => {
    setCursorStack([])
  }, [resetKey])

  const next = useCallback((cursor: string) => {
    setCursorStack((current) => [...current, cursor])
    window.scrollTo({ top: 0, behavior: 'smooth' })
  }, [])

  const previous = useCallback(() => {
    setCursorStack((current) => current.slice(0, -1))
    window.scrollTo({ top: 0, behavior: 'smooth' })
  }, [])

  const reset = useCallback(() => setCursorStack([]), [])

  return {
    cursor: cursorStack.at(-1) ?? null,
    page: cursorStack.length + 1,
    canGoBack: cursorStack.length > 0,
    next,
    previous,
    reset,
  }
}
