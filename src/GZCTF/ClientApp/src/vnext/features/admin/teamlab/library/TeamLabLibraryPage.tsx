import { Plus, Search } from 'lucide-react'
import { useState } from 'react'
import { Navigate, useNavigate, useSearchParams } from 'react-router'
import { RuntimeApiError } from '../../api/runtimeJsonClient'
import { ActionButton, InlineFeedback } from '../../../../shared/Interaction'
import { DataState } from '../../../../shared/Primitives'
import { errorMessage } from '../../../../shared/errors'
import { useVNextPageTitle } from '../../../../shared/useVNextPageTitle'
import {
  AdminPageHeader,
  CursorPaginationBar,
  FilterToolbar,
  RefreshIndicator,
  ToolbarGroup,
} from '../../shared/AdminWorkbench'
import { TeamLabCreateDialog } from './TeamLabCreateDialog'
import { TeamLabSceneTable } from './TeamLabSceneTable'
import { useTeamLabCatalog, type TeamLabSceneOwnerFilter, type TeamLabSceneStatusFilter } from './useTeamLabCatalog'
import styles from './TeamLabLibraryPage.module.css'
import { TeamLabWorkspaceNav } from '../shared/TeamLabWorkspaceNav'

export function TeamLabLibraryPage() {
  const [params] = useSearchParams()
  const view = params.get('view')
  if (view === 'runtimes' || view === 'sessions') {
    const next = new URLSearchParams(params)
    if (view === 'runtimes') next.delete('view')
    return <Navigate replace to={`/admin/teamlab/runtimes${next.size ? `?${next}` : ''}`} />
  }
  return <TeamLabSceneLibrary />
}

function TeamLabSceneLibrary() {
  const navigate = useNavigate()
  const catalog = useTeamLabCatalog()
  const [createOpen, setCreateOpen] = useState(false)
  useVNextPageTitle('组网场景库')

  const forbidden = catalog.error instanceof RuntimeApiError && catalog.error.status === 403

  return (
    <div className={styles.page}>
      <AdminPageHeader
        actions={<ActionButton icon={<Plus size={16} />} onClick={() => setCreateOpen(true)} tone="primary" type="button">新建场景</ActionButton>}
        eyebrow="TEAMLAB"
        title="组网场景库"
      />
      <TeamLabWorkspaceNav active="scenes" />
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
