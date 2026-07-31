import { Plus, Search } from 'lucide-react'
import { useMemo, useState } from 'react'
import { useNavigate } from 'react-router'
import { RuntimeApiError } from '../../api/runtimeJsonClient'
import { ActionButton, InlineFeedback } from '../../../../shared/Interaction'
import { DataState } from '../../../../shared/Primitives'
import { errorMessage } from '../../../../shared/errors'
import { useVNextPageTitle } from '../../../../shared/useVNextPageTitle'
import {
  AdminPageHeader,
  CursorPaginationBar,
  FilterToolbar,
  MetricItem,
  MetricStrip,
  RefreshIndicator,
  ToolbarGroup,
} from '../../shared/AdminWorkbench'
import { TeamLabCreateDialog } from './TeamLabCreateDialog'
import { TeamLabSceneTable } from './TeamLabSceneTable'
import { useTeamLabCatalog, type TeamLabSceneOwnerFilter, type TeamLabSceneStatusFilter } from './useTeamLabCatalog'
import styles from './TeamLabLibraryPage.module.css'

export function TeamLabLibraryPage() {
  const navigate = useNavigate()
  const catalog = useTeamLabCatalog()
  const [createOpen, setCreateOpen] = useState(false)

  useVNextPageTitle('TeamLab 场景库')

  const metrics = useMemo(() => {
    const scenes = catalog.page?.items ?? []
    return {
      scenes: scenes.length,
      published: scenes.filter((scene) => scene.latestRelease?.sourceRevision === scene.revision).length,
      running: scenes.filter((scene) => scene.latestTrialRuntime?.status === 'running').length,
      assets: scenes.reduce((total, scene) => total + scene.assetCount, 0),
    }
  }, [catalog.page?.items])
  const forbidden = catalog.error instanceof RuntimeApiError && catalog.error.status === 403

  return (
    <div className={styles.page}>
      <AdminPageHeader
        actions={<ActionButton icon={<Plus size={16} />} onClick={() => setCreateOpen(true)} tone="primary" type="button">创建场景</ActionButton>}
        description="维护可复用的网络场景、不可变发布版本和试运行记录。"
        eyebrow="TEAMLAB ORCHESTRATION"
        title="组网场景库"
      />
      <MetricStrip>
        <MetricItem detail="当前页" label="场景" value={metrics.scenes} />
        <MetricItem detail="当前修订已发布" label="可用版本" tone={metrics.published ? 'success' : 'neutral'} value={metrics.published} />
        <MetricItem detail="当前页试运行" label="运行中" tone={metrics.running ? 'info' : 'neutral'} value={metrics.running} />
        <MetricItem detail="当前页合计" label="资产" value={metrics.assets} />
      </MetricStrip>
      <FilterToolbar>
        <ToolbarGroup grow>
          <label className={styles.searchBox}>
            <Search aria-hidden="true" size={16} />
            <input
              aria-label="搜索组网场景"
              onChange={(event) => catalog.setSearchInput(event.currentTarget.value)}
              placeholder="名称"
              type="search"
              value={catalog.searchInput}
            />
          </label>
          <select
            aria-label="筛选所有者"
            onChange={(event) => catalog.setOwner(event.currentTarget.value as TeamLabSceneOwnerFilter)}
            value={catalog.owner}
          >
            <option value="">全部所有者</option>
            <option value="mine">我的场景</option>
          </select>
          <select
            aria-label="筛选场景状态"
            onChange={(event) => catalog.setStatus(event.currentTarget.value as TeamLabSceneStatusFilter)}
            value={catalog.status}
          >
            <option value="">全部状态</option>
            <option value="draft">草稿</option>
            <option value="published">已发布</option>
            <option value="running">运行中</option>
            <option value="failed">运行失败</option>
          </select>
        </ToolbarGroup>
        <RefreshIndicator active={catalog.isRefreshing} label={catalog.isRefreshing ? '正在同步' : '数据已同步'} />
      </FilterToolbar>

      {catalog.isLoading ? (
        <DataState description="正在读取场景、版本和试运行摘要。" loading title="场景库加载中" />
      ) : forbidden ? (
        <DataState description="当前账号没有 TeamLab 场景管理权限。" title="无法访问场景库" />
      ) : catalog.error ? (
        <>
          <InlineFeedback tone="danger">{errorMessage(catalog.error, '场景库加载失败。')}</InlineFeedback>
          <DataState description="服务恢复后可重新进入或刷新当前页面。" title="场景数据暂不可用" />
        </>
      ) : (
        <>
          <TeamLabSceneTable scenes={catalog.page?.items ?? []} />
          <CursorPaginationBar
            hasNext={Boolean(catalog.page?.nextCursor)}
            label="场景分页"
            onNext={() => catalog.page?.nextCursor && catalog.cursor.next(catalog.page.nextCursor)}
            onPrevious={catalog.cursor.previous}
            page={catalog.cursor.page}
          />
        </>
      )}

      <TeamLabCreateDialog
        onClose={() => setCreateOpen(false)}
        onCreated={(topologyId) => {
          setCreateOpen(false)
          void catalog.mutate()
          navigate(`/admin/teamlab/${topologyId}/design`)
        }}
        open={createOpen}
      />
    </div>
  )
}
