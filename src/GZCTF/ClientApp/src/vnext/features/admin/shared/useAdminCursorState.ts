import { useCallback, useState } from 'react'

export function useAdminCursorState(resetKey: string) {
  const [state, setState] = useState(() => ({ resetKey, cursorStack: [] as string[] }))
  // A changed filter must render from its first page immediately. Resetting in
  // an effect leaks the former cursor into one render and one request.
  const cursorStack = state.resetKey === resetKey ? state.cursorStack : []

  const next = useCallback((cursor: string) => {
    setState((current) => ({
      resetKey,
      cursorStack: current.resetKey === resetKey ? [...current.cursorStack, cursor] : [cursor],
    }))
    window.scrollTo({ top: 0, behavior: 'smooth' })
  }, [resetKey])

  const previous = useCallback(() => {
    setState((current) => ({
      resetKey,
      cursorStack: (current.resetKey === resetKey ? current.cursorStack : []).slice(0, -1),
    }))
    window.scrollTo({ top: 0, behavior: 'smooth' })
  }, [resetKey])

  const reset = useCallback(() => setState({ resetKey, cursorStack: [] }), [resetKey])

  return {
    cursor: cursorStack.at(-1) ?? null,
    page: cursorStack.length + 1,
    canGoBack: cursorStack.length > 0,
    next,
    previous,
    reset,
  }
}
