import { ArrowRight, Trash2, Wrench } from 'lucide-react'
import { useMemo, useState } from 'react'
import { useNavigate } from 'react-router'
import useSWR from 'swr'
import { ActionButton, InlineFeedback, VNextConfirmDialog } from '../../../../shared/Interaction'
import { DataState } from '../../../../shared/Primitives'
import { errorMessage } from '../../../../shared/errors'
import { CursorPaginationBar, DataTable, RefreshIndicator, type AdminDataColumn } from '../../shared/AdminWorkbench'
import { formatAdminDate } from '../../shared/adminFormat'
import { useAdminCursorState } from '../../shared/useAdminCursorState'
import { teamLabAdminApi, teamLabAdminKeys, teamLabRuntimeApi, type TeamLabAdminRuntimeSummary } from '../api'
import { useTeamLabScene } from '../shared/TeamLabSceneShell'
import { TeamLabAccessStatusBadge, TeamLabRuntimeStatusBadge } from '../shared/TeamLabStatusBadge'
import { isRuntimeTerminal, isRuntimeTransitioning } from './runtimePresentation'
import styles from './TeamLabRuntimesPage.module.css'

const pageSize = 30

export function TeamLabRuntimesPage() {
  const { scene } = useTeamLabScene()
  const navigate = useNavigate()
  const cursor = useAdminCursorState(scene.id)
  const [destroying, setDestroying] = useState<TeamLabAdminRuntimeSummary | null>(null)
  const [destroyError, setDestroyError] = useState<unknown>(null)
  const [acting, setActing] = useState(false)
  const releases = useSWR(teamLabAdminKeys.releases(scene.id), () => teamLabAdminApi.listReleases(scene.id))
  const request = useSWR(
    [...teamLabAdminKeys.runtimes(scene.id), cursor.cursor ?? '', pageSize],
    () => teamLabAdminApi.listTrialRuntimes(scene.id, cursor.cursor ?? undefined, pageSize),
    {
      keepPreviousData: true,
      revalidateOnFocus: true,
      refreshInterval: (latest) => latest?.items.some((runtime) => isRuntimeTransitioning(runtime.status)) ? 6_000 : 0,
    }
  )
  const columns = useMemo<AdminDataColumn<TeamLabAdminRuntimeSummary>[]>(() => [
    {
      id: 'runtime',
      header: '运行实例',
      width: 'wide',
      render: (runtime) => <span className={styles.identity}><strong>{scene.definition.name}</strong><span>{formatAdminDate(runtime.createdAt)}</span></span>,
    },
    { id: 'status', header: '状态', width: 'compact', render: (runtime) => <TeamLabRuntimeStatusBadge status={runtime.status} /> },
    { id: 'access', header: '选手入口', render: (runtime) => <TeamLabAccessStatusBadge open={runtime.openForAccess} /> },
    { id: 'release', header: '发布版本', visibility: 'desktop', render: (runtime) => { const release = releases.data?.find(item => item.id === runtime.releaseId); return release ? `v${release.version}` : '—' } },
    { id: 'updated', header: '最后更新', visibility: 'desktop', render: (runtime) => formatAdminDate(runtime.updatedAt ?? runtime.createdAt) },
    {
      id: 'actions', header: '操作', width: 'wide', align: 'right', render: (runtime) => (
        <div className={styles.rowActions} onClick={(event) => event.stopPropagation()}>
          <ActionButton aria-label="进入资产运维" icon={<Wrench size={15} />} onClick={() => navigate(`/admin/teamlab/${scene.id}/runtimes/${runtime.id}?tab=operations`)} title="进入资产运维" type="button" />
          <ActionButton aria-label={isWaitingForNode(runtime.status) ? '取消创建' : '销毁运行实例'}
            disabled={isRuntimeTerminal(runtime.status) || runtime.status === 'destroying' || runtime.status === 'cleanup-pending' || isNodeExecutionActive(runtime.status)}
            icon={<Trash2 size={15} />} onClick={() => { setDestroyError(null); setDestroying(runtime) }}
            title={isWaitingForNode(runtime.status) ? '取消创建' : '销毁运行实例'} tone="danger" type="button" />
          <ArrowRight aria-hidden="true" size={16} />
        </div>
      ),
    },
  ], [navigate, scene.id, scene.definition.name, releases.data])

  const destroy = async () => {
    if (!destroying || acting) return false
    setActing(true)
    setDestroyError(null)
    try {
      await teamLabRuntimeApi.destroyRuntime(destroying.id)
      await request.mutate()
      setDestroying(null)
      return true
    } catch (error) {
      setDestroyError(error)
      return false
    } finally {
      setActing(false)
    }
  }

  return (
    <section className={styles.page}>
      <header className={styles.pageHeader}>
        <div><h2>试运行</h2></div>
        <RefreshIndicator active={request.isValidating && (request.data?.items.some((runtime) => isRuntimeTransitioning(runtime.status)) ?? false)} label={request.isValidating ? '同步中' : '状态已同步'} />
      </header>
      {!request.data && !request.error ? <DataState description="正在读取场景试运行记录。" loading title="试运行加载中" /> : request.error ? (
        <InlineFeedback tone="danger">{errorMessage(request.error, '试运行记录加载失败。')}</InlineFeedback>
      ) : request.data?.items.length ? (
        <>
          <DataTable
            caption={`${scene.definition.name} 的试运行记录`}
            columns={columns}
            onRowClick={(runtime) => navigate(`/admin/teamlab/${scene.id}/runtimes/${runtime.id}`)}
            rowKey={(runtime) => runtime.id}
            rows={[...request.data.items]}
          />
          <CursorPaginationBar
            hasNext={Boolean(request.data.nextCursor)}
            label="试运行记录分页"
            onNext={() => request.data?.nextCursor && cursor.next(request.data.nextCursor)}
            onPrevious={cursor.previous}
            page={cursor.page}
          />
        </>
      ) : (
        <DataState description="从已就绪的发布版本创建试运行后，记录会出现在这里。" title="暂无试运行" />
      )}
      <VNextConfirmDialog
        confirmLabel={destroying && isWaitingForNode(destroying.status) ? '确认取消' : '确认销毁'}
        message={destroyError ? <InlineFeedback tone="danger">{errorMessage(destroyError, '无法提交操作。')}</InlineFeedback> : destroying && isWaitingForNode(destroying.status) ? '取消后可从发布版本重新创建。' : '销毁不可撤销。'}
        onClose={() => !acting && setDestroying(null)}
        onConfirm={destroy}
        open={destroying !== null}
        title={destroying && isWaitingForNode(destroying.status) ? '取消创建' : '销毁运行实例'}
        tone="danger"
      />
    </section>
  )
}

function isWaitingForNode(status: TeamLabAdminRuntimeSummary['status']) {
  return status === 'pending' || status === 'planning'
}

function isNodeExecutionActive(status: TeamLabAdminRuntimeSummary['status']) {
  return status === 'scheduled' || status === 'deploying' || status === 'probing'
}
