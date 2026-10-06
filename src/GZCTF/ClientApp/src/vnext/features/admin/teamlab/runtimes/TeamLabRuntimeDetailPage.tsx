import { ArrowLeft, CheckCircle2, FileClock, Network, Pause, Play, RefreshCw, RotateCcw, Shield, Trash2 } from 'lucide-react'
import { ActionMenu } from '../../../../shared/ActionMenu'
import { useCallback, useState } from 'react'
import { Link, useLocation, useParams, useSearchParams } from 'react-router'
import { ActionButton, InlineFeedback, VNextConfirmDialog, VNextDrawer } from '../../../../shared/Interaction'
import { DataState } from '../../../../shared/Primitives'
import { errorMessage } from '../../../../shared/errors'
import { useVNextPageTitle } from '../../../../shared/useVNextPageTitle'
import { formatAdminDate } from '../../shared/adminFormat'
import { teamLabRuntimeApi } from '../api'
import { TeamLabRuntimeStatusBadge } from '../shared/TeamLabStatusBadge'
import { RuntimeAssetWorkspace } from './RuntimeAssetWorkspace'
import { RuntimeActivityWorkspace } from './RuntimeActivityWorkspace'
import { RuntimeAccessWorkspace } from './RuntimeAccessWorkspace'
import { RuntimeDifferencesPanel } from './RuntimeDifferencesPanel'
import { RuntimeGrantPanel } from './RuntimeGrantPanel'
import { RuntimeShardTable } from './RuntimeShardTable'
import { RuntimeStageTimeline } from './RuntimeStageTimeline'
import { RuntimeTaskPanel } from './RuntimeTaskPanel'
import { useTeamLabRuntime } from './useTeamLabRuntime'
import { useRuntimeUpdatePreview } from './useRuntimeUpdatePreview'
import { emptyTeamLabEventFilters, type TeamLabEventFilters } from './useRuntimeEvents'
import { isRuntimeTransitioning } from './runtimePresentation'
import styles from './TeamLabRuntimeDetailPage.module.css'

type RuntimeTab = 'assets' | 'access' | 'activity'
const tabs = [
  { key: 'assets', label: '机器与网络', icon: Network }, { key: 'access', label: '访问方式', icon: Shield },
  { key: 'activity', label: '操作记录', icon: FileClock },
] as const

