import { useEffect, useRef } from 'react'
import { useBlocker } from 'react-router'

export function useTopologyNavigationGuard(shouldBlock: boolean, flush: () => Promise<boolean>) {
  const blocker = useBlocker(shouldBlock)
  const activeAttempt = useRef<string | null>(null)

  useEffect(() => {
    if (blocker.state !== 'blocked') {
      activeAttempt.current = null
      return
    }
    if (activeAttempt.current === blocker.location.key) return
    activeAttempt.current = blocker.location.key

    void flush().then((saved) => {
      if (saved) blocker.proceed()
      else blocker.reset()
    })
  }, [blocker, flush])
}
