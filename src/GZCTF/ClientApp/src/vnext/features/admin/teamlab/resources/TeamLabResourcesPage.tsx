import { Plus, Search } from 'lucide-react'
import { useState } from 'react'
import { RuntimeApiError } from '../../api/runtimeJsonClient'
import { ActionButton, InlineFeedback, VNextConfirmDialog } from '../../../../shared/Interaction'
import { DataState } from '../../../../shared/Primitives'
import { errorMessage } from '../../../../shared/errors'
import { useVNextPageTitle } from '../../../../shared/useVNextPageTitle'
import {
  AdminPageHeader,
  CursorPaginationBar,
  DataTable,
  DetailDrawer,
  type AdminDataColumn,
  RefreshIndicator,
  StatusBadge,
  FilterToolbar,
  ToolbarGroup,
} from '../../shared/AdminWorkbench'
import { formatAdminDate } from '../../shared/adminFormat'
import { teamLabResourcesApi } from '../api'
import type {
  TeamLabConnector,
  TeamLabDevicePackage,
  TeamLabNodeCacheEntry,
} from '../api/teamlabResourcesContracts'
import { ConnectorRegisterDialog, DevicePackageRegisterDialog } from './capabilityRegisterDialogs'
import {
  connectorHealthLabels,
  connectorKindLabels,
  deviceArtifactKindLabels,
  toAdminDate,
} from './resourcesPresentation'
import { useConnectorRegistry, useDevicePackageCatalog, useNodeArtifactCache } from './useTeamLabResources'
import styles from './TeamLabResourcesPage.module.css'

type ResourcesTab = 'packages' | 'connectors' | 'cache'

const tabLabels: Record<ResourcesTab, string> = {
  packages: '设备包目录',
  connectors: '现场连接器',
  cache: '节点制品缓存',
}

export function TeamLabResourcesPage() {
  const [tab, setTab] = useState<ResourcesTab>('packages')

  useVNextPageTitle('TeamLab 组网资源')

  return (
    <div className={styles.page}>
      <AdminPageHeader
        description="TeamLab 场景可用的设备包目录、现场连接器与节点制品缓存；镜像内容由外部流水线提供，平台只登记与调度。"
        eyebrow="TEAMLAB RESOURCES"
        title="组网资源"
      />
      <nav aria-label="组网资源分区" className={styles.tabs}>
        {(Object.keys(tabLabels) as ResourcesTab[]).map((key) => (
          <button
            className={styles.tab}
            data-active={tab === key}
            key={key}
            onClick={() => setTab(key)}
            type="button"
          >
            {tabLabels[key]}
          </button>
        ))}
      </nav>
      {tab === 'packages' ? <DevicePackagesTab /> : null}
      {tab === 'connectors' ? <ConnectorsTab /> : null}
      {tab === 'cache' ? <NodeCacheTab /> : null}
    </div>
  )
}