export function TeamLabRuntimeDetailPage() {
  const { runtimeId = '' } = useParams()
  const location = useLocation()
  const [params, setParams] = useSearchParams()
  const requestedTab = params.get('tab')
  const tab: RuntimeTab = requestedTab === 'events' || requestedTab === 'logs' || requestedTab === 'capture' ? 'activity'
    : requestedTab === 'overview' || requestedTab === 'network' || requestedTab === 'operations' || requestedTab === 'traffic' ? 'assets'
    : tabs.some(item => item.key === requestedTab) ? requestedTab as RuntimeTab : 'assets'
  const [confirmation, setConfirmation] = useState<'reset' | 'destroy' | 'update' | null>(null)
  const [drawer, setDrawer] = useState<'deployment' | 'grants' | 'check' | null>(null)
  const [acting, setActing] = useState(false)
  const [actionError, setActionError] = useState<unknown>(null)
  const [eventFilters, setEventFilters] = useState<TeamLabEventFilters>(emptyTeamLabEventFilters)
  const state = useTeamLabRuntime(runtimeId)
  const runtime = state.runtime
  const sceneId = runtime?.topologyId || ''
  const update = useRuntimeUpdatePreview(sceneId, runtimeId, runtime?.releaseId, !!sceneId && !runtime?.managedRolloutId)
  useVNextPageTitle(`${runtime?.topologyName || '运行环境'} · 运行环境`)

  const selectTab = useCallback((nextTab: RuntimeTab, assetId?: number) => setParams(current => {
    const next = new URLSearchParams(current)
    next.set('tab', nextTab)
    if (assetId !== undefined) next.set('asset', String(assetId))
    return next
  }), [setParams])
  const inspect = (filters: TeamLabEventFilters) => { setEventFilters(filters); selectTab('activity') }
  const act = async (action: 'reset' | 'destroy' | 'pause' | 'update') => {
    if (!runtime || acting) return false
    setActing(true)
    setActionError(null)
    try {
      const version = { releaseId: update.latestRelease?.id ?? runtime.releaseId, overlays: null }
      const next = action === 'destroy' ? await teamLabRuntimeApi.destroyRuntime(runtime.id)
        : action === 'pause' ? runtime.status === 'paused'
          ? await teamLabRuntimeApi.resumeRuntime(runtime.id) : await teamLabRuntimeApi.pauseRuntime(runtime.id)
        : action === 'update' ? update.preview?.canApply
          ? await teamLabRuntimeApi.updateRuntime(runtime.id, version) : await teamLabRuntimeApi.resetRuntime(runtime.id, version)
        : await teamLabRuntimeApi.resetRuntime(runtime.id, { releaseId: null, overlays: null })
      await state.mutate(next, { revalidate: false })
      return true
    } catch (error) { setActionError(error); return false }
    finally { setActing(false) }
  }

  if (state.isLoading) return <section className={styles.page}><DataState loading title="正在读取运行环境" /></section>
  if (!runtime || state.error) return <section className={styles.page}><DataState description={errorMessage(state.error, '运行环境加载失败。')} title="无法打开运行环境" />
    <ActionButton icon={<RefreshCw size={16} />} onClick={() => void state.mutate()} type="button">重新读取</ActionButton></section>
  const queueActive = !!runtime.queueStatus && ['pending', 'scheduling', 'scheduled', 'running'].includes(runtime.queueStatus)
  const canCancel = runtime.queueStatus === 'pending' || runtime.queueStatus === 'scheduling'
  const canReset = !runtime.managedRolloutId && !queueActive && ['running', 'failed', 'paused'].includes(runtime.status)
  const cleanup = runtime.status === 'cleanup-pending'
  const canDestroy = !runtime.managedRolloutId && !['scheduled', 'running'].includes(runtime.queueStatus ?? '') && !['destroying', 'destroyed'].includes(runtime.status)
  const hasUpdate = update.latestRelease && update.latestRelease.id !== runtime.releaseId
  const canUpdate = hasUpdate && update.preview && (update.preview.canApply ? runtime.status === 'running' && !queueActive : canReset)
  const transitioning = isRuntimeTransitioning(runtime.status)
  const requestedReturn = (location.state as { returnTo?: unknown } | null)?.returnTo
  const hasListReturn = typeof requestedReturn === 'string' &&
    (requestedReturn.startsWith('/admin/teamlab?view=runtimes') || /^\/admin\/teamlab\/runtimes(?:\?|$)/.test(requestedReturn))
  const listReturn = hasListReturn ? requestedReturn as string : '/admin/teamlab/runtimes'
  return <section className={styles.page}>
    <Link className={styles.backLink} to={hasListReturn || params.get('from') === 'runtime-search' || !sceneId ? listReturn : `/admin/teamlab/${sceneId}/releases`}><ArrowLeft size={16} />{sceneId && !hasListReturn && params.get('from') !== 'runtime-search' ? '版本与启动' : '运行环境'}</Link>
    <header className={styles.pageHeader}>
      <div><h2>{runtime.topologyName || '运行环境'}</h2><div className={styles.identity}><TeamLabRuntimeStatusBadge status={runtime.status} />
        <span>{runtime.releaseVersion ? `v${runtime.releaseVersion}` : '已发布版本'} · {formatAdminDate(runtime.createdAt)}</span>
        {sceneId ? <Link to={`/admin/teamlab/${sceneId}/releases`}>查看场景版本</Link> : <span>无场景来源</span>}</div>
        {runtime.managedRolloutId ? <p className={styles.ownership}>此环境由所属课程或赛事管理，生命周期操作须返回来源业务处理。</p> : null}</div>
      <div className={styles.actions}>
        <ActionButton aria-label="刷新运行状态" title="刷新运行状态" disabled={state.isRefreshing} icon={<RefreshCw size={17} />} onClick={() => void state.mutate()} type="button" />
        {['running', 'paused'].includes(runtime.status) ? <ActionButton disabled={acting || queueActive || !!runtime.managedRolloutId}
          icon={runtime.status === 'paused' ? <Play size={16} /> : <Pause size={16} />} onClick={() => void act('pause')} type="button">{runtime.status === 'paused' ? '恢复' : '暂停'}</ActionButton> : null}
        {(runtime.status === 'failed' && canReset) || cleanup ? <ActionButton disabled={acting} icon={<RotateCcw size={16} />}
          onClick={() => setConfirmation(cleanup ? 'destroy' : 'reset')} tone="primary" type="button">{cleanup ? '继续清理' : '重新部署'}</ActionButton> : null}
        <ActionMenu label="更多环境操作" items={[
          { label: '访问权限', icon: <Shield size={15} />, onSelect: () => setDrawer('grants') },
          { label: '运行状态检查', icon: <CheckCircle2 size={15} />, onSelect: () => setDrawer('check') },
          { label: '部署详情', onSelect: () => setDrawer('deployment') },
          ...(hasUpdate ? [{ label: `更新到 v${update.latestRelease!.version}`, disabled: !canUpdate || acting, onSelect: () => setConfirmation('update') }] : []),
          { label: '重置环境', disabled: !canReset || acting, separator: true, icon: <RotateCcw size={15} />, onSelect: () => setConfirmation('reset') },
          { label: canCancel ? '取消创建' : cleanup ? '继续清理' : '销毁环境', disabled: !canDestroy || acting, danger: true, icon: <Trash2 size={15} />, onSelect: () => setConfirmation('destroy') },
        ]} />
      </div>
    </header>
    {transitioning || runtime.failure || runtime.error ? <RuntimeTaskPanel runtime={runtime} onInspect={() => inspect({ generation: runtime.generation, stage: '' })} /> : null}
    {actionError ? <InlineFeedback tone="danger">{errorMessage(actionError, '环境操作失败。')}</InlineFeedback> : null}
    <nav aria-label="运行环境详情" className={styles.tabs}>{tabs.map(({ key, label, icon: Icon }) => <button key={key} aria-current={tab === key ? 'page' : undefined}
      onClick={() => selectTab(key)} type="button"><Icon size={17} />{label}</button>)}</nav>
    <div className={styles.content}>
      {tab === 'assets' ? <>{transitioning ? <RuntimeStageTimeline runtime={runtime} /> : null}<RuntimeAssetWorkspace key={`${runtime.id}:${runtime.generation}`} runtime={runtime} /></> : null}
      {tab === 'access' ? <RuntimeAccessWorkspace key={`${runtime.id}:${runtime.generation}`} runtime={runtime} /> : null}
      {tab === 'activity' ? <RuntimeActivityWorkspace runtime={runtime} eventFilters={eventFilters} onFiltersChange={setEventFilters}
        initialDetail={requestedTab === 'events' || requestedTab === 'logs' || requestedTab === 'capture' ? requestedTab : undefined} /> : null}
    </div>
    <VNextDrawer eyebrow="" open={drawer !== null} onClose={() => setDrawer(null)} title={drawer === 'grants' ? '访问权限' : drawer === 'check' ? '运行状态检查' : '部署详情'}>
      {drawer === 'grants' ? <RuntimeGrantPanel runtime={runtime} /> : drawer === 'check' ? <RuntimeDifferencesPanel runtime={runtime} /> : drawer === 'deployment' ? <>
        <dl className={styles.deploymentFacts}><div><dt>运行环境</dt><dd>{runtime.id}</dd></div><div><dt>代次 / 修订</dt><dd>{runtime.generation} / {runtime.planRevision ?? 0}</dd></div>
          <div><dt>当前任务</dt><dd>{runtime.deploymentQueueTicketId ?? '无'}</dd></div><div><dt>更新时间</dt><dd>{formatAdminDate(runtime.updatedAt ?? runtime.createdAt)}</dd></div></dl>
        <RuntimeShardTable runtime={runtime} onInspectFailure={filters => { setDrawer(null); inspect(filters) }} />
      </> : null}
    </VNextDrawer>
    <VNextConfirmDialog open={confirmation !== null} onClose={() => setConfirmation(null)} onConfirm={() => act(confirmation!)}
      title={confirmation === 'update' ? `更新到 v${update.latestRelease?.version}` : confirmation === 'reset' ? '重置环境' : canCancel ? '取消创建' : cleanup ? '继续清理' : '销毁环境'}
      confirmLabel={confirmation === 'destroy' ? canCancel ? '取消创建' : cleanup ? '继续清理' : '销毁环境' : '确认'} tone={confirmation === 'destroy' ? 'danger' : 'primary'}
      message={confirmation === 'reset' ? '资产数据会清除，环境将按当前发布版本重新创建。'
        : confirmation === 'destroy' ? canCancel ? '取消尚未执行的创建任务。' : cleanup ? '继续清理未完成的资源。' : '环境中的资产、网络和访问地址将一并删除。'
        : update.preview?.canApply ? '更新发生变化的资产，保留现有网络。' : update.preview?.resetRequiredReason ?? '更新需要重新创建环境。'} />
  </section>
}
