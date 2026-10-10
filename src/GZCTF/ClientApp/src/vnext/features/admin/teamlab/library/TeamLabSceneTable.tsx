import { Boxes, Network, PlayCircle, Trash2 } from 'lucide-react'
import { memo, useMemo } from 'react'
import { Link, useNavigate } from 'react-router'
import { DataState } from '../../../../shared/Primitives'
import type { TeamLabAdminSceneSummary } from '../api'
import { DataTable, type AdminDataColumn } from '../../shared/AdminWorkbench'
import { formatAdminDate } from '../../shared/adminFormat'
import { TeamLabRuntimeStatusBadge } from '../shared/TeamLabStatusBadge'
import styles from './TeamLabLibraryPage.module.css'
import { ActionButton } from '../../../../shared/Interaction'

function SceneActions({ scene, onDelete }: { scene: TeamLabAdminSceneSummary; onDelete: (scene: TeamLabAdminSceneSummary) => void }) {
  const deleteBlocked = scene.gameReferenceCount > 0 ? '请先解除比赛引用' : scene.latestTrialRuntime ? '请先删除该场景的运行记录' : undefined
  return <div className={styles.sceneActions} onClick={(event) => event.stopPropagation()} onKeyDown={(event) => event.stopPropagation()}>
    <Link aria-label={`设计 ${scene.name}`} to={`/admin/teamlab/${scene.id}/design`} title="设计"><Network size={16} /><span>设计</span></Link>
    <Link aria-label={`版本与启动 ${scene.name}`} to={`/admin/teamlab/${scene.id}/releases`} title="版本与启动"><Boxes size={16} /><span>版本与启动</span></Link>
    {scene.latestTrialRuntime ? <Link aria-label={`查看环境 ${scene.name}`} to={`/admin/teamlab/runtimes/${scene.latestTrialRuntime.id}`} title="查看环境"><PlayCircle size={16} /><span>查看环境</span></Link> : <span className={styles.muted}>暂无环境</span>}
    <ActionButton aria-label={`删除场景 ${scene.name}`} disabled={!!deleteBlocked} title={deleteBlocked || '删除场景'} icon={<Trash2 size={15} />} onClick={() => onDelete(scene)} tone="danger" type="button">删除</ActionButton>
    {deleteBlocked ? <small className={styles.muted}>{deleteBlocked}</small> : null}
  </div>
}

export const TeamLabSceneTable = memo(function TeamLabSceneTable({
  scenes,
  onDelete,
}: {
  scenes: readonly TeamLabAdminSceneSummary[]
  onDelete: (scene: TeamLabAdminSceneSummary) => void
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
        render: (scene) => <SceneActions scene={scene} onDelete={onDelete} />,
      },
    ],
    [onDelete]
  )

  return <>
    <div className={styles.desktopSceneTable}><DataTable
      caption="TeamLab 场景库"
      columns={columns}
      emptyDescription="调整搜索或筛选条件，或者创建新的场景。"
      emptyTitle="没有匹配的组网场景"
      onRowClick={(scene) => navigate(`/admin/teamlab/${scene.id}/design`)}
      rowKey={(scene) => scene.id}
      rows={[...scenes]}
    /></div>
    <div className={styles.mobileSceneList} aria-label="TeamLab 场景库">
      {scenes.length ? scenes.map(scene => <article className={styles.mobileScene} key={scene.id}>
        <h3><Link to={`/admin/teamlab/${scene.id}/design`}>{scene.name}</Link></h3>
        <p>{scene.latestRelease ? `最新发布 v${scene.latestRelease.version}` : '尚未发布'} · {scene.networkCount} 网段 · {scene.assetCount} 资产</p>
        <p>{!scene.latestRelease ? '未发布草稿' : scene.revision === scene.latestRelease.sourceRevision ? '草稿与最新发布一致' : '草稿有待发布修改'}</p>
        {scene.latestTrialRuntime ? <TeamLabRuntimeStatusBadge status={scene.latestTrialRuntime.status} /> : null}
        <SceneActions scene={scene} onDelete={onDelete} />
      </article>) : <DataState description="调整筛选条件，或新建场景。" title="暂无匹配场景" />}
    </div>
  </>
})
