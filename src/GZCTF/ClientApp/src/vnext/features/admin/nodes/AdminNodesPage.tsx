import { ArrowRight, Plus, Search, Server } from 'lucide-react'
import { ChangeEvent, useEffect, useMemo, useState } from 'react'
import { useNavigate } from 'react-router'
import { NodeCapability, NodeStatus } from '@Api'
import { ActionButton } from '../../../shared/Interaction'
import { DataState } from '../../../shared/Primitives'
import { useVNextPageTitle } from '../../../shared/useVNextPageTitle'
import type { NodeSummary } from '../api'
import {
  AdminPageHeader,
  DataTable,
  FilterToolbar,
  MetricItem,
  MetricStrip,
  PaginationBar,
  RefreshIndicator,
  ResourceMeter,
  StatusBadge,
  ToolbarGroup,
  type AdminDataColumn,
} from '../shared/AdminWorkbench'
import { useAdminQueryState } from '../shared/useAdminQueryState'
import styles from './AdminNodesPage.module.css'
import { NodeRegistrationDialog } from './NodeRegistrationDialog'
import { formatHeartbeat, hasNodeCapability, nodeStatusMeta, tunnelStatusMeta, useAdminNodes } from './useAdminNodes'

const PAGE_SIZE = 20

function capabilityText(node: NodeSummary) {
  const labels = []
  if (hasNodeCapability(node.capabilities, NodeCapability.Docker)) labels.push('Docker')
  if (hasNodeCapability(node.capabilities, NodeCapability.Kvm)) labels.push('KVM')
  if (node.canHostTeamLab) labels.push('TeamLab')
  return labels.length ? labels.join(' · ') : '未检测到运行能力'
}

function matchesCapability(node: NodeSummary, value: string | null) {
  if (!value) return true
  if (value === 'docker') return hasNodeCapability(node.capabilities, NodeCapability.Docker)
  if (value === 'kvm') return hasNodeCapability(node.capabilities, NodeCapability.Kvm)
  if (value === 'teamlab') return node.canHostTeamLab
  if (value === 'unschedulable') return !node.isSchedulable || node.unschedulableReasons.length > 0
  return true
}

