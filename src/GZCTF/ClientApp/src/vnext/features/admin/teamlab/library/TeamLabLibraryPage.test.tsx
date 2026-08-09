import { render, screen, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { RuntimeApiError } from '../../api/runtimeJsonClient'
import type { TeamLabAdminSceneSummary } from '../api'
import { TeamLabLibraryPage } from './TeamLabLibraryPage'
import { useTeamLabCatalog } from './useTeamLabCatalog'

vi.mock('./useTeamLabCatalog', () => ({ useTeamLabCatalog: vi.fn() }))

const scene: TeamLabAdminSceneSummary = {
  id: '019f0000-0000-7000-8000-000000000001',
  name: '企业域演练',
  ownerId: '019f0000-0000-7000-8000-000000000002',
  ownerDisplayName: 'Teacher A',
  revision: 4,
  schemaVersion: 2,
  networkCount: 3,
  assetCount: 8,
  infrastructureCount: 2,
  latestRelease: {
    id: '019f0000-0000-7000-8000-000000000003',
    version: 2,
    sourceRevision: 4,
    contentHash: '0123456789abcdef',
    publishedAt: 1_784_918_400_000,
  },
  validation: { revision: 4, valid: true, issueCount: 0, validatedAt: 1_784_918_400_000 },
  latestTrialRuntime: null,
  gameReferenceCount: 1,
  createdAt: 1_784_832_000_000,
  updatedAt: 1_784_918_400_000,
}

function catalog(overrides: Partial<ReturnType<typeof useTeamLabCatalog>> = {}): ReturnType<typeof useTeamLabCatalog> {
  return {
    page: { items: [scene], nextCursor: null },
    error: undefined,
    isLoading: false,
    isRefreshing: false,
    mutate: vi.fn(),
    searchInput: '',
    setSearchInput: vi.fn(),
    status: '',
    setStatus: vi.fn(),
    owner: '',
    setOwner: vi.fn(),
    cursor: {
      cursor: null,
      page: 1,
      canGoBack: false,
      next: vi.fn(),
      previous: vi.fn(),
      reset: vi.fn(),
    },
    ...overrides,
  }
}

describe('TeamLabLibraryPage', () => {
  beforeEach(() => vi.mocked(useTeamLabCatalog).mockReturnValue(catalog()))

  it('renders the server-ordered scene projection', () => {
    render(<MemoryRouter><TeamLabLibraryPage /></MemoryRouter>)

    expect(screen.getByRole('heading', { name: '组网场景库' })).toBeInTheDocument()
    const row = screen.getByRole('row', { name: /企业域演练/ })
    expect(row).toHaveTextContent('3 网段 · 8 资产 · 2 设施')
    expect(within(row).getByText('已发布')).toBeInTheDocument()
  })

  it('renders an explicit empty state without inventing local rows', () => {
    vi.mocked(useTeamLabCatalog).mockReturnValue(catalog({ page: { items: [], nextCursor: null } }))
    render(<MemoryRouter><TeamLabLibraryPage /></MemoryRouter>)

    expect(screen.getByText('没有匹配的组网场景')).toBeInTheDocument()
  })

  it('distinguishes permission failures from empty data', () => {
    vi.mocked(useTeamLabCatalog).mockReturnValue(
      catalog({ page: undefined, error: new RuntimeApiError('Forbidden', { kind: 'http', status: 403 }) })
    )
    render(<MemoryRouter><TeamLabLibraryPage /></MemoryRouter>)

    expect(screen.getByText('无法访问场景库')).toBeInTheDocument()
    expect(screen.queryByText('没有匹配的组网场景')).not.toBeInTheDocument()
  })
})
