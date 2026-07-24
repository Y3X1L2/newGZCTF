import { Plus, Search } from 'lucide-react'
import { useEffect, useMemo, useState } from 'react'
import type { StudentGroupBriefModel } from '@Api'
import { ActionButton, InlineFeedback, VNextConfirmDialog } from '../../../shared/Interaction'
import { DataState } from '../../../shared/Primitives'
import { errorMessage } from '../../../shared/errors'
import { useVNextPageTitle } from '../../../shared/useVNextPageTitle'
import { commonAdminApi } from '../api'
import {
  type AdminDataColumn,
  AdminPageHeader,
  DataTable,
  FilterToolbar,
  MetricItem,
  MetricStrip,
  RefreshIndicator,
  StatusBadge,
  ToolbarGroup,
} from '../shared/AdminWorkbench'
import { formatAdminDate } from '../shared/adminFormat'
import { useAdminQueryState } from '../shared/useAdminQueryState'
import styles from './AdminStudentGroupsPage.module.css'
import { StudentGroupCreateDialog, StudentGroupDetailDrawer } from './StudentGroupEditors'
import { useAdminStudentGroups } from './useAdminStudentGroups'

export function AdminStudentGroupsPage() {
  const queryState = useAdminQueryState()
  const [query, setQuery] = useState(queryState.params.get('q') ?? '')
  const [selected, setSelected] = useState<StudentGroupBriefModel | null>(null)
  const [createOpen, setCreateOpen] = useState(false)
  const [archiveTarget, setArchiveTarget] = useState<StudentGroupBriefModel | null>(null)
  const [feedback, setFeedback] = useState<{ tone: 'danger' | 'success'; message: string } | null>(null)
  const includeArchived = queryState.params.get('archived') === '1'
  const keyword = queryState.params.get('q') ?? ''
  const groupsRequest = useAdminStudentGroups(keyword, includeArchived)

  useVNextPageTitle('学员组管理')

  useEffect(() => setQuery(queryState.params.get('q') ?? ''), [queryState.params])
  useEffect(() => {
    const current = queryState.params.get('q') ?? ''
    if (query.trim() === current) return undefined
    const timer = window.setTimeout(() => queryState.update({ q: query.trim() || null }, { replace: true }), 250)
    return () => window.clearTimeout(timer)
  }, [query, queryState])

  const source = groupsRequest.groups ?? []
  const metrics = useMemo(() => ({
    members: source.reduce((total, group) => total + (group.memberCount ?? 0), 0),
    managers: source.reduce((total, group) => total + (group.managerCount ?? 0), 0),
    archived: source.filter((group) => group.isArchived).length,
  }), [source])

  const columns: AdminDataColumn<StudentGroupBriefModel>[] = [
    { id: 'group', header: '学员组', width: 'wide', render: (group) => <div className={styles.groupIdentity}><strong>{group.name || '未命名学员组'}</strong><small>#{group.id ?? '—'} · {group.description || '未填写用途说明'}</small></div> },
    { id: 'status', header: '状态', width: 'compact', render: (group) => <StatusBadge tone={group.isArchived ? 'neutral' : 'success'}>{group.isArchived ? '已归档' : '使用中'}</StatusBadge> },
    { id: 'members', header: '学员', width: 'compact', render: (group) => `${group.memberCount ?? 0} 人` },
    { id: 'managers', header: '负责教师', width: 'compact', render: (group) => `${group.managerCount ?? 0} 人` },
    { id: 'updated', header: '更新时间', width: 'medium', visibility: 'desktop', render: (group) => <time className={styles.mono}>{formatAdminDate(group.updatedAt, false)}</time> },
  ]

  return (
    <div className={styles.page}>
      <AdminPageHeader
        actions={<ActionButton icon={<Plus size={16} />} onClick={() => setCreateOpen(true)} tone="primary" type="button">新建学员组</ActionButton>}
        description="组织培训学员并分配负责教师；归档记录保留历史关系。"
        eyebrow="TRAINING GOVERNANCE"
        title="学员组管理"
      />
      <MetricStrip>
        <MetricItem detail={includeArchived ? '包含归档' : '当前可用'} label="学员组" value={source.length} />
        <MetricItem detail="当前筛选结果" label="学员关系" value={metrics.members} />
        <MetricItem detail="当前筛选结果" label="教师关系" value={metrics.managers} />
        <MetricItem detail="当前筛选结果" label="归档" tone={metrics.archived ? 'warning' : 'neutral'} value={metrics.archived} />
      </MetricStrip>
      <FilterToolbar>
        <ToolbarGroup grow>
          <label className={styles.searchBox}>
            <Search aria-hidden="true" size={16} />
            <input aria-label="搜索学员组" onChange={(event) => setQuery(event.currentTarget.value)} placeholder="名称或用途说明" type="search" value={query} />
          </label>
          <label className={styles.archiveToggle}>
            <input checked={includeArchived} onChange={(event) => queryState.update({ archived: event.currentTarget.checked ? '1' : null })} type="checkbox" />
            <span>包含归档记录</span>
          </label>
        </ToolbarGroup>
        <RefreshIndicator active={groupsRequest.isRefreshing} label="列表按需刷新" />
      </FilterToolbar>
      {feedback ? <InlineFeedback tone={feedback.tone}>{feedback.message}</InlineFeedback> : null}
      {groupsRequest.error ? <InlineFeedback tone="danger">{errorMessage(groupsRequest.error, '学员组列表加载失败。')}</InlineFeedback> : null}
      {groupsRequest.isLoading ? (
        <DataState description="正在读取学员组关系。" loading title="学员组列表加载中" />
      ) : (
        <DataTable caption="培训学员组管理列表" columns={columns} emptyDescription="当前筛选条件下没有学员组。" emptyTitle="没有匹配学员组" onRowClick={setSelected} rowKey={(group) => group.id ?? group.name ?? 'unknown'} rows={source} />
      )}
      <StudentGroupCreateDialog
        onClose={() => setCreateOpen(false)}
        onCreated={async (group) => {
          await groupsRequest.mutate()
          setFeedback({ tone: 'success', message: `学员组“${group.name}”已创建。` })
        }}
        open={createOpen}
      />
      <StudentGroupDetailDrawer
        group={selected}
        onClose={() => setSelected(null)}
        onRequestArchive={setArchiveTarget}
        onUpdated={async () => { await groupsRequest.mutate() }}
      />
      <VNextConfirmDialog
        confirmLabel="归档学员组"
        description="归档会隐藏该组的常规使用入口，但保留历史成员与课程关系。当前版本没有取消归档接口。"
        message={<>确认归档学员组 <strong>{archiveTarget?.name}</strong>？</>}
        onClose={() => setArchiveTarget(null)}
        onConfirm={async () => {
          if (!archiveTarget?.id) return false
          try {
            await commonAdminApi.archiveStudentGroup(archiveTarget.id)
            setSelected(null)
            await groupsRequest.mutate()
            setFeedback({ tone: 'success', message: `学员组“${archiveTarget.name}”已归档。` })
            return true
          } catch (requestError) {
            setFeedback({ tone: 'danger', message: errorMessage(requestError, '学员组归档失败。') })
            return false
          }
        }}
        open={Boolean(archiveTarget)}
        title="归档学员组"
      />
    </div>
  )
}
