import { ArrowRight, PlayCircle } from 'lucide-react'
import { useMemo } from 'react'
import { useNavigate } from 'react-router'
import useSWR from 'swr'
import { InlineFeedback } from '../../../../shared/Interaction'
import { DataState } from '../../../../shared/Primitives'
import { errorMessage } from '../../../../shared/errors'
import { CursorPaginationBar, DataTable, RefreshIndicator, type AdminDataColumn } from '../../shared/AdminWorkbench'
import { formatAdminDate } from '../../shared/adminFormat'
import { useAdminCursorState } from '../../shared/useAdminCursorState'
import { teamLabAdminApi, teamLabAdminKeys, type TeamLabAdminRuntimeSummary } from '../api'
import { useTeamLabScene } from '../shared/TeamLabSceneShell'
import { TeamLabRuntimeStatusBadge } from '../shared/TeamLabStatusBadge'
import { isRuntimeTerminal } from './runtimePresentation'
import styles from './TeamLabRuntimesPage.module.css'

const pageSize = 30

export function TeamLabRuntimesPage() {
  const { scene } = useTeamLabScene()
  const navigate = useNavigate()
  const cursor = useAdminCursorState(scene.id)
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
    { id: 'open', header: '', width: 'compact', align: 'right', render: () => <ArrowRight aria-hidden="true" size={16} /> },
  ], [])

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
    </section>
  )
}
