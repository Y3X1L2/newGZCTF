import { Activity, ArrowLeft, Boxes, FileClock, Network, RotateCcw, Trash2, Wrench, Pause, Play } from 'lucide-react'
import { useCallback, useState } from 'react'
import { Link, useParams, useSearchParams } from 'react-router'
import { ActionButton, InlineFeedback, VNextConfirmDialog } from '../../../../shared/Interaction'
import { DataState } from '../../../../shared/Primitives'
import { errorMessage } from '../../../../shared/errors'
import { useVNextPageTitle } from '../../../../shared/useVNextPageTitle'
import { MetricItem, MetricStrip, RefreshIndicator } from '../../shared/AdminWorkbench'
import { formatAdminDate } from '../../shared/adminFormat'
import { teamLabRuntimeApi } from '../api'
import { TeamLabRuntimeStatusBadge } from '../shared/TeamLabStatusBadge'
import { CapturePanel } from './CapturePanel'
import { AssetDiagnosticsPanel } from './AssetDiagnosticsPanel'
import { AssetFilesPanel } from './AssetFilesPanel'
import { AssetControlPanel } from './AssetControlPanel'
import { RuntimeDifferencesPanel } from './RuntimeDifferencesPanel'
import { DeviceHealthPanel } from './DeviceHealthPanel'
import { VmDiagnosticsPanel } from './VmDiagnosticsPanel'
import { RuntimeAccessPanel } from './RuntimeAccessPanel'
import { RuntimeEventPanel } from './RuntimeEventPanel'
import { RuntimeLinkPolicyPanel } from './RuntimeLinkPolicyPanel'
import { RuntimeLogPanel } from './RuntimeLogPanel'
import { RuntimeRemoteAccessPanel } from './RuntimeRemoteAccessPanel'
import { RemoteSessionsPanel } from './RemoteSessionsPanel'
import { RuntimeShardTable } from './RuntimeShardTable'
import { RuntimeStageTimeline } from './RuntimeStageTimeline'
import { RuntimeTaskPanel } from './RuntimeTaskPanel'
import { TaskHistoryPanel } from './TaskHistoryPanel'
import { RuntimeTopologyView } from './RuntimeTopologyView'
import styles from './TeamLabRuntimeDetailPage.module.css'
import { TrafficFlowPanel } from './TrafficFlowPanel'
import { TrafficPathPanel } from './TrafficPathPanel'
import { emptyTeamLabEventFilters, useRuntimeEvents, type TeamLabEventFilters } from './useRuntimeEvents'
import { useTeamLabRuntime } from './useTeamLabRuntime'
import { useTrafficObservability, type TrafficFlowFilters, type TrafficPathFilters } from './useTrafficObservability'

type RuntimeTab = 'overview' | 'operations' | 'link-policies' | 'events' | 'traffic' | 'capture'

const initialFlowFilters: TrafficFlowFilters = { query: '', protocol: '', networkKey: '' }
const initialPathFilters: TrafficPathFilters = { query: '', protocol: '', confidence: '' }

