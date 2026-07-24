import { Eye, RefreshCw, Search } from 'lucide-react'
import { ChangeEvent, useEffect, useMemo, useState } from 'react'
import { useSWRConfig } from 'swr'
import { ActionButton, InlineFeedback, VNextConfirmDialog } from '../../../shared/Interaction'
import { DataState } from '../../../shared/Primitives'
import { instanceAdminApi, type GlobalInstanceItem } from '../api'
import { useAdminNodes } from '../nodes/useAdminNodes'
import { formatAdminDate } from '../shared/adminFormat'
import {
  AdminPageHeader,
  DataTable,
  FilterToolbar,
  MetricItem,
  MetricStrip,
  PaginationBar,
  RefreshIndicator,
  StatusBadge,
  ToolbarGroup,
  type AdminDataColumn,
} from '../shared/AdminWorkbench'
import { useAdminQueryState } from '../shared/useAdminQueryState'
import styles from './AdminInstancesPage.module.css'
import { InstanceDetailDrawer } from './InstanceDetailDrawer'
import {
  canDestroyInstance,
  instanceContextLabel,
  instanceEntryLabel,
  instanceKindLabel,
  instanceOwnerLabel,
  instanceStatusMeta,
} from './instancePresentation'
import { useAdminInstances } from './useAdminInstances'

const PAGE_SIZE = 20

