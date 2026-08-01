import { ArrowRight, PlayCircle, Trash2, Wrench } from 'lucide-react'
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
import { TeamLabRuntimeStatusBadge } from '../shared/TeamLabStatusBadge'
import { isRuntimeTerminal } from './runtimePresentation'
import styles from './TeamLabRuntimesPage.module.css'

const pageSize = 30

export function TeamLabRuntimesPage() {
  const { scene } = useTeamLabScene()
  const navigate = useNavigate()
  const cursor = useAdminCursorState(scene.id)
  const [destroying, setDestroying] = useState<TeamLabAdminRuntimeSummary | null>(null)
  const [destroyError, setDestroyError] = useState<unknown>(null)
  const [acting, setActing] = useState(false)
  const request = useSWR(
    [...teamLabAdminKeys.runtimes(scene.id), cursor.cursor ?? '', pageSize],
    () => teamLabAdminApi.listTrialRuntimes(scene.id, cursor.cursor ?? undefined, pageSize),
    {
      keepPreviousData: true,
      revalidateOnFocus: true,
      refreshInterval: (latest) => latest?.items.some((runtime) => !isRuntimeTerminal(runtime.status)) ? 6_000 : 0,
    }
  )
  const columns = useMemo<AdminDataColumn<TeamLabAdminRuntimeSummary>[]>(() => [
    {
      id: 'runtime',
      header: '运行实例',
      width: 'wide',
      render: (runtime) => <span className={styles.identity}><strong>{runtime.stage}</strong><code>{runtime.id}</code></span>,
    },
    { id: 'status', header: '状态', width: 'compact', render: (runtime) => <TeamLabRuntimeStatusBadge status={runtime.status} /> },
    { id: 'access', header: '选手入口', render: (runtime) => runtime.openForAccess ? '已开放' : '未开放' },
    { id: 'release', header: '发布版本', visibility: 'desktop', render: (runtime) => <code>{runtime.releaseId}</code> },
    { id: 'updated', header: '最后更新', visibility: 'desktop', render: (runtime) => formatAdminDate(runtime.updatedAt ?? runtime.createdAt) },
    {
      id: 'actions', header: '操作', width: 'wide', align: 'right', render: (runtime) => (
        <div className={styles.rowActions} onClick={(event) => event.stopPropagation()}>
          <ActionButton aria-label="进入资产运维" icon={<Wrench size={15} />} onClick={() => navigate(`/admin/teamlab/${scene.id}/runtimes/${runtime.id}?tab=operations`)} title="进入资产运维" type="button" />
          <ActionButton aria-label="销毁运行实例" disabled={isRuntimeTerminal(runtime.status) || runtime.status === 'destroying' || runtime.status === 'cleanup-pending'} icon={<Trash2 size={15} />} onClick={() => { setDestroyError(null); setDestroying(runtime) }} title="销毁运行实例" tone="danger" type="button" />
          <ArrowRight aria-hidden="true" size={16} />
        </div>
      ),
    },
  ], [navigate, scene.id])

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
        <div><span>TRIAL RUNTIMES</span><h2>试运行管理</h2><p>查看当前场景各发布版本的部署、分片和观测状态。</p></div>
        <RefreshIndicator active={request.isValidating && Boolean(request.data)} label={request.isValidating ? '同步中' : '状态已同步'} />
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
      <footer className={styles.note}><PlayCircle size={15} />试运行创建入口位于“发布版本”页。</footer>
      <VNextConfirmDialog
        confirmationText={destroying?.id.slice(0, 8)}
        confirmLabel="确认销毁"
        description="将清理该实例的分片、资产、路由、抓包和访问授权。"
        message={destroyError ? <InlineFeedback tone="danger">{errorMessage(destroyError, '无法提交销毁任务。')}</InlineFeedback> : '销毁不可撤销。'}
        onClose={() => !acting && setDestroying(null)}
        onConfirm={destroy}
        open={destroying !== null}
        title="销毁运行实例"
        tone="danger"
      />
    </section>
  )
}
