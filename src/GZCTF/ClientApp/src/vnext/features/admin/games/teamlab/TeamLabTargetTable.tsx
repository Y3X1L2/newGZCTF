import { ArrowRight, ExternalLink } from 'lucide-react'
import { memo, useMemo } from 'react'
import { InlineFeedback } from '../../../../shared/Interaction'
import { DataState } from '../../../../shared/Primitives'
import { errorMessage } from '../../../../shared/errors'
import { CursorPaginationBar, DataTable, RefreshIndicator, StatusBadge, type AdminDataColumn } from '../../shared/AdminWorkbench'
import { formatAdminDate } from '../../shared/adminFormat'
import type { TeamLabGameTarget } from '../../api/teamlabGameAdminApi'
import { targetStatusMeta } from './teamLabGamePresentation'
import type { useGameTeamLab } from './useGameTeamLab'
import styles from './TeamLabGame.module.css'

type TargetState = ReturnType<typeof useGameTeamLab>['targets']

export const TeamLabTargetTable = memo(function TeamLabTargetTable({
  targets,
  onSelect,
}: {
  targets: TargetState
  onSelect: (target: TeamLabGameTarget) => void
}) {
  const columns = useMemo<AdminDataColumn<TeamLabGameTarget>[]>(() => [
    {
      id: 'team',
      header: '请求对象',
      width: 'wide',
      render: (target) => <span className={styles.identityCell}><strong>{target.displayName}</strong><small>队伍 #{target.teamId}</small></span>,
    },
    {
      id: 'status',
      header: '准备状态',
      render: (target) => {
        const meta = targetStatusMeta[target.status]
        return <StatusBadge pulse={meta.active} tone={meta.tone}>{meta.label}</StatusBadge>
      },
    },
    {
      id: 'runtime',
      header: '运行环境',
      width: 'wide',
      render: (target) => target.runtimeId ? <span className={styles.identityCell}><code>{target.runtimeId}</code><small>{target.runtimeStage ?? '等待运行状态'}</small></span> : '尚未创建',
    },
    { id: 'updated', header: '最后更新', visibility: 'desktop', render: (target) => formatAdminDate(target.updatedAt) },
    { id: 'open', header: '', width: 'compact', align: 'right', render: (target) => target.runtimeId ? <ExternalLink aria-hidden="true" size={15} /> : <ArrowRight aria-hidden="true" size={15} /> },
  ], [])

  return (
    <section className={styles.targetsSection} aria-labelledby="teamlab-targets-title">
      <header className={styles.sectionHeader}>
        <div><span>TEAM TARGETS</span><h2 id="teamlab-targets-title">队伍环境</h2></div>
        <RefreshIndicator active={targets.isRefreshing} label={targets.isRefreshing ? '同步中' : '状态已同步'} />
      </header>
      {targets.isLoading ? <DataState description="正在读取服务端队伍目标分页。" loading title="队伍环境加载中" /> : targets.error ? (
        <InlineFeedback tone="danger">{errorMessage(targets.error, '队伍环境加载失败。')}</InlineFeedback>
      ) : targets.page?.items.length ? (
        <>
          <DataTable caption="比赛 TeamLab 队伍环境" columns={columns} onRowClick={onSelect} rowKey={(target) => target.id} rows={[...targets.page.items]} />
          <CursorPaginationBar
            hasNext={Boolean(targets.page.nextCursor)}
            label="队伍环境分页"
            onNext={() => targets.page?.nextCursor && targets.cursor.next(targets.page.nextCursor)}
            onPrevious={targets.cursor.previous}
            page={targets.cursor.page}
          />
        </>
      ) : <DataState description="准备比赛环境后，已通过报名审核的队伍会形成部署目标。" title="暂无队伍目标" />}
    </section>
  )
})
