import { expect, it, vi } from 'vitest'
import api from '@Api'
import { leagueCatalogApi } from './leagueCatalogApi'

vi.mock('@Api', () => ({
  default: { teamLabAdminTopology: { teamLabAdminTopologyReleases: vi.fn() } },
}))

it('excludes archived releases because the league backend rejects them as fixed scenes', async () => {
  const release = {
    id: 'active',
    topologyId: 'scene',
    version: 2,
    sourceRevision: 3,
    schemaVersion: 2,
    contentHash: 'hash',
    publishedBy: null,
    publisherName: null,
    publishedAt: 1790000000000,
    archived: false,
  }
  vi.mocked(api.teamLabAdminTopology.teamLabAdminTopologyReleases).mockResolvedValue({
    data: [{ ...release, id: 'archived', version: 1, archived: true }, release],
  } as never)
  const choices = await leagueCatalogApi.releases('scene')
  expect(choices.map((choice) => choice.id)).toEqual(['active'])
  expect(api.teamLabAdminTopology.teamLabAdminTopologyReleases).toHaveBeenCalledExactlyOnceWith('scene')
})