export function TeamLabRuntimeDetailPage() {
  const { topologyId = '', runtimeId = '' } = useParams()
  const [searchParams] = useSearchParams()
  const [tab, setTab] = useState<RuntimeTab>(() => searchParams.get('tab') === 'operations' ? 'operations' : 'overview')
  const [resetOpen, setResetOpen] = useState(false)
  const [destroyOpen, setDestroyOpen] = useState(false)
  const [pauseOpen, setPauseOpen] = useState(false)
  const [acting, setActing] = useState(false)
  const [actionError, setActionError] = useState<unknown>(null)
  const [flowFilters, setFlowFilters] = useState<TrafficFlowFilters>(initialFlowFilters)
  const [pathFilters, setPathFilters] = useState<TrafficPathFilters>(initialPathFilters)
  const [eventFilters, setEventFilters] = useState<TeamLabEventFilters>(emptyTeamLabEventFilters)
  const runtimeState = useTeamLabRuntime(runtimeId)
  const runtime = runtimeState.runtime
  const events = useRuntimeEvents(tab === 'events' ? runtimeId : '', runtime?.status, eventFilters)
  const traffic = useTrafficObservability(tab === 'traffic' ? runtimeId : '', runtime?.status, flowFilters, pathFilters)

  useVNextPageTitle(runtime ? `运行时 ${runtime.id.slice(0, 8)}` : 'TeamLab 运行时')

  const inspectFailure = useCallback((filters: TeamLabEventFilters) => {
    setEventFilters(filters)
    setTab('events')
  }, [])

  const reset = async () => {
    if (!runtime || acting) return false
    setActing(true)
    setActionError(null)
    try {
      const next = await teamLabRuntimeApi.resetRuntime(runtime.id, { overlays: null, releaseId: null })
      await runtimeState.mutate(next, { revalidate: false })
      return true
    } catch (error) {
      setActionError(error)
      return false
    } finally {
      setActing(false)
    }
  }

  const destroy = async () => {
    if (!runtime || acting) return false
    setActing(true)
    setActionError(null)
    try {
      const next = await teamLabRuntimeApi.destroyRuntime(runtime.id)
      await runtimeState.mutate(next, { revalidate: false })
      return true
    } catch (error) {
      setActionError(error)
      return false
    } finally {
      setActing(false)
    }
  }

  const pauseOrResume = async () => {
    if (!runtime || acting) return false
    setActing(true)
    setActionError(null)
    try {
      const next = runtime.status === 'paused' ? await teamLabRuntimeApi.resumeRuntime(runtime.id) : await teamLabRuntimeApi.pauseRuntime(runtime.id)
      await runtimeState.mutate(next, { revalidate: false })
      return true
    } catch (error) {
      setActionError(error)
      return false
    } finally { setActing(false) }
  }

  if (!runtimeId) return <DataState description="运行时标识无效。" title="无法打开运行时" />
  if (runtimeState.isLoading)
    return <DataState description="正在读取运行时、分片和资产投影。" loading title="运行时加载中" />
  if (runtimeState.error || !runtime)
    return <DataState description={errorMessage(runtimeState.error, '运行时加载失败。')} title="无法打开运行时" />

  const queueActive = !!runtime.queueStatus && ['pending', 'scheduling', 'scheduled', 'running'].includes(runtime.queueStatus)
  const canReset = !runtime.managedRolloutId && !queueActive && ['running', 'failed', 'paused'].includes(runtime.status)
  const canDestroy = !runtime.managedRolloutId && !['destroying', 'destroyed', 'cleanup-pending'].includes(runtime.status)
  const cleanupPending = runtime.status === 'cleanup-pending'
  return (
    <section className={styles.page}>
      <Link className={styles.backLink} to={searchParams.get('from') === 'runtime-search' ? '/admin/teamlab?view=runtimes' : `/admin/teamlab/${topologyId}/runtimes`}>
        <ArrowLeft size={16} />
        {searchParams.get('from') === 'runtime-search' ? '运行实例检索' : '试运行列表'}
      </Link>
      <header className={styles.pageHeader}>
        <div>
          <span>运行控制</span>
          <h2>运行实例 {runtime.id.slice(0, 8)}</h2>
          <p>
            发布 {runtime.releaseId} · 第 {runtime.generation} 代
          </p>
        </div>
        <div className={styles.actions}>
          <RefreshIndicator
            active={runtimeState.isRefreshing}
            label={runtimeState.isRefreshing ? '同步中' : '状态已同步'}
          />
          <ActionButton
            disabled={!canReset || acting}
            icon={<RotateCcw size={16} />}
            onClick={() => setResetOpen(true)}
            type="button"
          >
            重置
          </ActionButton>
          {(['running', 'paused'].includes(runtime.status) || runtime.error === 'runtime_pause_failed') ? <ActionButton disabled={acting || queueActive || !!runtime.managedRolloutId} icon={runtime.status === 'paused' ? <Play size={16} /> : <Pause size={16} />} onClick={() => setPauseOpen(true)} type="button">{runtime.status === 'paused' ? '恢复' : runtime.error === 'runtime_pause_failed' ? '重试暂停' : '暂停'}</ActionButton> : null}
          {cleanupPending ? (
            <ActionButton
              disabled={acting || !!runtime.managedRolloutId}
              icon={<RotateCcw size={16} />}
              onClick={() => setDestroyOpen(true)}
              tone="danger"
              type="button"
            >
              继续清理
            </ActionButton>
          ) : (
            <ActionButton
              disabled={!canDestroy || acting}
              icon={<Trash2 size={16} />}
              onClick={() => setDestroyOpen(true)}
              tone="danger"
              type="button"
            >
              销毁
            </ActionButton>
          )}
        </div>
      </header>
      {runtime.managedRolloutId ? <InlineFeedback tone="neutral">批量发布托管：{runtime.managedRolloutId}</InlineFeedback> : null}
      <RuntimeTaskPanel runtime={runtime} onInspect={() => inspectFailure({ generation: runtime.generation, stage: '' })} />
      {actionError ? (
        <InlineFeedback tone="danger">{errorMessage(actionError, '运行时操作失败。')}</InlineFeedback>
      ) : null}
      <MetricStrip>
        <MetricItem
          detail={runtime.stage}
          label="运行状态"
          value={<TeamLabRuntimeStatusBadge status={runtime.status} />}
          tone={runtime.status === 'failed' ? 'danger' : runtime.status === 'running' ? 'success' : 'info'}
        />
        <MetricItem detail={`${runtime.networks.length} 个网段`} label="节点分片" value={runtime.shards.length} />
        <MetricItem
          detail={`${runtime.assets.filter((asset) => asset.kind === 'vm').length} VM`}
          label="运行资产"
          value={runtime.assets.length}
        />
        <MetricItem
          detail={formatAdminDate(runtime.updatedAt ?? runtime.createdAt)}
          label="选手入口"
          value={runtime.openForAccess ? '已开放' : '未开放'}
          tone={runtime.openForAccess ? 'success' : 'neutral'}
        />
      </MetricStrip>

      <nav aria-label="运行时详情" className={styles.tabs}>
        <button data-active={tab === 'overview' || undefined} onClick={() => setTab('overview')} type="button">
          <Boxes size={16} />
          部署概览
        </button>
        <button data-active={tab === 'operations' || undefined} onClick={() => setTab('operations')} type="button">
          <Wrench size={16} />
          资产运维
        </button>
        <button data-active={tab === 'link-policies' || undefined} onClick={() => setTab('link-policies')} type="button">
          <Network size={16} />
          链路策略
        </button>
        <button data-active={tab === 'events' || undefined} onClick={() => setTab('events')} type="button">
          <FileClock size={16} />
          事件与日志
        </button>
        <button data-active={tab === 'traffic' || undefined} onClick={() => setTab('traffic')} type="button">
          <Activity size={16} />
          流量观测
        </button>
        <button data-active={tab === 'capture' || undefined} onClick={() => setTab('capture')} type="button">
          <span className={styles.captureDot} />
          按需抓包
        </button>
      </nav>

      <div className={styles.content}>
        {tab === 'overview' ? (
          <>
            <RuntimeStageTimeline runtime={runtime} />
            <RuntimeAccessPanel canCreate={runtime.status === 'running'} runtimeId={runtime.id} />
            <RuntimeShardTable
              onInspectFailure={inspectFailure}
              runtime={runtime}
            />
            <RuntimeTopologyView
              onInspectFailure={inspectFailure}
              runtime={runtime}
            />
          </>
        ) : null}
        {tab === 'operations' ? <><DeviceHealthPanel runtimeId={runtime.id} generation={runtime.generation} /><RuntimeDifferencesPanel key={`${runtime.id}:${runtime.generation}`} runtime={runtime} /><AssetControlPanel runtime={runtime} /><AssetDiagnosticsPanel runtime={runtime} /><AssetFilesPanel runtime={runtime} /><VmDiagnosticsPanel runtime={runtime} /><RuntimeRemoteAccessPanel runtime={runtime} /><RemoteSessionsPanel runtimeId={runtime.id} /></> : null}
        {tab === 'link-policies' ? (
          <RuntimeLinkPolicyPanel
            assets={runtime.assets.map((asset) => ({ key: asset.key, name: asset.name }))}
            networks={runtime.networks.map((network) => ({ key: network.key, name: network.name }))}
            runtimeId={runtime.id}
          />
        ) : null}
        {tab === 'events' ? (
          <div className={styles.stack}>
            <TaskHistoryPanel key={`${runtime.id}:${runtime.generation}`} runtimeId={runtime.id} generation={runtime.generation} />
            <RuntimeEventPanel
              currentGeneration={runtime.generation}
              error={events.error}
              events={events.events}
              filters={eventFilters}
              loading={events.isLoading}
              onFiltersChange={setEventFilters}
            />
            <RuntimeLogPanel runtimeId={runtimeId} status={runtime.status} />
          </div>
        ) : null}
        {tab === 'traffic' ? (
          <div className={styles.stack}>
            <TrafficFlowPanel filters={flowFilters} flows={traffic.flows} onFiltersChange={setFlowFilters} />
            <TrafficPathPanel filters={pathFilters} onFiltersChange={setPathFilters} paths={traffic.paths} runtimeId={runtime.id} />
          </div>
        ) : null}
        {tab === 'capture' ? <CapturePanel networks={runtime.networks} runtimeId={runtime.id} /> : null}
      </div>

      <VNextConfirmDialog
        confirmLabel="确认重置"
        description="当前代资源会先被清理，再按同一发布版本创建下一代。"
        message="重置期间选手入口会关闭。"
        onClose={() => setResetOpen(false)}
        onConfirm={reset}
        open={resetOpen}
        title="重置运行环境"
        tone="primary"
      />
      <VNextConfirmDialog open={pauseOpen} onClose={() => setPauseOpen(false)} onConfirm={pauseOrResume} title={runtime.status === 'paused' ? '恢复运行环境' : '暂停运行环境'} confirmLabel="确认" description="保留原节点、网络和磁盘，按当前运行代次操作。" message={runtime.status === 'paused' ? '恢复后请核对服务健康状态。' : '暂停将中断当前业务连接。'} />
      <VNextConfirmDialog
        confirmLabel={cleanupPending ? '继续清理' : '确认销毁'}
        confirmationText={cleanupPending ? undefined : runtime.id.slice(0, 8)}
        description={
          cleanupPending ? '重新提交当前代资源的幂等清理任务。' : '所有分片、路由、抓包任务和临时资源将进入清理流程。'
        }
        message={cleanupPending ? '仅重试尚未完成的清理，不会创建新的运行资源。' : '销毁操作不可撤销。'}
        onClose={() => setDestroyOpen(false)}
        onConfirm={destroy}
        open={destroyOpen}
        title={cleanupPending ? '恢复运行时清理' : '销毁运行环境'}
      />
    </section>
  )
}
