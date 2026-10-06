import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { SWRConfig } from 'swr'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { listTeamLabImageOptions, teamLabAdminApi, type TeamLabTopologyDetail } from '../api'
import { useTeamLabScene } from '../shared/TeamLabSceneShell'
import { TeamLabDesignRoute } from './TeamLabDesignRoute'

vi.mock('../shared/TeamLabSceneShell', () => ({ useTeamLabScene: vi.fn() }))
vi.mock('./state/useTopologyNavigationGuard', () => ({ useTopologyNavigationGuard: vi.fn() }))
vi.mock('../api', async importOriginal => ({
  ...(await importOriginal<typeof import('../api')>()),
  listTeamLabImageOptions: vi.fn(),
}))
vi.mock('./TeamLabDesignPage', () => ({
  TeamLabDesignPage: ({ initialDocument, onDocumentChange, onPublish }: {
    initialDocument: { name: string }
    onDocumentChange: (document: { name: string }) => void
    onPublish: () => void
  }) => <>
    <button onClick={() => onDocumentChange({ ...initialDocument, name: 'Updated scene' })} type="button">修改草稿</button>
    <button onClick={onPublish} type="button">检查并发布</button>
  </>,
}))

const scene: TeamLabTopologyDetail = {
  id: '019f0000-0000-7000-8000-000000000001',
  revision: 1,
  schemaVersion: 2,
  definition: { name: 'Original scene', networks: [], assets: [], infrastructure: [], connections: [], observation: { flowMetadataEnabled: false, onDemandPcapEnabled: false } },
  editor: { networks: {}, assets: {}, infrastructure: {} },
  createdAt: 1,
  updatedAt: 1,
}

function renderRoute() {
  return render(<MemoryRouter><SWRConfig value={{ provider: () => new Map(), dedupingInterval: 0 }}><TeamLabDesignRoute /></SWRConfig></MemoryRouter>)
}

describe('TeamLabDesignRoute publish sequence', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(useTeamLabScene).mockReturnValue({ scene })
    vi.mocked(listTeamLabImageOptions).mockResolvedValue([])
    vi.spyOn(teamLabAdminApi, 'listReleases').mockResolvedValue([])
  })

  it('saves the draft, validates that revision, then publishes after confirmation', async () => {
    const steps: string[] = []
    vi.spyOn(teamLabAdminApi, 'updateTopology').mockImplementation(async (_, request) => {
      steps.push('save')
      expect(request.revision).toBe(1)
      return { ...scene, revision: 2, definition: { ...scene.definition, name: 'Updated scene' } }
    })
    vi.spyOn(teamLabAdminApi, 'validateTopology').mockImplementation(async () => {
      steps.push('validate')
      return { valid: true, issues: [] }
    })
    vi.spyOn(teamLabAdminApi, 'publishTopology').mockImplementation(async (_, request) => {
      steps.push('publish')
      expect(request.revision).toBe(2)
      return { id: 'release-id', topologyId: scene.id, version: 1, sourceRevision: 2, schemaVersion: 2, contentHash: 'hash', publishedBy: null, publisherName: null, publishedAt: 1 }
    })
    renderRoute()
    fireEvent.click(await screen.findByRole('button', { name: '修改草稿' }))
    fireEvent.click(screen.getByRole('button', { name: '检查并发布' }))
    const dialog = await screen.findByRole('dialog', { name: '发布当前设计' })
    expect(steps).toEqual(['save', 'validate'])
    fireEvent.click(within(dialog).getByRole('button', { name: '确认发布' }))
    await waitFor(() => expect(steps).toEqual(['save', 'validate', 'publish']))
  })

  it('discards a validation result when the draft changes during the request', async () => {
    vi.spyOn(teamLabAdminApi, 'updateTopology').mockImplementation(async () => ({ ...scene, revision: 2 }))
    let finishValidation: ((result: { valid: boolean; issues: [] }) => void) | undefined
    vi.spyOn(teamLabAdminApi, 'validateTopology').mockImplementation(() => new Promise(resolve => { finishValidation = resolve }))
    const publish = vi.spyOn(teamLabAdminApi, 'publishTopology')
    renderRoute()
    fireEvent.click(await screen.findByRole('button', { name: '检查并发布' }))
    await waitFor(() => expect(finishValidation).toBeDefined())
    fireEvent.click(screen.getByRole('button', { name: '修改草稿' }))
    finishValidation!({ valid: true, issues: [] })
    expect(await screen.findByText('校验期间设计发生变化，请重新检查并发布。')).toBeInTheDocument()
    expect(screen.queryByRole('dialog', { name: '发布当前设计' })).not.toBeInTheDocument()
    expect(publish).not.toHaveBeenCalled()
  })
})
