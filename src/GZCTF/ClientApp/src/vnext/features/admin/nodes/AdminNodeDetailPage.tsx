import { ArrowLeft, Network, RefreshCw, Trash2 } from 'lucide-react'
import { useMemo, useState } from 'react'
import { Link, useNavigate, useParams, useSearchParams } from 'react-router'
import { useSWRConfig } from 'swr'
import { ActionButton, InlineFeedback, VNextConfirmDialog } from '../../../shared/Interaction'
import { DataState } from '../../../shared/Primitives'
import { errorMessage } from '../../../shared/errors'
import { useVNextPageTitle } from '../../../shared/useVNextPageTitle'
import { nodeAdminApi, type NodeSummary } from '../api'
import {
  AdminPageHeader,
  MetricItem,
  MetricStrip,
  RefreshIndicator,
  ResourceMeter,
  StatusBadge,
} from '../shared/AdminWorkbench'
import styles from './AdminNodeDetailPage.module.css'
import { NodeResourceTab } from './NodeResourceTab'
import { formatHeartbeat, formatLoad, nodeStatusMeta, tunnelStatusMeta, useAdminNode } from './useAdminNodes'

const tabs = [
  { id: 'resources', label: '资源' },
  { id: 'capacity', label: '容量' },
  { id: 'network', label: '网络' },
  { id: 'agent', label: 'Agent' },
  { id: 'teamlab', label: 'TeamLab' },
  { id: 'events', label: '事件' },
] as const

type NodeTab = (typeof tabs)[number]['id']

function parseCapabilities(value: string | null) {
  if (!value) return []
  try {
    const parsed = JSON.parse(value) as Record<string, unknown>
    return Object.entries(parsed).filter((entry): entry is [string, boolean] => typeof entry[1] === 'boolean')
  } catch {
    return []
  }
}

function actionMessage(value: Record<string, unknown>) {
  if (typeof value.message === 'string') return value.message
  if (typeof value.success === 'boolean') return value.success ? '操作已完成。' : '操作未完成。'
  return '操作请求已提交。'
}

function Facts({ children }: { children: React.ReactNode }) {
  return <dl className={styles.facts}>{children}</dl>
}

function Fact({ label, value, wide = false }: { label: string; value: React.ReactNode; wide?: boolean }) {
  return (
    <div className={wide ? styles.factWide : undefined}>
      <dt>{label}</dt>
      <dd>{value}</dd>
    </div>
  )
}

function CapacityPanel({ node }: { node: NodeSummary }) {
  return (
    <section className={styles.panel}>
      <header>
        <span>CAPACITY</span>
        <h2>容量与负载</h2>
      </header>
      <div className={styles.capacityGrid}>
        <ResourceMeter
          detail={`${node.allocatedContainers} 已分配 · ${node.reservedContainers} 已预留`}
          label="Docker 槽位"
          max={node.maxContainers}
          value={node.allocatedContainers + node.reservedContainers}
        />
        <ResourceMeter
          detail={`${node.allocatedVms} 已分配 · ${node.reservedVms} 已预留`}
          label="VM 槽位"
          max={node.maxVms}
          value={node.allocatedVms + node.reservedVms}
        />
        <ResourceMeter label="CPU 负载" max={100} value={Math.round(node.cpuLoad * 100)} />
        <ResourceMeter label="内存负载" max={100} value={Math.round(node.memoryLoad * 100)} />
        <ResourceMeter
          detail={`${node.portPoolStart}-${node.portPoolEnd}`}
          label="公网端口池"
          max={node.totalPorts}
          value={node.usedPorts}
        />
      </div>
      <Facts>
        <Fact label="当前容器" value={node.currentContainers} />
        <Fact label="当前虚拟机" value={node.currentVms} />
        <Fact label="CPU" value={formatLoad(node.cpuLoad)} />
        <Fact label="内存" value={formatLoad(node.memoryLoad)} />
      </Facts>
    </section>
  )
}

