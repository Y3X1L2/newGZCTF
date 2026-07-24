import { ExternalLink, Eye, Search, Trash2 } from 'lucide-react'
import { useState } from 'react'
import { ActionButton, InlineFeedback, VNextConfirmDialog } from '../../../shared/Interaction'
import { DataState } from '../../../shared/Primitives'
import { errorMessage } from '../../../shared/errors'
import { instanceAdminApi, nodeAdminApi, type NodeResourceItem } from '../api'
import {
  DataTable,
  DetailDrawer,
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
import styles from './NodeResourceTab.module.css'
import { useNodeResources } from './useAdminNodes'

const PAGE_SIZE = 12

function kindLabel(kind: string) {
  if (kind === 'container') return 'Docker'
  if (kind === 'vm') return 'VM'
  if (kind === 'pentest') return '渗透环境'
  if (kind === 'teamlab') return 'TeamLab'
  return kind
}

function contextLabel(resource: NodeResourceItem) {
  return (
    [resource.gameTitle, resource.challengeTitle, resource.teamName || resource.userName].filter(Boolean).join(' / ') ||
    '无业务上下文'
  )
}

export function NodeResourceTab({ nodeId, onMutated }: { nodeId: string; onMutated: () => void | Promise<void> }) {
  const queryState = useAdminQueryState(PAGE_SIZE)
  const [selected, setSelected] = useState<NodeResourceItem | null>(null)
  const [destroyTarget, setDestroyTarget] = useState<NodeResourceItem | null>(null)
  const [failure, setFailure] = useState<string | null>(null)
  const type = queryState.params.get('type') ?? 'all'
  const status = queryState.params.get('status') ?? 'all'
  const { resources, error, isLoading, isRefreshing, mutate } = useNodeResources(nodeId, {
    type,
    status,
    page: queryState.page,
    pageSize: PAGE_SIZE,
  })
  const pageCount = Math.max(1, Math.ceil((resources?.total ?? 0) / PAGE_SIZE))
  const page = Math.min(queryState.page, pageCount)

  const columns: AdminDataColumn<NodeResourceItem>[] = [
    {
      id: 'resource',
      header: '资源',
      width: 'wide',
      render: (resource) => (
        <div className={styles.resourceName}>
          <strong>{resource.name}</strong>
          <small>
            {kindLabel(resource.kind)} · {resource.providerName || resource.runtimeId || resource.id}
          </small>
        </div>
      ),
    },
    {
      id: 'context',
      header: '业务上下文',
      width: 'wide',
      visibility: 'desktop',
      render: (resource) => <span className={styles.context}>{contextLabel(resource)}</span>,
    },
    {
      id: 'status',
      header: '状态',
      width: 'compact',
      render: (resource) => (
        <StatusBadge tone={resource.isActive ? 'success' : 'neutral'}>{resource.status}</StatusBadge>
      ),
    },
    {
      id: 'address',
      header: '入口',
      width: 'wide',
      visibility: 'wide',
      render: (resource) =>
        resource.entry ? (
          <a className={styles.entry} href={resource.entry} rel="noreferrer" target="_blank">
            {resource.entry}
            <ExternalLink size={13} />
          </a>
        ) : (
          <span className={styles.mono}>
            {resource.ip || '—'}
            {resource.port ? `:${resource.port}` : ''}
          </span>
        ),
    },
    {
      id: 'duration',
      header: '运行时长',
      width: 'medium',
      visibility: 'desktop',
      render: (resource) => <span className={styles.mono}>{resource.duration || '—'}</span>,
    },
    {
      id: 'action',
      header: '操作',
      width: 'compact',
      align: 'right',
      render: (resource) => (
        <button
          aria-label={`查看 ${resource.name}`}
          className={styles.iconButton}
          onClick={() => setSelected(resource)}
          type="button"
        >
          <Eye size={16} />
        </button>
      ),
    },
  ]

  const destroy = async () => {
    if (!destroyTarget) return false
    setFailure(null)
    try {
      if (destroyTarget.kind === 'container') await instanceAdminApi.destroyContainer(destroyTarget.id)
      else if (destroyTarget.kind === 'vm') await nodeAdminApi.destroyVm(destroyTarget.id)
      else throw new Error('该资源类型暂不支持从统一节点页面销毁。')
      setDestroyTarget(null)
      setSelected(null)
      await mutate()
      await onMutated()
      return true
    } catch (destroyError) {
      setFailure(errorMessage(destroyError, '运行资源销毁失败。'))
      return false
    }
  }

  return (
    <div className={styles.tab}>
      {resources ? (
        <MetricStrip>
          <MetricItem
            label="运行中"
            tone={resources.runningCount ? 'success' : 'neutral'}
            value={resources.runningCount}
          />
          <MetricItem label="Docker" value={resources.containerCount} />
          <MetricItem label="VM" value={resources.vmCount} />
          <MetricItem label="渗透环境" value={resources.pentestCount} />
          <MetricItem label="TeamLab" value={resources.teamLabCount} />
        </MetricStrip>
      ) : null}

      <FilterToolbar>
        <ToolbarGroup grow>
          <label className={styles.filterMark}>
            <Search size={15} />
            资源筛选
          </label>
          <select
            aria-label="资源类型"
            onChange={(event) =>
              queryState.update({ type: event.currentTarget.value === 'all' ? null : event.currentTarget.value })
            }
            value={type}
          >
            <option value="all">全部类型</option>
            <option value="container">Docker</option>
            <option value="vm">VM</option>
            <option value="pentest">渗透环境</option>
            <option value="teamlab">TeamLab</option>
          </select>
          <select
            aria-label="资源状态"
            onChange={(event) =>
              queryState.update({ status: event.currentTarget.value === 'all' ? null : event.currentTarget.value })
            }
            value={status}
          >
            <option value="all">全部状态</option>
            <option value="active">运行中</option>
            <option value="history">历史记录</option>
          </select>
        </ToolbarGroup>
        <RefreshIndicator active={isRefreshing} label="10 秒自动同步" />
      </FilterToolbar>

      {failure ? <InlineFeedback tone="danger">{failure}</InlineFeedback> : null}

      {isLoading ? (
        <DataState description="正在汇总节点上的容器、VM 和场景资源。" loading title="资源加载中" />
      ) : error ? (
        <DataState description="节点资源接口暂时不可用。" title="资源加载失败" />
      ) : (
        <>
          <DataTable
            caption="节点运行资源"
            columns={columns}
            emptyDescription="切换资源类型或状态后重试。"
            emptyTitle="该节点没有符合条件的资源"
            onRowClick={setSelected}
            rowKey={(resource) => `${resource.kind}:${resource.id}`}
            rows={resources?.items ?? []}
          />
          <PaginationBar
            onPageChange={queryState.setPage}
            page={page}
            pageCount={pageCount}
            total={resources?.total ?? 0}
          />
        </>
      )}

      <DetailDrawer
        description={selected ? `${kindLabel(selected.kind)} · ${selected.status}` : undefined}
        footer={
          selected?.isActive && (selected.kind === 'container' || selected.kind === 'vm') ? (
            <ActionButton
              icon={<Trash2 size={16} />}
              onClick={() => setDestroyTarget(selected)}
              tone="danger"
              type="button"
            >
              销毁资源
            </ActionButton>
          ) : undefined
        }
        onClose={() => setSelected(null)}
        open={Boolean(selected)}
        title={selected?.name ?? '运行资源详情'}
      >
        {selected ? (
          <dl className={styles.detailGrid}>
            <div>
              <dt>资源类型</dt>
              <dd>{kindLabel(selected.kind)}</dd>
            </div>
            <div>
              <dt>运行状态</dt>
              <dd>{selected.status}</dd>
            </div>
            <div>
              <dt>业务上下文</dt>
              <dd>{contextLabel(selected)}</dd>
            </div>
            <div>
              <dt>运行时长</dt>
              <dd>{selected.duration || '—'}</dd>
            </div>
            <div>
              <dt>内部地址</dt>
              <dd>
                {selected.ip || '—'}
                {selected.port ? `:${selected.port}` : ''}
              </dd>
            </div>
            <div>
              <dt>操作系统</dt>
              <dd>{selected.osType || '—'}</dd>
            </div>
            <div className={styles.detailWide}>
              <dt>镜像</dt>
              <dd>{selected.image || '—'}</dd>
            </div>
            <div className={styles.detailWide}>
              <dt>公开入口</dt>
              <dd>{selected.entry || '—'}</dd>
            </div>
            <div className={styles.detailWide}>
              <dt>资源标识</dt>
              <dd>{selected.runtimeId || selected.id}</dd>
            </div>
          </dl>
        ) : null}
      </DetailDrawer>

      <VNextConfirmDialog
        confirmLabel="销毁资源"
        confirmationText={destroyTarget?.name}
        description={destroyTarget ? contextLabel(destroyTarget) : undefined}
        message="销毁操作会中断正在运行的靶机或实验环境，平台将等待服务端确认后再刷新资源状态。"
        onClose={() => setDestroyTarget(null)}
        onConfirm={destroy}
        open={Boolean(destroyTarget)}
        title={`销毁 ${destroyTarget?.name ?? '运行资源'}？`}
      />
    </div>
  )
}