function DevicePackagesTab() {
  const catalog = useDevicePackageCatalog()
  const [registerOpen, setRegisterOpen] = useState(false)
  const [selected, setSelected] = useState<TeamLabDevicePackage | null>(null)
  const [busy, setBusy] = useState(false)
  const [actionError, setActionError] = useState<unknown>(null)
  const [archiveTarget, setArchiveTarget] = useState<TeamLabDevicePackage | null>(null)

  const run = async (action: () => Promise<unknown>) => {
    setBusy(true)
    setActionError(null)
    try {
      await action()
      await catalog.mutate()
    } catch (reason) {
      setActionError(reason)
    } finally {
      setBusy(false)
    }
  }

  const columns: AdminDataColumn<TeamLabDevicePackage>[] = [
    { id: 'name', header: '设备包', render: (row) => <span className={styles.primaryCell}>{row.displayName}</span> },
    { id: 'version', header: '版本', render: (row) => row.version },
    { id: 'kind', header: '制品', render: (row) => deviceArtifactKindLabels[row.artifactKind] },
    { id: 'assets', header: '资产类型', render: (row) => row.supportedAssetKinds.join('、') || '—' },
    { id: 'resources', header: '资源需求', render: (row) => `${row.cpuMillis}m / ${row.memoryMiB}Mi / ${row.storageGib}Gi` },
    {
      id: 'state',
      header: '状态',
      render: (row) => (
        <StatusBadge tone={row.archived ? 'neutral' : row.enabled ? 'success' : 'warning'}>
          {row.archived ? '已归档' : row.enabled ? '启用' : '停用'}
        </StatusBadge>
      ),
    },
    { id: 'updatedAt', header: '更新时间', render: (row) => formatAdminDate(toAdminDate(row.updatedAt)) },
  ]

  return (
    <section aria-label="设备包目录">
      <FilterToolbar>
        <ToolbarGroup grow>
          <label className={styles.searchBox}>
            <Search aria-hidden="true" size={16} />
            <input
              aria-label="搜索设备包"
              onChange={(event) => catalog.setSearchInput(event.currentTarget.value)}
              placeholder="名称"
              type="search"
              value={catalog.searchInput}
            />
          </label>
        </ToolbarGroup>
        <ActionButton icon={<Plus size={16} />} onClick={() => setRegisterOpen(true)} tone="primary" type="button">
          登记设备包
        </ActionButton>
        <RefreshIndicator active={catalog.isRefreshing} label={catalog.isRefreshing ? '正在同步' : '数据已同步'} />
      </FilterToolbar>

      {actionError ? <InlineFeedback tone="danger">{errorMessage(actionError, '设备包操作失败。')}</InlineFeedback> : null}
      {catalog.isLoading ? (
        <DataState description="正在读取设备包目录。" loading title="设备包加载中" />
      ) : catalog.error instanceof RuntimeApiError && catalog.error.status === 403 ? (
        <DataState description="当前账号没有 TeamLab 资源管理权限。" title="无法访问组网资源" />
      ) : catalog.error ? (
        <DataState description={errorMessage(catalog.error, '设备包目录暂不可用。')} title="设备包目录加载失败" />
      ) : (
        <>
          <DataTable
            caption="设备包目录"
            columns={columns}
            emptyDescription="登记外部流水线产出的设备包后，场景资产即可引用。"
            emptyTitle="暂无设备包"
            onRowClick={setSelected}
            rowKey={(row) => row.id}
            rows={[...(catalog.page?.items ?? [])]}
          />
          <CursorPaginationBar
            hasNext={Boolean(catalog.page?.next)}
            label="设备包分页"
            onNext={() => catalog.page?.next && catalog.cursor.next(catalog.page.next)}
            onPrevious={catalog.cursor.previous}
            page={catalog.cursor.page}
          />
        </>
      )}

      <DevicePackageRegisterDialog
        onClose={() => setRegisterOpen(false)}
        onRegistered={() => {
          setRegisterOpen(false)
          void catalog.mutate()
        }}
        open={registerOpen}
      />

      <DetailDrawer
        description={selected?.description ?? '不可变设备包版本的能力与资源声明。'}
        onClose={() => setSelected(null)}
        open={Boolean(selected)}
        title={selected ? `${selected.displayName} · ${selected.version}` : ''}
        footer={
          selected && !selected.archived ? (
            <>
              <ActionButton
                disabled={busy}
                onClick={() => void run(() => teamLabResourcesApi.setDevicePackageEnabled(selected.id, !selected.enabled))}
                type="button"
              >
                {selected.enabled ? '停用' : '启用'}
              </ActionButton>
              <ActionButton
                disabled={busy}
                onClick={() => setArchiveTarget(selected)}
                tone="danger"
                type="button"
              >
                归档
              </ActionButton>
            </>
          ) : null
        }
      >
        {selected ? (
          <dl className={styles.detailList}>
            <div>
              <dt>制品引用</dt>
              <dd className={styles.mono}>{selected.artifactReference}</dd>
            </div>
            <div>
              <dt>摘要</dt>
              <dd className={styles.mono}>{selected.digest ?? '未登记'}</dd>
            </div>
            <div>
              <dt>端口声明</dt>
              <dd>{selected.ports.length > 0 ? selected.ports.map((port) => `${port.name}/${port.port}/${port.protocol}`).join('，') : '—'}</dd>
            </div>
            <div>
              <dt>协议事件类型</dt>
              <dd>{selected.protocolEventTypes.length > 0 ? selected.protocolEventTypes.join('、') : '—'}</dd>
            </div>
            <div>
              <dt>参数 schema</dt>
              <dd>
                <pre className={styles.jsonBlock}>{JSON.stringify(selected.parameterSchema, null, 2)}</pre>
              </dd>
            </div>
            <div>
              <dt>健康声明</dt>
              <dd>
                <pre className={styles.jsonBlock}>{JSON.stringify(selected.healthDeclaration, null, 2)}</pre>
              </dd>
            </div>
          </dl>
        ) : null}
      </DetailDrawer>

      <VNextConfirmDialog
        confirmLabel="归档"
        description="归档后设备包不再出现在场景资产选择中，已引用它的历史版本保持可读。"
        message={`确认归档设备包 ${archiveTarget?.displayName ?? ''} ${archiveTarget?.version ?? ''}？`}
        onClose={() => setArchiveTarget(null)}
        onConfirm={() => {
          const target = archiveTarget
          setArchiveTarget(null)
          if (target) void run(() => teamLabResourcesApi.archiveDevicePackage(target.id))
        }}
        open={Boolean(archiveTarget)}
        title="归档设备包"
      />
    </section>
  )
}

