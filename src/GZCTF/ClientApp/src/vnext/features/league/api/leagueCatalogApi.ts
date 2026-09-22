import api from '@Api'
import { teamLabAdminApi } from '../../admin/teamlab/api/teamlabAdminApi'

export const leagueCatalogApi = {
  async teams() {
    return (await api.team.teamGetTeamsInfo()).data
  },
  scenes(after?: string) {
    return teamLabAdminApi.listTopologies({ cursor: after, limit: 30 })
  },
  releases(topologyId: string) {
    return teamLabAdminApi.listReleases(topologyId)
  },
}
