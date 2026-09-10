import useSWR from 'swr'
import { readVmDiagnostics } from '../api/teamlabAssetDiagnosticsApi'

export function useVmDiagnostics(runtimeId: string, generation: number, assetId?: number) {
  return useSWR(assetId === undefined ? null : ['teamlab:vm-diagnostics', runtimeId, generation, assetId],
    () => readVmDiagnostics(runtimeId, assetId!),
    { keepPreviousData: false, revalidateOnFocus: false, shouldRetryOnError: false })
}
