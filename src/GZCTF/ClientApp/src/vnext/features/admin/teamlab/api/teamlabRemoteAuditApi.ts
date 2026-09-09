import { runtimeJsonClient } from '../../api/runtimeJsonClient'
import { teamLabParsing as parse } from './teamlabParsers'
import { downloadFileUrl } from '@Utils/downloadFileUrl'

const base = (sessionId: string) => `/api/admin/teamlab/remote-sessions/${encodeURIComponent(sessionId)}/audit-files`
function parsePage(value: unknown) {
  const page = parse.record(value, 'remote audit')
  return { state: parse.string(page.state, 'state'), retentionDays: parse.number(page.retentionDays, 'retentionDays'),
    items: parse.array(page.items, 'files', (value, label) => {
      const file = parse.record(value, label)
      return { id: parse.number(file.id, 'id'), size: parse.number(file.size, 'size'), sha256: parse.string(file.sha256, 'sha256'),
        createdAt: parse.number(file.createdAt, 'createdAt'), expiresAt: parse.nullableNumber(file.expiresAt, 'expiresAt') }
    }) }
}
export const remoteAuditApi = {
  list: async (sessionId: string) => parsePage(await runtimeJsonClient.get(base(sessionId))),
  generate: async (sessionId: string) => parsePage(await runtimeJsonClient.postJson(base(sessionId), {})),
  download: async (sessionId: string, fileId: number) => {
    downloadFileUrl(`${base(sessionId)}/${fileId}/download`, `session-${sessionId}-audit.json`)
  },
}
