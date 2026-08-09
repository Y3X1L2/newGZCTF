import { ArrowUpRight } from 'lucide-react'
import { memo, useMemo } from 'react'
import { useNavigate } from 'react-router'
import type { TeamLabAdminSceneSummary } from '../api'
import { DataTable, type AdminDataColumn } from '../../shared/AdminWorkbench'
import { formatAdminDate } from '../../shared/adminFormat'
import { TeamLabRuntimeStatusBadge, TeamLabSceneStatusBadge } from '../shared/TeamLabStatusBadge'
import styles from './TeamLabLibraryPage.module.css'

export const TeamLabSceneTable = memo(function TeamLabSceneTable({
  scenes,
}: {
  scenes: readonly TeamLabAdminSceneSummary[]
}) {
  const navigate = useNavigate()
  const columns = useMemo<AdminDataColumn<TeamLabAdminSceneSummary>[]>(
    () => [
      {
        id: 'scene',
        header: '场景',
        width: 'wide',
        render: (scene) => (
          <div className={styles.sceneIdentity}>
            <strong>{scene.name}</strong>
            <small>修订 {scene.revision} · Schema {scene.schemaVersion}</small>
          </div>
        ),
      },
      {
        id: 'status',
        header: '状态',
        width: 'compact',
        render: (scene) => <TeamLabSceneStatusBadge scene={scene} />,
      },
      {
        id: 'resources',
        header: '拓扑规模',
        width: 'medium',
        render: (scene) => `${scene.networkCount} 网段 · ${scene.assetCount} 资产 · ${scene.infrastructureCount} 设施`,
      },
      {
        id: 'owner',
        header: '所有者',
        width: 'medium',
        visibility: 'desktop',
        render: (scene) => scene.ownerDisplayName,
      },
      {
        id: 'runtime',
        header: '最近试运行',
        width: 'medium',
        visibility: 'wide',
        render: (scene) => scene.latestTrialRuntime
          ? <TeamLabRuntimeStatusBadge status={scene.latestTrialRuntime.status} />
          : <span className={styles.muted}>未运行</span>,
      },
      {
        id: 'usage',
        header: '比赛引用',
        width: 'compact',
        visibility: 'wide',
        render: (scene) => scene.gameReferenceCount,
      },
      {
        id: 'updated',
        header: '更新时间',
        width: 'medium',
        visibility: 'desktop',
        render: (scene) => <time className={styles.mono}>{formatAdminDate(scene.updatedAt, false)}</time>,
      },
      {
        id: 'action',
        header: '操作',
        width: 'compact',
        align: 'right',
        render: (scene) => (
          <button
            aria-label={`打开 ${scene.name}`}
            className={styles.iconButton}
            onClick={() => navigate(`/admin/teamlab/${scene.id}/design`)}
            title="打开场景"
            type="button"
          >
            <ArrowUpRight size={16} />
          </button>
        ),
      },
    ],
    [navigate]
  )

  return (
    <DataTable
      caption="TeamLab 场景库"
      columns={columns}
      emptyDescription="调整搜索或筛选条件，或者创建新的场景。"
      emptyTitle="没有匹配的组网场景"
      onRowClick={(scene) => navigate(`/admin/teamlab/${scene.id}/design`)}
      rowKey={(scene) => scene.id}
      rows={[...scenes]}
    />
  )
})