export function AdminNodeDetailPage() {
  const { nodeId } = useParams()
  const [searchParams, setSearchParams] = useSearchParams()
  const navigate = useNavigate()
  const swr = useSWRConfig()
  const { node, error, isLoading, isRefreshing, mutate } = useAdminNode(nodeId)
  const [working, setWorking] = useState<string | null>(null)
  const [feedback, setFeedback] = useState<{ tone: 'danger' | 'success'; message: string } | null>(null)
  const [deleteOpen, setDeleteOpen] = useState(false)
  const requestedTab = searchParams.get('tab') as NodeTab | null
  const activeTab = tabs.some((tab) => tab.id === requestedTab) ? (requestedTab as NodeTab) : 'resources'
  const capabilities = useMemo(
    () => parseCapabilities(node?.teamLabCapabilitiesJson ?? null),
    [node?.teamLabCapabilitiesJson]
  )

  useVNextPageTitle(node ? `${node.name} · 节点管理` : '节点详情')

  const refresh = async () => {
    await Promise.all([mutate(), swr.mutate('vnext:admin:nodes')])
  }

  const toggleScheduling = async () => {
    if (!node || working) return
    setWorking('schedule')
    setFeedback(null)
    try {
      await nodeAdminApi.update(node.id, { isSchedulable: !node.isSchedulable })
      await refresh()
      setFeedback({ tone: 'success', message: node.isSchedulable ? '节点已暂停调度。' : '节点已恢复调度。' })
    } catch (updateError) {
      setFeedback({ tone: 'danger', message: errorMessage(updateError, '节点调度状态更新失败。') })
    } finally {
      setWorking(null)
    }
  }

  const syncAgent = async () => {
    if (!node || working) return
    setWorking('agent')
    setFeedback(null)
    try {
      const result = await nodeAdminApi.syncAgent(node.id)
      setFeedback({ tone: 'success', message: actionMessage(result) })
      await refresh()
    } catch (syncError) {
      setFeedback({ tone: 'danger', message: errorMessage(syncError, 'Agent 同步失败。') })
    } finally {
      setWorking(null)
    }
  }

  const probeTeamLab = async () => {
    if (!node || working) return
    setWorking('teamlab')
    setFeedback(null)
    try {
      const result = await nodeAdminApi.enableTeamLab(node.id, { dryRun: true, tunnelIp: node.teamLabTunnelIp })
      setFeedback({ tone: 'success', message: actionMessage(result) })
      await refresh()
    } catch (probeError) {
      setFeedback({ tone: 'danger', message: errorMessage(probeError, 'TeamLab 网络检测失败。') })
    } finally {
      setWorking(null)
    }
  }

  const deregister = async () => {
    if (!node) return false
    setWorking('delete')
    setFeedback(null)
    try {
      await nodeAdminApi.deregister(node.id)
      await swr.mutate('vnext:admin:nodes')
      navigate('/admin/nodes', { replace: true })
      return true
    } catch (deleteError) {
      setFeedback({ tone: 'danger', message: errorMessage(deleteError, '节点注销失败。') })
      return false
    } finally {
      setWorking(null)
    }
  }

  if (!nodeId) return <DataState description="节点标识缺失。" title="无法打开节点" />
  if (isLoading) return <DataState description="正在读取节点状态、容量和 Agent 心跳。" loading title="节点详情加载中" />
  if (error || !node) return <DataState description="节点不存在或当前账户没有管理权限。" title="节点详情加载失败" />

  const status = nodeStatusMeta(node.status)
  const tunnel = tunnelStatusMeta(node.teamLabTunnelStatus)

  return (
    <div className={styles.page}>
      <Link className={styles.backLink} to="/admin/nodes">
        <ArrowLeft size={16} />
        返回节点列表
      </Link>
      <AdminPageHeader
        actions={
          <>
            <ActionButton disabled={Boolean(working)} onClick={() => void toggleScheduling()} type="button">
              {working === 'schedule' ? '正在更新' : node.isSchedulable ? '暂停调度' : '恢复调度'}
            </ActionButton>
            <ActionButton
              disabled={Boolean(working) || node.isLocal}
              icon={<RefreshCw size={16} />}
              onClick={() => void syncAgent()}
              type="button"
            >
              {working === 'agent' ? '正在同步' : '同步 Agent'}
            </ActionButton>
            <ActionButton
              disabled={Boolean(working) || node.isLocal}
              icon={<Trash2 size={16} />}
              onClick={() => setDeleteOpen(true)}
              tone="danger"
              type="button"
            >
              注销节点
            </ActionButton>
          </>
        }
        description={`${node.hostAddress}:${node.agentPort} · ${node.isLocal ? '本地平台节点' : '远程运行节点'}`}
        eyebrow="NODE CONTROL"
        title={node.name}
      />

      <MetricStrip>
        <MetricItem
          detail={node.isSchedulable ? '参与新任务调度' : '不接收新任务'}
          label="运行状态"
          tone={status.tone}
          value={status.label}
        />
        <MetricItem
          detail={`${node.allocatedContainers} 已分配`}
          label="Docker"
          value={`${node.currentContainers} / ${node.maxContainers}`}
        />
        <MetricItem detail={`${node.allocatedVms} 已分配`} label="VM" value={`${node.currentVms} / ${node.maxVms}`} />
        <MetricItem
          detail={`${node.portPoolStart}-${node.portPoolEnd}`}
          label="公网端口"
          value={`${node.usedPorts} / ${node.totalPorts}`}
        />
        <MetricItem
          detail={node.teamLabTunnelIp || '未分配隧道地址'}
          label="TeamLab"
          tone={tunnel.tone}
          value={tunnel.label}
        />
      </MetricStrip>

      <div className={styles.statusLine}>
        <StatusBadge tone={status.tone}>{status.label}</StatusBadge>
        <span>{formatHeartbeat(node.lastHeartbeat)}</span>
        <RefreshIndicator active={isRefreshing} label="节点状态自动同步" />
      </div>

      {feedback ? <InlineFeedback tone={feedback.tone}>{feedback.message}</InlineFeedback> : null}
      {node.unschedulableReasons.length ? (
        <InlineFeedback tone="danger">{node.unschedulableReasons.join('；')}</InlineFeedback>
      ) : null}

      <nav aria-label="节点详情视图" className={styles.tabs}>
        {tabs.map((tab) => (
          <button
            aria-current={activeTab === tab.id ? 'page' : undefined}
            data-active={activeTab === tab.id || undefined}
            key={tab.id}
            onClick={() => {
              const next = new URLSearchParams(searchParams)
              if (tab.id === 'resources') next.delete('tab')
              else next.set('tab', tab.id)
              next.delete('page')
              next.delete('type')
              next.delete('status')
              setSearchParams(next)
            }}
            type="button"
          >
            {tab.label}
          </button>
        ))}
      </nav>

      <div className={styles.tabContent}>
        {activeTab === 'resources' ? <NodeResourceTab nodeId={node.id} onMutated={refresh} /> : null}
        {activeTab === 'capacity' ? <CapacityPanel node={node} /> : null}
        {activeTab === 'network' ? (
          <section className={styles.panel}>
            <header>
              <span>NETWORK</span>
              <h2>网络与端口</h2>
            </header>
            <Facts>
              <Fact label="节点地址" value={node.hostAddress} />
              <Fact label="Agent 端口" value={node.agentPort} />
              <Fact label="端口池模式" value={node.portPoolMode} />
              <Fact label="公网端口范围" value={`${node.portPoolStart}-${node.portPoolEnd}`} />
              <Fact label="已占用端口" value={`${node.usedPorts} / ${node.totalPorts}`} />
              <Fact label="TeamLab 隧道地址" value={node.teamLabTunnelIp || '—'} />
            </Facts>
          </section>
        ) : null}
        {activeTab === 'agent' ? (
          <section className={styles.panel}>
            <header>
              <span>AGENT</span>
              <h2>Agent 与心跳</h2>
            </header>
            <Facts>
              <Fact label="Agent 版本" value={node.teamLabAgentVersion || '未上报'} />
              <Fact label="协议版本" value={node.teamLabProtocolVersion || '—'} />
              <Fact label="最后心跳" value={formatHeartbeat(node.lastHeartbeat)} />
              <Fact label="配置版本" value={node.teamLabTunnelConfigVersion || '—'} />
            </Facts>
            <div className={styles.panelActions}>
              <ActionButton
                disabled={Boolean(working) || node.isLocal}
                icon={<RefreshCw size={16} />}
                onClick={() => void syncAgent()}
                type="button"
              >
                同步 Agent
              </ActionButton>
            </div>
          </section>
        ) : null}
        {activeTab === 'teamlab' ? (
          <section className={styles.panel}>
            <header>
              <span>TEAMLAB FABRIC</span>
              <h2>组网能力</h2>
            </header>
            <Facts>
              <Fact label="网络状态" value={tunnel.label} />
              <Fact label="隧道地址" value={node.teamLabTunnelIp || '—'} />
              <Fact label="Fabric 地址" value={node.teamLabFabricIp || '—'} />
              <Fact label="Docker 场景" value={node.canHostTeamLabDocker ? '可调度' : '不可调度'} />
              <Fact label="VM 场景" value={node.canHostTeamLabVm ? '可调度' : '不可调度'} />
              <Fact label="最近握手" value={formatHeartbeat(node.teamLabTunnelLastHandshake)} />
              <Fact
                label="能力探测"
                value={
                  capabilities.length
                    ? capabilities.map(([key, enabled]) => `${key}:${enabled ? 'yes' : 'no'}`).join(' · ')
                    : '未上报'
                }
                wide
              />
            </Facts>
            {node.teamLabTunnelLastError ? (
              <InlineFeedback tone="danger">{node.teamLabTunnelLastError}</InlineFeedback>
            ) : null}
            <div className={styles.panelActions}>
              <ActionButton
                disabled={Boolean(working)}
                icon={<Network size={16} />}
                onClick={() => void probeTeamLab()}
                type="button"
              >
                运行兼容性检测
              </ActionButton>
            </div>
          </section>
        ) : null}
        {activeTab === 'events' ? (
          <DataState description="当前稳定服务端尚未提供节点级运行事件查询接口。" title="暂无节点事件数据" />
        ) : null}
      </div>

      <VNextConfirmDialog
        confirmLabel="注销节点"
        confirmationText={node.name}
        description={`${node.hostAddress} · ${node.currentContainers} 个容器 · ${node.currentVms} 个 VM`}
        message="节点仍承载活动资源时服务端会拒绝普通注销。注销成功后，该节点将从调度集群中移除。"
        onClose={() => setDeleteOpen(false)}
        onConfirm={deregister}
        open={deleteOpen}
        title={`注销 ${node.name}？`}
      />
    </div>
  )
}
