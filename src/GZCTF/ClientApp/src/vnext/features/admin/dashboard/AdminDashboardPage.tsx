import { AlertTriangle, ArrowRight, RefreshCw } from 'lucide-react'
import { Link } from 'react-router'
import { ActionButton } from '../../../shared/Interaction'
import { DataState } from '../../../shared/Primitives'
import type { AdminLogEntry, DeploymentTask, GlobalInstanceItem, NodeSummary } from '../api'
import { instanceContextLabel, instanceKindLabel, instanceStatusMeta } from '../instances/instancePresentation'
import { adminLogResource, adminLogSource } from '../logs/adminLogPresentation'
import { nodeStatusMeta } from '../nodes/useAdminNodes'
import { deploymentStageLabel, deploymentStatusMeta } from '../queue/deploymentQueuePresentation'
import { formatAdminDate } from '../shared/adminFormat'
import { AdminPageHeader, MetricItem, MetricStrip, ResourceMeter, StatusBadge } from '../shared/AdminWorkbench'
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
            <StatusBadge pulse={status.active} tone={status.tone}>{status.label}</StatusBadge>
          </Link>
        )
      })}
    </div>
  )
}

function InstanceRows({ instances }: { instances: GlobalInstanceItem[] }) {
  const risks = instances
    .filter((instance) => instanceStatusMeta(instance).tone === 'danger' || (instance.expectedStopAt ?? Infinity) < Date.now() + 15 * 60_000)
    .slice(0, 6)
  if (!risks.length) return <DataState description="当前加载范围内没有异常或 15 分钟内到期的实例。" title="运行资源稳定" />
  return (
    <div className={styles.compactRows}>
      {risks.map((instance) => {
        const status = instanceStatusMeta(instance)
        return (
          <Link className={styles.compactRow} key={`${instance.nodeId}:${instance.kind}:${instance.id}`} to={`/admin/instances?node=${instance.nodeId}`}>
            <div className={styles.rowIdentity}>
              <strong>{instance.name}</strong>
              <small>{instanceContextLabel(instance)}</small>
            </div>
            <span>{instanceKindLabel(instance.kind)} · {instance.nodeName}</span>
            <StatusBadge tone={status.tone}>{status.label}</StatusBadge>
          </Link>
        )
      })}
    </div>
  )
}

function ErrorRows({ logs }: { logs: AdminLogEntry[] }) {
  if (!logs.length) return <DataState description="最近查询范围内没有 Error 日志。" title="没有最近错误" />
  return (
    <div className={styles.errorRows}>
      {logs.slice(0, 6).map((log, index) => (
        <Link className={styles.errorRow} key={log.id ?? `${log.time}:${index}`} to={`/admin/logs?level=Error`}>
          <AlertTriangle size={16} />
          <div className={styles.rowIdentity}>
            <strong>{log.msg || adminLogResource(log)}</strong>
            <small>{adminLogSource(log)} · {formatAdminDate(log.time)}</small>
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
          <ActionButton icon={<RefreshCw size={16} />} onClick={() => void dashboard.refresh()} type="button">
            刷新概览
          </ActionButton>
        }
        description="汇总节点、容量、部署、实例和错误日志的当前事实状态。"
        eyebrow="OPERATIONS OVERVIEW"
        title="运行概览"
      />

      <MetricStrip>
        <MetricItem
          detail="在线或繁忙节点"
          label="节点在线"
          tone={metrics.onlineNodes === metrics.totalNodes && metrics.totalNodes > 0 ? 'success' : 'warning'}
          value={<Link className={styles.metricLink} to="/admin/nodes">{metrics.onlineNodes} / {metrics.totalNodes}</Link>}
        />
        <MetricItem detail="允许接收新任务" label="可调度节点" value={<Link className={styles.metricLink} to="/admin/nodes">{metrics.schedulableNodes}</Link>} />
        <MetricItem detail="最大值减已分配与预留" label="Docker 可用槽位" value={metrics.dockerAvailable} />
        <MetricItem detail="最大值减已分配与预留" label="VM 可用槽位" value={metrics.vmAvailable} />
        <MetricItem
          detail={`节点覆盖 ${metrics.instanceCoverage}`}
          label="活跃实例"
          value={<Link className={styles.metricLink} to="/admin/instances">{metrics.activeInstances}</Link>}
        />
        <MetricItem
          detail="全部环境模板"
          label="异常镜像"
          tone={metrics.imageErrors ? 'danger' : 'neutral'}
          value={<Link className={styles.metricLink} to="/admin/images?status=2">{metrics.imageErrors}</Link>}
        />
      </MetricStrip>

      <div className={styles.primaryGrid}>
        <DashboardSectionBoundary name="节点容量">
          <section className={styles.section}>
            <SectionHeader label="管理节点" title="节点容量" to="/admin/nodes" />
            {dashboard.nodes.isLoading ? (
              <DataState description="正在读取节点容量。" loading title="节点加载中" />
            ) : dashboard.nodes.error ? (
              <DataState description="节点接口暂时不可用。" title="节点容量加载失败" />
            ) : (
              <NodeRows nodes={dashboard.nodes.nodes ?? []} />
            )}
          </section>
        </DashboardSectionBoundary>

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
      </div>

      <div className={styles.secondaryGrid}>
        <DashboardSectionBoundary name="异常实例">
          <section className={styles.section}>
            <SectionHeader label="查看实例" title="异常与即将到期" to="/admin/instances" />
            {dashboard.instances.isLoading ? (
              <DataState description="正在汇总活跃实例。" loading title="实例加载中" />
            ) : dashboard.instances.error ? (
              <DataState description="实例聚合暂时不可用。" title="实例加载失败" />
            ) : (
              <InstanceRows instances={dashboard.instances.inventory?.items ?? []} />
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
