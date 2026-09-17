import { useMemo } from 'react'
import useSWR from 'swr'
import { teamLabAdminApi, teamLabAdminKeys, teamLabRuntimeApi, teamLabRuntimeKeys } from '../api'

export function useRuntimeUpdatePreview(
  topologyId: string,
  runtimeId: string,
  currentReleaseId?: string,
  enabled = true
) {
  const releases = useSWR(
    topologyId && enabled ? teamLabAdminKeys.releases(topologyId) : null,
    () => teamLabAdminApi.listReleases(topologyId),
    { revalidateOnFocus: true }
  )
  const latestRelease = useMemo(
    () => releases.data?.length
      ? releases.data.reduce((latest, item) => item.version > latest.version ? item : latest)
      : undefined,
    [releases.data]
  )
  const targetReleaseId = latestRelease?.id !== currentReleaseId ? latestRelease?.id : undefined
  const preview = useSWR(
    runtimeId && targetReleaseId ? teamLabRuntimeKeys.updatePreview(runtimeId, targetReleaseId) : null,
    () => teamLabRuntimeApi.previewUpdate(runtimeId, targetReleaseId!),
    { revalidateOnFocus: true }
  )

  return {
    latestRelease,
    preview: preview.data,
    error: releases.error ?? preview.error,
    isLoading: Boolean(targetReleaseId) && !preview.data && !preview.error,
  }
}
