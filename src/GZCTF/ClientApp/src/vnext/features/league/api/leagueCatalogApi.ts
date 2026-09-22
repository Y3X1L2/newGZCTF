import api from '@Api'
import { teamLabAdminApi } from '../../admin/teamlab/api/teamlabAdminApi'
import { parseTeamLabReleaseList } from '../../admin/teamlab/api/teamlabParsers'

export const leagueCatalogApi = {
  async teams() {
    return (await api.team.teamGetTeamsInfo()).data
  },
  scenes(after?: string) {
    return teamLabAdminApi.listTopologies({ cursor: after, limit: 30 })
  },
  async releases(topologyId: string) {
    const { data } = await api.teamLabAdminTopology.teamLabAdminTopologyReleases(topologyId)
    return parseTeamLabReleaseList(data.filter((release) => !release.archived))
  },
}
