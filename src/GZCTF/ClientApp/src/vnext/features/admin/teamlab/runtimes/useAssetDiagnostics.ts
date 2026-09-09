import useSWR from 'swr'
import { readAssetDiagnostics } from '../api/teamlabAssetDiagnosticsApi'

export function useAssetDiagnostics(runtimeId: string, generation: number, assetId: number | undefined, tail: number) {
  return useSWR(assetId === undefined ? null : ['teamlab:asset-diagnostics', runtimeId, generation, assetId, tail],
    () => readAssetDiagnostics(runtimeId, assetId!, tail),
    { keepPreviousData: false, revalidateOnFocus: false, shouldRetryOnError: false })
}
