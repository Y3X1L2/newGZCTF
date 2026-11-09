import { AlertTriangle, ArrowRight, RefreshCw } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import { ActionButton, InlineFeedback } from '../../../shared/Interaction'
import { DataState } from '../../../shared/Primitives'
import type { AdminLogEntry, DeploymentTask, GlobalInstanceItem, NodeSummary } from '../api'
import { instanceContextLabel, instanceKindLabel, instanceStatusMeta } from '../instances/instancePresentation'
import { adminLogActor, adminLogTitle } from '../logs/adminLogPresentation'
import { NodeTelemetryPanel } from '../nodes/NodeTelemetryPanel'
import { liveTelemetryNodes } from '../nodes/nodeTelemetry'
import { nodeStatusMeta, formatHeartbeat } from '../nodes/useAdminNodes'
import { deploymentStageLabel, deploymentStatusMeta } from '../queue/deploymentQueuePresentation'
import {
  AdminPageHeader,
  MetricItem,
  MetricStrip,
  ResourceMeter,
  StatusBadge,
  RefreshIndicator,
} from '../shared/AdminWorkbench'
import { formatAdminDate } from '../shared/adminFormat'
import styles from './AdminDashboardPage.module.css'
import { DashboardSectionBoundary } from './DashboardSectionBoundary'
import { useAdminDashboard } from './useAdminDashboard'

function SectionHeader({ title, label, to }: { title: string; label: string; to: string }) {
  return (
    <header className={styles.sectionHeader}>
      <h2>{title}</h2>
      <Link to={to}>
        {label}
        <ArrowRight size={15} />
      </Link>
    </header>
  )
}

function NodeRows({ nodes }: { nodes: NodeSummary[] }) {
  if (!nodes.length) return <DataState description="注册节点后将在这里显示容量。" title="暂无节点" />
  return (
    <div className={styles.nodeRows}>
      {nodes.slice(0, 6).map((node) => {
        const status = nodeStatusMeta(node.status)
        return (
          <Link className={styles.nodeRow} key={node.id} to={`/admin/nodes/${node.id}`}>
            <div className={styles.rowIdentity}>
              <strong>{node.name}</strong>
              <small>{node.hostAddress}</small>
            </div>
            <StatusBadge tone={status.tone}>{status.label}</StatusBadge>
            <div className={styles.nodeTelemetry}>
              <span>
                CPU <b>{liveTelemetryNodes([node]).length ? `${(node.cpuLoad * 100).toFixed(1)}%` : '—'}</b>
              </span>
              <span>
                内存 <b>{liveTelemetryNodes([node]).length ? `${(node.memoryLoad * 100).toFixed(1)}%` : '—'}</b>
              </span>
              <small>心跳 {formatHeartbeat(node.lastHeartbeat)}</small>
            </div>
            <ResourceMeter
              detail={`${node.allocatedContainers} 已分配 · ${node.reservedContainers} 已预留`}
              label="Docker"
              max={node.maxContainers}
              value={node.allocatedContainers + node.reservedContainers}
            />
            <ResourceMeter
              detail={`${node.allocatedVms} 已分配 · ${node.reservedVms} 已预留`}
              label="VM"
              max={node.maxVms}
              value={node.allocatedVms + node.reservedVms}
            />
          </Link>
        )
      })}
    </div>
  )
}

function QueueRows({ tasks }: { tasks: DeploymentTask[] }) {
  if (!tasks.length) return <DataState description="最近没有部署请求。" title="部署队列为空" />
  return (
    <div className={styles.compactRows}>
      {tasks.slice(0, 6).map((task) => {
        const status = deploymentStatusMeta(task.statusKey)
        return (
          <Link className={styles.compactRow} key={task.id} to={`/admin/queue?status=${task.statusKey}`}>
            <div className={styles.rowIdentity}>
              <strong>{task.requestLabel}</strong>
              <small>{task.targetNodeLabel}</small>
            </div>
            <span>{deploymentStageLabel(task.stage, task.stageMessage)}</span>
            <StatusBadge pulse={status.active} tone={status.tone}>
              {status.label}
            </StatusBadge>
          </Link>
        )
      })}
    </div>
  )
}

function InstanceRows({ instances }: { instances: GlobalInstanceItem[] }) {
  const risks = instances
    .filter(
      (instance) =>
        instanceStatusMeta(instance).tone === 'danger' ||
        (instance.expectedStopAt ?? Infinity) < Date.now() + 15 * 60_000
    )
    .slice(0, 6)
  if (!risks.length)
    return (
      <p className={styles.stableNote}>
        当前加载范围内无异常或 15 分钟内到期实例。<Link to="/admin/instances">查看实例 →</Link>
      </p>
    )
  return (
    <div className={styles.riskRows}>
      {risks.map((instance) => {
        const status = instanceStatusMeta(instance)
        return (
          <Link
            className={styles.compactRow}
            key={`${instance.nodeId}:${instance.kind}:${instance.id}`}
            to={`/admin/instances?node=${instance.nodeId}`}
          >
            <div className={styles.rowIdentity}>
              <strong>{instance.name}</strong>
              <small>{instanceContextLabel(instance)}</small>
            </div>
            <span>
              {instanceKindLabel(instance.kind)} · {instance.nodeName}
            </span>
            <StatusBadge tone={status.tone}>{status.label}</StatusBadge>
          </Link>
        )
      })}
    </div>
  )
}

