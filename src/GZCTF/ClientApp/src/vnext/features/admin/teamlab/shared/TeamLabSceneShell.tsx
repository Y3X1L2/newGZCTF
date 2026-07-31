import { ArrowLeft, Boxes, Network, PlayCircle } from 'lucide-react'
import { createContext, useContext } from 'react'
import { Link, NavLink, Outlet, useParams } from 'react-router'
import useSWR from 'swr'
import { InlineFeedback } from '../../../../shared/Interaction'
import { DataState } from '../../../../shared/Primitives'
import { errorMessage } from '../../../../shared/errors'
import { useVNextPageTitle } from '../../../../shared/useVNextPageTitle'
import { AdminPageHeader, StatusBadge } from '../../shared/AdminWorkbench'
import { teamLabAdminApi, teamLabAdminKeys, type TeamLabTopologyDetail } from '../api'
import styles from './TeamLabSceneShell.module.css'

interface TeamLabSceneContextValue {
  scene: TeamLabTopologyDetail
}

const TeamLabSceneContext = createContext<TeamLabSceneContextValue | null>(null)

export function useTeamLabScene() {
  const context = useContext(TeamLabSceneContext)
  if (!context) throw new Error('useTeamLabScene must be used inside TeamLabSceneShell.')
  return context
}

export function TeamLabSceneShell() {
  const { topologyId = '' } = useParams()
  const request = useSWR(
    topologyId ? teamLabAdminKeys.topology(topologyId) : null,
    () => teamLabAdminApi.getTopology(topologyId),
    { keepPreviousData: true, revalidateOnFocus: true }
  )

  useVNextPageTitle(request.data?.definition.name ?? 'TeamLab 场景')

  if (!topologyId) return <DataState description="场景标识无效。" title="无法打开场景" />
  if (!request.data && !request.error)
    return <DataState description="正在读取场景结构和修订信息。" loading title="场景加载中" />
  if (request.error || !request.data)
    return (
      <div className={styles.statePage}>
        <InlineFeedback tone="danger">{errorMessage(request.error, '场景加载失败。')}</InlineFeedback>
        <DataState description="请返回场景库检查访问权限或场景状态。" title="无法打开场景" />
      </div>
    )

  const scene = request.data
  return (
    <TeamLabSceneContext.Provider value={{ scene }}>
      <div className={styles.page}>
        <Link className={styles.backLink} to="/admin/teamlab">
          <ArrowLeft size={16} />
          场景库
        </Link>
        <AdminPageHeader
          actions={<StatusBadge tone="neutral">修订 {scene.revision}</StatusBadge>}
          description={`${scene.definition.networks.length} 个网段 · ${scene.definition.assets.length} 个资产 · ${scene.definition.infrastructure.length} 个基础设施节点`}
          eyebrow="TEAMLAB SCENE"
          title={scene.definition.name}
        />
        <nav aria-label="场景管理" className={styles.tabs}>
          <NavLink to="design"><Network size={16} />设计</NavLink>
          <NavLink to="releases"><Boxes size={16} />发布版本</NavLink>
          <NavLink to="runtimes"><PlayCircle size={16} />试运行</NavLink>
        </nav>
        <main className={styles.content}>
          <Outlet context={{ scene }} />
        </main>
      </div>
    </TeamLabSceneContext.Provider>
  )
}