function ConnectorsTab() {
  const registry = useConnectorRegistry()
  const [registerOpen, setRegisterOpen] = useState(false)
  const [selected, setSelected] = useState<TeamLabConnector | null>(null)
  const [busy, setBusy] = useState(false)
  const [actionError, setActionError] = useState<unknown>(null)
  const [archiveTarget, setArchiveTarget] = useState<TeamLabConnector | null>(null)

  const run = async (action: () => Promise<unknown>) => {
    setBusy(true)
    setActionError(null)
    try {
      await action()
      await registry.mutate()
    } catch (reason) {
      setActionError(reason)
    } finally {
      setBusy(false)
    }
  }

  const columns: AdminDataColumn<TeamLabConnector>[] = [
    { id: 'name', header: '连接器', render: (row) => <span className={styles.primaryCell}>{row.displayName}</span> },
    { id: 'kind', header: '类型', render: (row) => connectorKindLabels[row.kind] },
    { id: 'scope', header: '授权范围', render: (row) => (row.controlScopeId ? '指定范围' : '平台级') },
    {
      id: 'occupancy',
      header: '占用',
      render: (row) => `${row.occupiedSlots} / ${row.capacity}${row.supportsSharedUse ? '（共享）' : ''}`,
    },
    {
      id: 'health',
      header: '健康',
      render: (row) => (
        <StatusBadge tone={connectorHealthLabels[row.health].tone}>{connectorHealthLabels[row.health].label}</StatusBadge>
      ),
    },
    { id: 'updatedAt', header: '更新时间', render: (row) => formatAdminDate(toAdminDate(row.updatedAt)) },
  ]

  return (
    <section aria-label="现场连接器">
      <FilterToolbar>
        <ToolbarGroup>
          <span className={styles.toolbarHint}>独占连接器同一时间只属于一个运行环境；占用事实在节点失联期间保持。</span>
        </ToolbarGroup>
        <ActionButton icon={<Plus size={16} />} onClick={() => setRegisterOpen(true)} tone="primary" type="button">
          登记连接器
        </ActionButton>
        <RefreshIndicator active={registry.isRefreshing} label={registry.isRefreshing ? '正在同步' : '数据已同步'} />
      </FilterToolbar>

      {actionError ? <InlineFeedback tone="danger">{errorMessage(actionError, '连接器操作失败。')}</InlineFeedback> : null}
      {registry.isLoading ? (
        <DataState description="正在读取现场连接器。" loading title="连接器加载中" />
      ) : registry.error ? (
        <DataState description={errorMessage(registry.error, '连接器目录暂不可用。')} title="连接器目录加载失败" />
      ) : (
        <>
          <DataTable
            caption="现场连接器"
            columns={columns}
            emptyDescription="登记现场资源后，场景资产即可按 ID 引用。"
            emptyTitle="暂无现场连接器"
            onRowClick={setSelected}
            rowKey={(row) => row.id}
            rows={[...(registry.page?.items ?? [])]}
          />
          <CursorPaginationBar
            hasNext={Boolean(registry.page?.next)}
            label="连接器分页"
            onNext={() => registry.page?.next && registry.cursor.next(registry.page.next)}
            onPrevious={registry.cursor.previous}
            page={registry.cursor.page}
          />
        </>
      )}

      <ConnectorRegisterDialog
        onClose={() => setRegisterOpen(false)}
        onRegistered={() => {
          setRegisterOpen(false)
          void registry.mutate()
        }}
        open={registerOpen}
      />

      <DetailDrawer
        description={selected?.description ?? '连接器占用与健康状态。'}
        onClose={() => setSelected(null)}
        open={Boolean(selected)}
        title={selected?.displayName ?? ''}
        footer={
          selected && !selected.archived ? (
            <>
              <ActionButton
                disabled={busy || selected.health === 'unreachable'}
                onClick={() =>
                  selected && void run(() => teamLabResourcesApi.setConnectorHealth(selected.id, 'unreachable'))
                }
                tone="danger"
                type="button"
              >
                标记不可达
              </ActionButton>
              <ActionButton
                disabled={busy || selected.health === 'healthy'}
                onClick={() => selected && void run(() => teamLabResourcesApi.setConnectorHealth(selected.id, 'healthy'))}
                type="button"
              >
                标记健康
              </ActionButton>
              <ActionButton disabled={busy} onClick={() => setArchiveTarget(selected)} tone="danger" type="button">
                归档
              </ActionButton>
            </>
          ) : null
        }
      >
        {selected ? (
          <div className={styles.connectorDetail}>
            <dl className={styles.detailList}>
              <div>
                <dt>连接器 ID</dt>
                <dd className={styles.mono}>{selected.id}</dd>
              </div>
              <div>
                <dt>健康观察时间</dt>
                <dd>{selected.healthObservedAt ? formatAdminDate(toAdminDate(selected.healthObservedAt)) : '尚未上报'}</dd>
              </div>
            </dl>
            <h4 className={styles.subheading}>活动租约</h4>
            {selected.activeLeases.length === 0 ? (
              <p className={styles.muted}>当前没有运行环境占用。</p>
            ) : (
              <ul className={styles.leaseList}>
                {selected.activeLeases.map((lease) => (
                  <li key={lease.id}>
                    <span className={styles.mono}>{lease.runtimeId}</span>
                    <span>槽位 {lease.slot}</span>
                    <span>{formatAdminDate(toAdminDate(lease.acquiredAt))} 起占用</span>
                  </li>
                ))}
              </ul>
            )}
          </div>
        ) : null}
      </DetailDrawer>

      <VNextConfirmDialog
        confirmLabel="归档"
        description="仍有活动租约的连接器无法归档。"
        message={`确认归档连接器 ${archiveTarget?.displayName ?? ''}？`}
        onClose={() => setArchiveTarget(null)}
        onConfirm={() => {
          const target = archiveTarget
          setArchiveTarget(null)
          if (target) void run(() => teamLabResourcesApi.archiveConnector(target.id))
        }}
        open={Boolean(archiveTarget)}
        title="归档连接器"
      />
    </section>
  )
}

