import { runtimeJsonClient } from '../../admin/api/runtimeJsonClient'

export interface WorkspaceTarget {
  section?: 'environments' | 'templates' | 'resources' | 'workbench'
  environmentId?: string
  runtimeId?: string
  gameId?: number
}

export interface WorkspaceConnection {
  available: boolean
  environmentId?: string
  permissions?: string[]
  error?: string
}

export interface NetlabConnection {
  endpoint: string
  projectName: string
  configured: boolean
}

export const netlabApi = {
  workspace: (target: WorkspaceTarget) =>
    runtimeJsonClient.get('/api/teamlab/workspace', { ...target }) as Promise<WorkspaceConnection>,
  connection: () => runtimeJsonClient.get('/api/teamlab/connection') as Promise<NetlabConnection>,
  saveConnection: (connection: { endpoint: string; projectName: string; token?: string }) =>
    runtimeJsonClient.putJson('/api/teamlab/connection', connection),
}