export function AdminInstancesPage() {
  const queryState = useAdminQueryState(PAGE_SIZE)
  const nodesRequest = useAdminNodes()
  const swr = useSWRConfig()
  const status = queryState.params.get('status') === 'history' ? 'history' : 'active'
  const nodeId = queryState.params.get('node') || undefined
  const type = queryState.params.get('type') ?? 'all'
  const instancesRequest = useAdminInstances(nodesRequest.nodes, nodesRequest.error, { status, nodeId })
  const [query, setQuery] = useState('')
  const [selectedKey, setSelectedKey] = useState<string | null>(null)
  const [destroyTarget, setDestroyTarget] = useState<GlobalInstanceItem | null>(null)
  const [destroyPendingKeys, setDestroyPendingKeys] = useState<Set<string>>(() => new Set())
  const [failure, setFailure] = useState<string | null>(null)
  const items = instancesRequest.inventory?.items ?? []

  const resourceKey = (instance: GlobalInstanceItem) => `${instance.nodeId}:${instance.kind}:${instance.id}`

  useEffect(() => {
    if (!instancesRequest.inventory) return
    setDestroyPendingKeys((current) => {
      const activeKeys = new Set(instancesRequest.inventory?.items.filter((item) => item.isActive).map(resourceKey))
      const next = new Set([...current].filter((key) => activeKeys.has(key)))
      if (next.size === current.size && [...next].every((key) => current.has(key))) return current
      return next
    })
  }, [instancesRequest.inventory])

  const filtered = useMemo(() => {
    const normalized = query.trim().toLowerCase()
    return items.filter((instance) => {
      if (type !== 'all' && instance.kind !== type) return false
      if (!normalized) return true
      return [
        instance.name,
        instanceOwnerLabel(instance),
        instanceContextLabel(instance),
        instance.nodeName,
        instance.image,
        instanceEntryLabel(instance),
      ]
        .filter(Boolean)
        .some((value) => value?.toLowerCase().includes(normalized))
    })
  }, [items, query, type])
  const pageCount = Math.max(1, Math.ceil(filtered.length / PAGE_SIZE))
  const page = Math.min(queryState.page, pageCount)
  const visible = filtered.slice((page - 1) * PAGE_SIZE, page * PAGE_SIZE)

  const metrics = useMemo(
    () => ({
      total: items.length,
      containers: items.filter((item) => item.kind === 'container').length,
      vms: items.filter((item) => item.kind === 'vm').length,
      pentest: items.filter((item) => item.kind === 'pentest').length,
      teamLab: items.filter((item) => item.kind === 'teamlab').length,
    }),
    [items]
  )

  const columns: AdminDataColumn<GlobalInstanceItem>[] = [
    {
      id: 'instance',
      header: '实例',
      width: 'wide',
      render: (instance) => (
        <div className={styles.instanceName}>
          <strong>{instance.name}</strong>
          <small>
            {instanceKindLabel(instance.kind)} · {instanceOwnerLabel(instance)}
          </small>
        </div>
      ),
    },
    {
      id: 'context',
      header: '业务上下文',
      width: 'wide',
      visibility: 'desktop',
      render: (instance) => <span className={styles.ellipsis}>{instanceContextLabel(instance)}</span>,
    },
    {
      id: 'node',
      header: '节点',
      width: 'medium',
      render: (instance) => <span className={styles.ellipsis}>{instance.nodeName}</span>,
    },
    {
      id: 'entry',
      header: '入口',
      width: 'wide',
      visibility: 'wide',
      render: (instance) =>
        instance.entry?.startsWith('http') ? (
          <a className={styles.tableLink} href={instance.entry} rel="noopener noreferrer" target="_blank">
            {instanceEntryLabel(instance)}
          </a>
        ) : (
          <span className={styles.mono}>{instanceEntryLabel(instance)}</span>
        ),
    },
    {
      id: 'expiry',
      header: '到期时间',
      width: 'medium',
      visibility: 'desktop',
      render: (instance) => <span className={styles.mono}>{formatAdminDate(instance.expectedStopAt, false)}</span>,
    },
    {
      id: 'status',
      header: '状态',
      width: 'compact',
      render: (instance) => {
        const meta = instanceStatusMeta(instance)
        const pending = destroyPendingKeys.has(resourceKey(instance))
        return (
          <StatusBadge pulse={instance.isActive} tone={pending ? 'warning' : meta.tone}>
            {pending ? '销毁确认中' : meta.label}
          </StatusBadge>
        )
      },
    },
    {
      id: 'action',
      header: '操作',
      width: 'compact',
      align: 'right',
      render: (instance) => (
        <button
          aria-label={`查看 ${instance.name}`}
          className={styles.iconButton}
          onClick={() => setSelectedKey(resourceKey(instance))}
          type="button"
        >
          <Eye size={16} />
        </button>
      ),
    },
  ]

  const refresh = async () => {
    await Promise.all([nodesRequest.mutate(), instancesRequest.mutate()])
  }

  const destroy = async () => {
    if (!destroyTarget) return false
    const key = resourceKey(destroyTarget)
    setFailure(null)
    setDestroyPendingKeys((current) => new Set(current).add(key))
    try {
      await instanceAdminApi.destroy(destroyTarget)
      await Promise.all([
        instancesRequest.mutate(),
        nodesRequest.mutate(),
        swr.mutate(
          (cacheKey) =>
            Array.isArray(cacheKey) &&
            cacheKey[0] === 'vnext:admin:node-resources' &&
            cacheKey[1] === destroyTarget.nodeId
        ),
      ])
      return true
    } catch (error) {
      setDestroyPendingKeys((current) => {
        const next = new Set(current)
        next.delete(key)
        return next
      })
      setFailure(error instanceof Error ? error.message : '实例销毁失败。')
      return false
    }
  }

  const selected = items.find((item) => resourceKey(item) === selectedKey) ?? null
  const historyNeedsNode = status === 'history' && !nodeId

  return (
    <div className={styles.page}>
      <AdminPageHeader
        actions={
          <ActionButton icon={<RefreshCw size={16} />} onClick={() => void refresh()} type="button">
            刷新资源
          </ActionButton>
        }
        description="跨节点查看 Docker、VM、渗透环境与 TeamLab 运行资源。"
        eyebrow="RUNTIME INVENTORY"
        title="运行实例"
      />

      <MetricStrip>
        <MetricItem
          detail={
            instancesRequest.inventory?.source === 'legacy-containers'
              ? '仅传统比赛容器'
              : `已读取 ${instancesRequest.inventory?.loadedNodes ?? 0}/${instancesRequest.inventory?.totalNodes ?? 0} 个节点`
          }
          label={status === 'active' ? '活跃实例' : '历史实例'}
          tone={metrics.total ? 'success' : 'neutral'}
          value={metrics.total}
        />
        <MetricItem label="Docker" value={metrics.containers} />
        <MetricItem label="VM" value={metrics.vms} />
        <MetricItem label="渗透环境" value={metrics.pentest} />
        <MetricItem label="TeamLab" value={metrics.teamLab} />
      </MetricStrip>

      <FilterToolbar>
        <ToolbarGroup grow>
          <label className={styles.searchBox}>
            <Search aria-hidden="true" size={16} />
            <input
              aria-label="搜索运行实例"
              onChange={(event: ChangeEvent<HTMLInputElement>) => setQuery(event.currentTarget.value)}
              placeholder="搜索实例、所有者、节点或入口"
              type="search"
              value={query}
            />
          </label>
          <select
            aria-label="实例状态范围"
            onChange={(event) => queryState.update({ status: event.currentTarget.value === 'active' ? null : 'history' })}
            value={status}
          >
            <option value="active">运行中</option>
            <option value="history">历史记录</option>
          </select>
          <select
            aria-label="实例类型"
            onChange={(event) => queryState.update({ type: event.currentTarget.value === 'all' ? null : event.currentTarget.value })}
            value={type}
          >
            <option value="all">全部类型</option>
            <option value="container">Docker</option>
            <option value="vm">VM</option>
            <option value="pentest">渗透环境</option>
            <option value="teamlab">TeamLab</option>
          </select>
          <select
            aria-label="所属节点"
            onChange={(event) => queryState.update({ node: event.currentTarget.value || null })}
            value={nodeId ?? ''}
          >
            <option value="">{status === 'history' ? '请选择节点' : '全部节点'}</option>
            {(nodesRequest.nodes ?? []).map((node) => (
              <option key={node.id} value={node.id}>
                {node.name}
              </option>
            ))}
          </select>
        </ToolbarGroup>
        <RefreshIndicator active={instancesRequest.isRefreshing} label={status === 'active' ? '10 秒自动同步' : '按需读取'} />
      </FilterToolbar>

      {instancesRequest.inventory?.source === 'legacy-containers' ? (
        <InlineFeedback>降级视图：节点资源接口不可用，当前仅显示传统比赛 Docker 容器。</InlineFeedback>
      ) : null}
      {instancesRequest.inventory?.failures.length ? (
        <InlineFeedback tone="danger">
          {instancesRequest.inventory.failures.length} 个节点读取失败，当前统计只覆盖已成功读取的节点。
        </InlineFeedback>
      ) : null}
      {failure ? <InlineFeedback tone="danger">{failure}</InlineFeedback> : null}

      {historyNeedsNode ? (
        <DataState description="全域历史接口尚未提供，请先在节点筛选中选择一个节点。" title="选择节点查看历史" />
      ) : instancesRequest.isLoading || nodesRequest.isLoading ? (
        <DataState description="正在以受控并发汇总各节点运行资源。" loading title="运行实例加载中" />
      ) : instancesRequest.error ? (
        <DataState description="节点资源与传统容器接口均无法提供当前视图。" title="运行实例加载失败" />
      ) : (
        <>
          <DataTable
            caption="全域运行实例"
            columns={columns}
            emptyDescription="调整状态、类型、节点或关键词后重试。"
            emptyTitle="没有符合条件的运行实例"
            onRowClick={(instance) => setSelectedKey(resourceKey(instance))}
            rowKey={resourceKey}
            rows={visible}
          />
          <PaginationBar onPageChange={queryState.setPage} page={page} pageCount={pageCount} total={filtered.length} />
        </>
      )}

      <InstanceDetailDrawer
        destroyPending={selected ? destroyPendingKeys.has(resourceKey(selected)) : false}
        instance={selected}
        onClose={() => setSelectedKey(null)}
        onDestroy={() => selected && canDestroyInstance(selected) && setDestroyTarget(selected)}
      />

      <VNextConfirmDialog
        confirmLabel="销毁实例"
        confirmationText={destroyTarget?.name}
        description="实例会保留到节点确认终态"
        message={`将终止 ${destroyTarget ? instanceKindLabel(destroyTarget.kind) : '运行资源'}，并释放节点 ${destroyTarget?.nodeName || '未知'} 上的运行容量。`}
        onClose={() => setDestroyTarget(null)}
        onConfirm={destroy}
        open={Boolean(destroyTarget)}
        title={`销毁 ${destroyTarget?.name ?? '运行实例'}？`}
      />
    </div>
  )
}
