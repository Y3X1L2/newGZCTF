import { useState } from 'react'
import useSWR from 'swr'
import { remoteAuditApi } from '../api/teamlabRemoteAuditApi'

export function useRemoteAudit(sessionId: string) {
  const [generating, setGenerating] = useState(false)
  const [actionError, setActionError] = useState<unknown>(null)
  const [downloading, setDownloading] = useState(false)
  const query = useSWR(['teamlab:remote-audit', sessionId], () => remoteAuditApi.list(sessionId),
    { keepPreviousData: false, shouldRetryOnError: false, refreshInterval: data => data?.state === 'pending' ? 5000 : 0 })
  const generate = async () => {
    if (generating) return
    setGenerating(true)
    setActionError(null)
    try { await query.mutate(await remoteAuditApi.generate(sessionId), { revalidate: false }) }
    catch (error) { setActionError(error) }
    finally { setGenerating(false) }
  }
  const download = async (fileId: number) => {
    if (downloading) return
    setDownloading(true)
    setActionError(null)
    try { await remoteAuditApi.download(sessionId, fileId) }
    catch (error) { setActionError(error) }
    finally { setDownloading(false) }
  }
  return { ...query, generating, actionError, generate, downloading, download }
}
