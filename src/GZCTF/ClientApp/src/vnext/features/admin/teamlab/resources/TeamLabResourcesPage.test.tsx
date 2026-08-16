import { fireEvent, render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { TeamLabConnector, TeamLabDevicePackage } from '../api'
import { TeamLabResourcesPage } from './TeamLabResourcesPage'
import { useConnectorRegistry, useDevicePackageCatalog, useNodeArtifactCache } from './useTeamLabResources'

vi.mock('./useTeamLabResources', () => ({
  useDevicePackageCatalog: vi.fn(),
  useConnectorRegistry: vi.fn(),
  useNodeArtifactCache: vi.fn(),
}))

const devicePackage: TeamLabDevicePackage = {
  id: '019f0000-0000-7000-8000-0000000000a1',
  name: 'plc-simulator',
  displayName: 'PLC 模拟器',
  version: '1.2.0',
  artifactKind: 'oci-image',
  artifactReference: 'registry.example.com/yinyu/plc-simulator:1.2.0',
  digest: 'sha256:' + 'a'.repeat(64),
  description: 'Modbus/TCP 协议仿真',
  supportedAssetKinds: ['docker'],
  cpuMillis: 500,
  memoryMiB: 256,
  storageGib: 4,
  ports: [{ name: 'modbus', port: 502, protocol: 'tcp' }],
  parameterSchema: { type: 'object' },
  healthDeclaration: { kind: 'tcp', port: 502 },
  protocolEventTypes: ['modbus-read'],
  enabled: true,
  archived: false,
  createdAt: '2026-08-17T08:00:00Z',
  updatedAt: '2026-08-17T08:00:00Z',
}

const connector: TeamLabConnector = {
  id: '019f0000-0000-7000-8000-0000000000c1',
  name: 'field-vlan-1',
  displayName: '现场 VLAN 1',
  kind: 'vlan',
  controlScopeId: null,
  supportsSharedUse: false,
  capacity: 1,
  occupiedSlots: 1,
  activeLeases: [
    {
      id: '019f0000-0000-7000-8000-0000000000d1',
      connectorId: '019f0000-0000-7000-8000-0000000000c1',
      runtimeId: '019f0000-0000-7000-8000-0000000000e1',
      slot: 1,
      acquiredAt: '2026-08-17T07:00:00Z',
      releasedAt: null,
      releaseReason: 'none',
    },
  ],
  health: 'healthy',
  healthObservedAt: '2026-08-17T07:30:00Z',
  description: null,
  archived: false,
  createdAt: '2026-08-17T06:00:00Z',
  updatedAt: '2026-08-17T07:30:00Z',
}

function cursorState() {
  return {
    cursor: null,
    page: 1,
    canGoBack: false,
    next: vi.fn(),
    previous: vi.fn(),
    reset: vi.fn(),
  }
}

function catalog(overrides: Record<string, unknown> = {}) {
  return {
    page: { items: [devicePackage], next: null },
    error: undefined,
    isLoading: false,
    isRefreshing: false,
    mutate: vi.fn(),
    searchInput: '',
    setSearchInput: vi.fn(),
    cursor: cursorState(),
    ...overrides,
  }
}

function registry(overrides: Record<string, unknown> = {}) {
  return {
    page: { items: [connector], next: null },
    error: undefined,
    isLoading: false,
    isRefreshing: false,
    mutate: vi.fn(),
    cursor: cursorState(),
    ...overrides,
  }
}

function cache(overrides: Record<string, unknown> = {}) {
  return {
    page: {
      items: [
        {
          templateId: 7,
          nodeId: '019f0000-0000-7000-8000-0000000000f1',
          imageHash: null,
          status: 'ready',
          operation: 'distribute',
          stage: 'verifying',
          attemptCount: 1,
          activeReferenceCount: 2,
          lastErrorCode: null,
          progressUpdatedAt: '2026-08-17T07:00:00Z',
        },
      ],
      next: null,
    },
    error: undefined,
    isLoading: false,
    isRefreshing: false,
    mutate: vi.fn(),
    cursor: cursorState(),
    ...overrides,
  }
}

describe('TeamLabResourcesPage', () => {
  beforeEach(() => {
    vi.mocked(useDevicePackageCatalog).mockReturnValue(catalog() as ReturnType<typeof useDevicePackageCatalog>)
    vi.mocked(useConnectorRegistry).mockReturnValue(registry() as ReturnType<typeof useConnectorRegistry>)
    vi.mocked(useNodeArtifactCache).mockReturnValue(cache() as ReturnType<typeof useNodeArtifactCache>)
  })

  it('renders the device package catalog with capability summary', () => {
    render(<TeamLabResourcesPage />)

    expect(screen.getByRole('heading', { name: '组网资源' })).toBeInTheDocument()
    const row = screen.getByRole('row', { name: /PLC 模拟器/ })
    expect(row).toHaveTextContent('1.2.0')
    expect(row).toHaveTextContent('OCI 镜像')
    expect(row).toHaveTextContent('启用')
    expect(screen.getByRole('button', { name: '登记设备包' })).toBeInTheDocument()
  })

  it('switches to the connector tab and exposes occupancy without endpoints', () => {
    render(<TeamLabResourcesPage />)
    fireEvent.click(screen.getByRole('button', { name: '现场连接器' }))

    const row = screen.getByRole('row', { name: /现场 VLAN 1/ })
    expect(row).toHaveTextContent('1 / 1')
    expect(row).toHaveTextContent('健康')
    expect(screen.queryByText('10.0.7.125')).not.toBeInTheDocument()
  })

  it('switches to the node artifact cache tab with reference counts', () => {
    render(<TeamLabResourcesPage />)
    fireEvent.click(screen.getByRole('button', { name: '节点制品缓存' }))

    expect(screen.getByRole('row', { name: /#7/ })).toHaveTextContent('2')
  })

  it('separates empty catalog from load failure', () => {
    vi.mocked(useDevicePackageCatalog).mockReturnValue(
      catalog({ page: { items: [], next: null } }) as ReturnType<typeof useDevicePackageCatalog>
    )
    render(<TeamLabResourcesPage />)

    expect(screen.getByText('暂无设备包')).toBeInTheDocument()
  })
})