function NodeCacheTab() {
  const cache = useNodeArtifactCache()

  const columns: AdminDataColumn<TeamLabNodeCacheEntry>[] = [
    { id: 'template', header: '模板', render: (row) => `#${row.templateId}` },
    { id: 'node', header: '节点', render: (row) => <span className={styles.mono}>{row.nodeId}</span> },
    { id: 'status', header: '状态', render: (row) => row.status },
    { id: 'operation', header: '操作', render: (row) => (row.operation === 'distribute' ? '分发' : '回收') },
    { id: 'stage', header: '阶段', render: (row) => row.stage },
    { id: 'references', header: '用途引用', render: (row) => row.activeReferenceCount },
    { id: 'updatedAt', header: '更新时间', render: (row) => formatAdminDate(toAdminDate(row.progressUpdatedAt)) },
  ]

  return (
    <section aria-label="节点制品缓存">
      <FilterToolbar>
        <ToolbarGroup>
          <span className={styles.toolbarHint}>镜像在节点的分发状态与用途引用计数；最后一个引用释放后才会物理回收。</span>
        </ToolbarGroup>
        <RefreshIndicator active={cache.isRefreshing} label={cache.isRefreshing ? '正在同步' : '数据已同步'} />
      </FilterToolbar>
      {cache.isLoading ? (
        <DataState description="正在读取节点制品缓存。" loading title="节点缓存加载中" />
      ) : cache.error ? (
        <DataState description={errorMessage(cache.error, '节点缓存暂不可用。')} title="节点缓存加载失败" />
      ) : (
        <>
          <DataTable
            caption="节点制品缓存"
            columns={columns}
            emptyDescription="还没有模板分发到执行节点。"
            emptyTitle="暂无缓存记录"
            rowKey={(row) => `${row.templateId}:${row.nodeId}`}
            rows={[...(cache.page?.items ?? [])]}
          />
          <CursorPaginationBar
            hasNext={Boolean(cache.page?.next)}
            label="节点缓存分页"
            onNext={() => cache.page?.next && cache.cursor.next(cache.page.next)}
            onPrevious={cache.cursor.previous}
            page={cache.cursor.page}
          />
        </>
      )}
    </section>
  )
}
