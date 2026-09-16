import { useCallback, useEffect, useRef, useState } from 'react'
import { copyText } from '@Utils/clipboard'

export function useClipboard({ timeout = 2000 }: { timeout?: number } = {}) {
  const [copied, setCopied] = useState(false)
  const [error, setError] = useState<Error | null>(null)
  const timer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined)
  const request = useRef(0)

  const reset = useCallback(() => {
    request.current += 1
    clearTimeout(timer.current)
    setCopied(false)
    setError(null)
  }, [])

  useEffect(
    () => () => {
      request.current += 1
      clearTimeout(timer.current)
    },
    []
  )

  const copy = useCallback(
    async (value?: string | null) => {
      const id = ++request.current
      clearTimeout(timer.current)
      setCopied(false)
      setError(null)
      const success = await copyText(value ?? '')
      if (id !== request.current) return success
      setCopied(success)
      if (success) timer.current = setTimeout(() => setCopied(false), timeout)
      else setError(new Error('复制失败，请选中文本后手动复制。'))
      return success
    },
    [timeout]
  )

  return { copied, error, copy, reset }
}
