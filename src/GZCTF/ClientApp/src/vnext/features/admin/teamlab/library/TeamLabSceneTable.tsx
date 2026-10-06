import { ArrowUpRight, Boxes, Network, PlayCircle } from 'lucide-react'
import { memo, useMemo } from 'react'
import { Link, useNavigate } from 'react-router'
import type { TeamLabAdminSceneSummary } from '../api'
import { DataTable, type AdminDataColumn } from '../../shared/AdminWorkbench'
import { formatAdminDate } from '../../shared/adminFormat'
import { TeamLabRuntimeStatusBadge } from '../shared/TeamLabStatusBadge'
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
            <small>{scene.latestRelease ? `最新发布 v${scene.latestRelease.version}` : '尚未发布'}</small>
          </div>
        ),
      },
      {
        id: 'draft',
        header: '设计草稿',
        width: 'medium',
        render: (scene) => <span>{!scene.latestRelease ? '未发布草稿' : scene.revision === scene.latestRelease.sourceRevision ? '与最新发布一致' : '有待发布修改'}{scene.validation?.revision === scene.revision && !scene.validation.valid ? ' · 校验未通过' : ''}</span>,
      },
      {
        id: 'resources',
        header: '拓扑规模',
        width: 'medium',
        render: (scene) => `${scene.networkCount} 网段 · ${scene.assetCount} 资产`,
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
        header: '最近运行环境',
        width: 'medium',
        visibility: 'wide',
        render: (scene) => scene.latestTrialRuntime
          ? <TeamLabRuntimeStatusBadge status={scene.latestTrialRuntime.status} />
          : <span className={styles.muted}>无记录</span>,
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
        width: 'wide',
        align: 'right',
        render: (scene) => (
          <div className={styles.sceneActions} onClick={(event) => event.stopPropagation()}>
            <Link aria-label={`设计 ${scene.name}`} to={`/admin/teamlab/${scene.id}/design`} title="设计"><Network size={16} /><span>设计</span></Link>
            <Link aria-label={`版本与启动 ${scene.name}`} to={`/admin/teamlab/${scene.id}/releases`} title="版本与启动"><Boxes size={16} /><span>版本与启动</span></Link>
            {scene.latestTrialRuntime ? <Link aria-label={`查看环境 ${scene.name}`} to={`/admin/teamlab/runtimes/${scene.latestTrialRuntime.id}`} title="查看环境"><PlayCircle size={16} /><span>查看环境</span></Link> : <span className={styles.muted}>暂无环境</span>}
            <ArrowUpRight aria-hidden="true" size={15} />
          </div>
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