export function AdminNodesPage() {
  const navigate = useNavigate()
  const queryState = useAdminQueryState(PAGE_SIZE)
  const [query, setQuery] = useState(queryState.params.get('q') ?? '')
  const [registerOpen, setRegisterOpen] = useState(false)
  const { nodes, error, isLoading, isRefreshing, mutate } = useAdminNodes()

  useVNextPageTitle('节点管理')

  useEffect(() => setQuery(queryState.params.get('q') ?? ''), [queryState.params])

  useEffect(() => {
    const current = queryState.params.get('q') ?? ''
    if (query.trim() === current) return undefined
    const timer = window.setTimeout(() => queryState.update({ q: query.trim() || null }, { replace: true }), 250)
    return () => window.clearTimeout(timer)
  }, [query, queryState])

  const filtered = useMemo(() => {
    const keyword = (queryState.params.get('q') ?? '').trim().toLocaleLowerCase('zh-CN')
    const status = queryState.params.get('status')
    const capability = queryState.params.get('capability')
    return (nodes ?? []).filter((node) => {
      const statusMatches = !status || String(node.status) === status
      const keywordMatches =
        !keyword ||
        `${node.name} ${node.hostAddress} ${capabilityText(node)}`.toLocaleLowerCase('zh-CN').includes(keyword)
      return statusMatches && keywordMatches && matchesCapability(node, capability)
    })
  }, [nodes, queryState.params])

  const pageCount = Math.max(1, Math.ceil(filtered.length / PAGE_SIZE))
  const page = Math.min(queryState.page, pageCount)
  const visible = filtered.slice((page - 1) * PAGE_SIZE, page * PAGE_SIZE)

  useEffect(() => {
    if (queryState.page <= pageCount) return
    queryState.update({ page: pageCount <= 1 ? null : pageCount }, { replace: true, resetPage: false })
  }, [pageCount, queryState])

  const metrics = useMemo(() => {
    const source = nodes ?? []
    return {
      online: source.filter((node) => node.status === NodeStatus.Online || node.status === NodeStatus.Busy).length,
      dockerUsed: source.reduce((total, node) => total + node.allocatedContainers, 0),
      dockerMax: source.reduce((total, node) => total + node.maxContainers, 0),
      vmUsed: source.reduce((total, node) => total + node.allocatedVms, 0),
      vmMax: source.reduce((total, node) => total + node.maxVms, 0),
      alerts: source.filter(
        (node) =>
          node.status === NodeStatus.Error || node.status === NodeStatus.Offline || node.unschedulableReasons.length > 0
      ).length,
    }
  }, [nodes])

  const columns: AdminDataColumn<NodeSummary>[] = [
    {
      id: 'identity',
      header: '节点',
      width: 'wide',
      render: (node) => (
        <div className={styles.nodeIdentity}>
          <span>
            <Server size={17} />
          </span>
          <div>
            <strong>{node.name}</strong>
            <small>{node.isLocal ? '本地节点' : node.hostAddress}</small>
          </div>
        </div>
      ),
    },
    {
      id: 'status',
      header: '状态',
      width: 'compact',
      render: (node) => {
        const meta = nodeStatusMeta(node.status)
        return (
          <div className={styles.statusStack}>
            <StatusBadge tone={meta.tone}>{meta.label}</StatusBadge>
            <small>{node.isSchedulable ? '参与调度' : '暂停调度'}</small>
          </div>
        )
      },
    },
    {
      id: 'capability',
      header: '运行能力',
      width: 'medium',
      visibility: 'desktop',
      render: (node) => <span className={styles.capability}>{capabilityText(node)}</span>,
    },
    {
      id: 'capacity',
      header: '容量',
      width: 'wide',
      visibility: 'desktop',
      render: (node) => (
        <div className={styles.capacityCell}>
          <ResourceMeter label="Docker" max={node.maxContainers} value={node.allocatedContainers} />
          <ResourceMeter label="VM" max={node.maxVms} value={node.allocatedVms} />
        </div>
      ),
    },
    {
      id: 'ports',
      header: '端口池',
      width: 'medium',
      visibility: 'wide',
      render: (node) => (
        <div className={styles.portCell}>
          <strong>
            {node.usedPorts} / {node.totalPorts}
          </strong>
          <small>
            {node.portPoolMode} · {node.portPoolStart}-{node.portPoolEnd}
          </small>
        </div>
      ),
    },
    {
      id: 'teamlab',
      header: 'TeamLab',
      width: 'medium',
      visibility: 'desktop',
      render: (node) => {
        const meta = tunnelStatusMeta(node.teamLabTunnelStatus)
        return <StatusBadge tone={meta.tone}>{node.canHostTeamLab ? meta.label : '能力不足'}</StatusBadge>
      },
    },
    {
      id: 'heartbeat',
      header: '最后心跳',
      width: 'medium',
      visibility: 'wide',
      render: (node) => <span className={styles.mono}>{formatHeartbeat(node.lastHeartbeat)}</span>,
    },
    {
      id: 'action',
      header: '操作',
      width: 'compact',
      align: 'right',
      render: (node) => (
        <button
          aria-label={`打开 ${node.name}`}
          className={styles.iconButton}
          onClick={() => navigate(`/admin/nodes/${node.id}`)}
          type="button"
        >
          <ArrowRight size={16} />
        </button>
      ),
    },
  ]

  return (
    <div className={styles.page}>
      <AdminPageHeader
        actions={
          <ActionButton icon={<Plus size={16} />} onClick={() => setRegisterOpen(true)} tone="primary" type="button">
            添加节点
          </ActionButton>
        }
        description="查看节点运行能力、容量、端口池、Agent 心跳与多节点调度状态。"
        eyebrow="RUNTIME FLEET"
        title="节点管理"
      />

      <MetricStrip>
        <MetricItem
          detail={`共 ${nodes?.length ?? 0} 个节点`}
          label="在线节点"
          tone={metrics.online ? 'success' : 'warning'}
          value={metrics.online}
        />
        <MetricItem
          detail={`${metrics.dockerUsed} 个槽位已占用`}
          label="Docker 容量"
          value={`${metrics.dockerUsed} / ${metrics.dockerMax}`}
        />
        <MetricItem
          detail={`${metrics.vmUsed} 个槽位已占用`}
          label="VM 容量"
          value={`${metrics.vmUsed} / ${metrics.vmMax}`}
        />
        <MetricItem
          detail="离线、异常或不可调度"
          label="运行告警"
          tone={metrics.alerts ? 'danger' : 'neutral'}
          value={metrics.alerts}
        />
      </MetricStrip>

      <FilterToolbar>
        <ToolbarGroup grow>
          <label className={styles.searchBox}>
            <Search aria-hidden="true" size={16} />
            <input
              aria-label="搜索节点"
              onChange={(event: ChangeEvent<HTMLInputElement>) => setQuery(event.currentTarget.value)}
              placeholder="搜索节点名称、地址或能力"
              type="search"
              value={query}
            />
          </label>
          <select
            aria-label="节点状态"
            onChange={(event) => queryState.update({ status: event.currentTarget.value || null })}
            value={queryState.params.get('status') ?? ''}
          >
            <option value="">全部状态</option>
            <option value={NodeStatus.Online}>在线</option>
            <option value={NodeStatus.Busy}>繁忙</option>
            <option value={NodeStatus.Offline}>离线</option>
            <option value={NodeStatus.Error}>异常</option>
            <option value={NodeStatus.Unknown}>未知</option>
          </select>
          <select
            aria-label="节点能力"
            onChange={(event) => queryState.update({ capability: event.currentTarget.value || null })}
            value={queryState.params.get('capability') ?? ''}
          >
            <option value="">全部能力</option>
            <option value="docker">Docker</option>
            <option value="kvm">KVM</option>
            <option value="teamlab">TeamLab</option>
            <option value="unschedulable">不可调度</option>
          </select>
        </ToolbarGroup>
        <RefreshIndicator active={isRefreshing} label="10 秒自动同步" />
      </FilterToolbar>

      {isLoading ? (
        <DataState description="正在读取节点心跳、容量和调度能力。" loading title="节点列表加载中" />
      ) : error ? (
        <DataState description="节点接口暂时不可用，请检查平台与 Agent 状态。" title="节点列表加载失败" />
      ) : (
        <>
          <DataTable
            caption="运行节点列表"
            columns={columns}
            emptyDescription="调整名称、状态或能力筛选后重试。"
            emptyTitle="没有符合条件的节点"
            onRowClick={(node) => navigate(`/admin/nodes/${node.id}`)}
            rowKey={(node) => node.id}
            rows={visible}
          />
          <PaginationBar onPageChange={queryState.setPage} page={page} pageCount={pageCount} total={filtered.length} />
        </>
      )}

      <NodeRegistrationDialog
        onClose={() => setRegisterOpen(false)}
        onCompleted={async () => {
          await mutate()
        }}
        open={registerOpen}
      />
    </div>
  )
}
