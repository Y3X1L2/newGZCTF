import useSWR from 'swr'
import { netlabApi, type WorkspaceTarget } from './api/netlabApi'

export function useNetlabWorkspace(target: WorkspaceTarget) {
  const request = useSWR(['netlab:workspace', target], () => netlabApi.workspace(target), { revalidateOnFocus: false })
  return { ...request, reload: () => request.mutate() }
}
