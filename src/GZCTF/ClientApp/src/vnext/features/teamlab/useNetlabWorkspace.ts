import { useEffect, useRef } from 'react'
import useSWR from 'swr'
import { useVNextTheme } from '../../app/VNextThemeProvider'
import { netlabApi, type WorkspaceTarget } from './api/netlabApi'

export function useNetlabWorkspace(target: WorkspaceTarget) {
  const request = useSWR(['netlab:workspace', target], () => netlabApi.workspace(target), { revalidateOnFocus: false })
  const frame = useRef<HTMLIFrameElement>(null)
  const { theme } = useVNextTheme()
  useEffect(() => {
    const updateTheme = () => frame.current?.contentWindow?.postMessage({ type: 'netlab:theme', theme }, window.location.origin)
    const receive = (event: MessageEvent) => {
      if (event.source !== frame.current?.contentWindow || event.origin !== window.location.origin) return
      if (event.data?.type === 'netlab:ready') {
        updateTheme()
      }
    }
    window.addEventListener('message', receive)
    updateTheme()
    return () => window.removeEventListener('message', receive)
  }, [theme])
  const reload = () => {
    frame.current?.contentWindow?.location.reload()
    return request.mutate()
  }
  return { ...request, frame, reload }
}