function ErrorRows({ logs }: { logs: AdminLogEntry[] }) {
  const { t } = useTranslation('admin')
  const text = (key: string) => t(`logs.${key}`)
  if (!logs.length) return <DataState description="最近查询范围内没有 Error 日志。" title="没有最近错误" />
  return (
    <div className={styles.errorRows}>
      {logs.slice(0, 6).map((log, index) => (
        <Link className={styles.errorRow} key={log.id ?? `${log.time}:${index}`} to={`/admin/logs?level=Error`}>
          <AlertTriangle size={16} />
          <div className={styles.rowIdentity}>
            <strong>{adminLogTitle(log, text)}</strong>
            <small>
              {adminLogActor(log, text)} · {formatAdminDate(log.time)}
            </small>
          </div>
        </Link>
      ))}
    </div>
  )
}

export function AdminDashboardPage() {
  const dashboard = useAdminDashboard()
  const metrics = dashboard.metrics

  return (
    <div className={styles.page}>
      <AdminPageHeader
        actions={
          <ActionButton
            loading={dashboard.manualRefreshing}
            disabled={dashboard.manualRefreshing}
            icon={<RefreshCw size={16} />}
            onClick={dashboard.refresh}
            type="button"
          >
            {dashboard.manualRefreshing ? '正在刷新概览…' : '刷新概览'}
          </ActionButton>
        }
        description="汇总节点、容量、部署、实例和错误日志的当前事实状态。"
        eyebrow="OPERATIONS OVERVIEW"
        title="运行概览"
      />

      <div className={styles.syncLine}>
        <RefreshIndicator
          active={dashboard.nodes.isRefreshing || dashboard.manualRefreshing}
          label="10 秒自动同步 · 以服务端心跳为准"
        />
        <span>CPU / 内存仅统计 60 秒内有心跳的在线节点</span>
      </div>
      {dashboard.nodes.error ||
      dashboard.images.error ||
      dashboard.queue.error ||
      dashboard.instances.error ||
      dashboard.logs.error ? (
        <InlineFeedback tone="danger">
          部分监控数据读取失败，请点击刷新概览重试。已保留的数值可能来自上次成功同步。
        </InlineFeedback>
      ) : null}
      <NodeTelemetryPanel
        nodes={dashboard.nodes.nodes ?? []}
        loading={dashboard.nodes.isLoading}
        failed={Boolean(dashboard.nodes.error)}
      />

      <MetricStrip>
        <MetricItem
          detail="在线或繁忙节点"
          label="节点在线"
          tone={metrics.onlineNodes === metrics.totalNodes && metrics.totalNodes > 0 ? 'success' : 'warning'}
          value={
            <Link className={styles.metricLink} to="/admin/nodes">
              {dashboard.nodes.nodes ? `${metrics.onlineNodes} / ${metrics.totalNodes}` : '—'}
            </Link>
          }
        />
        <MetricItem
          detail="允许接收新任务"
          label="可调度节点"
          value={
            <Link className={styles.metricLink} to="/admin/nodes">
              {dashboard.nodes.nodes ? metrics.schedulableNodes : '—'}
            </Link>
          }
        />
        <MetricItem
          detail={`节点覆盖 ${metrics.instanceCoverage}`}
          label="活跃实例"
          value={
            <Link className={styles.metricLink} to="/admin/instances">
              {dashboard.instances.inventory ? metrics.activeInstances : '—'}
            </Link>
          }
        />
        <MetricItem
          detail="全部环境模板"
          label="异常镜像"
          tone={metrics.imageErrors ? 'danger' : 'neutral'}
          value={
            <Link className={styles.metricLink} to="/admin/images?status=2">
              {dashboard.images.images ? metrics.imageErrors : '—'}
            </Link>
          }
        />
      </MetricStrip>

      <div className={styles.nodeSection}>
        <DashboardSectionBoundary name="节点容量">
          <section className={styles.section}>
            <SectionHeader label="管理节点" title="节点负载与容量" to="/admin/nodes" />
            {dashboard.nodes.isLoading ? (
              <DataState description="正在读取节点容量。" loading title="节点加载中" />
            ) : dashboard.nodes.error ? (
              <DataState description="节点接口暂时不可用。" title="节点容量加载失败" />
            ) : (
              <NodeRows nodes={dashboard.nodes.nodes ?? []} />
            )}
          </section>
        </DashboardSectionBoundary>
      </div>
      <DashboardSectionBoundary name="异常实例">
        <section aria-label="实例关注事项" className={styles.riskSection}>
          {dashboard.instances.isLoading ? (
            <DataState description="正在汇总活跃实例。" loading title="实例加载中" />
          ) : dashboard.instances.error ? (
            <DataState description="实例聚合暂时不可用。" title="实例加载失败" />
          ) : (
            <InstanceRows instances={dashboard.instances.inventory?.items ?? []} />
          )}
        </section>
      </DashboardSectionBoundary>

      <div className={styles.secondaryGrid}>
        <DashboardSectionBoundary name="部署队列">
          <section className={styles.section}>
            <SectionHeader label="查看全部" title="最近部署任务" to="/admin/queue" />
            {dashboard.queue.isLoading ? (
              <DataState description="正在读取最近部署任务。" loading title="队列加载中" />
            ) : dashboard.queue.error ? (
              <DataState description="部署队列接口暂时不可用。" title="队列加载失败" />
            ) : (
              <QueueRows tasks={dashboard.queue.queue?.items ?? []} />
            )}
          </section>
        </DashboardSectionBoundary>

        <DashboardSectionBoundary name="错误日志">
          <section className={styles.section}>
            <SectionHeader label="查看日志" title="最近 Error 日志" to="/admin/logs?level=Error" />
            {dashboard.logs.isLoading ? (
              <DataState description="正在读取最近错误日志。" loading title="日志加载中" />
            ) : dashboard.logs.error ? (
              <DataState description="错误日志接口暂时不可用。" title="日志加载失败" />
            ) : (
              <ErrorRows logs={dashboard.logs.logs?.items ?? []} />
            )}
          </section>
        </DashboardSectionBoundary>
      </div>
    </div>
  )
}
