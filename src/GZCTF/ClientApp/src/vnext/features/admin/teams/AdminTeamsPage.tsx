import { Save, Search, Trash2 } from 'lucide-react'
import { useEffect, useMemo, useState } from 'react'
import { type AdminTeamModel, type TeamInfoModel } from '@Api'
import { TextAreaField, TextField, ToggleField } from '../../../shared/FormControls'
import { ActionButton, InlineFeedback, VNextConfirmDialog } from '../../../shared/Interaction'
import { DataState } from '../../../shared/Primitives'
import { errorMessage } from '../../../shared/errors'
import { useVNextPageTitle } from '../../../shared/useVNextPageTitle'
import { commonAdminApi } from '../api'
import {
  type AdminDataColumn,
  AdminPageHeader,
  DataTable,
  DetailDrawer,
  FilterToolbar,
  MetricItem,
  MetricStrip,
  PaginationBar,
  RefreshIndicator,
  StatusBadge,
  ToolbarGroup,
} from '../shared/AdminWorkbench'
import { useAdminQueryState } from '../shared/useAdminQueryState'
import styles from './AdminTeamsPage.module.css'
import { useAdminTeams } from './useAdminTeams'

const PAGE_SIZE = 30

function TeamIdentity({ team }: { team: TeamInfoModel }) {
  return (
    <div className={styles.teamIdentity}>
      <span>{team.avatar ? <img alt="" src={team.avatar} /> : team.name?.slice(0, 1).toUpperCase()}</span>
      <div><strong>{team.name || '未命名战队'}</strong><small>#{team.id ?? '—'}</small></div>
    </div>
  )
}

function TeamDetailDrawer({
  team,
  onClose,
  onSave,
  onRequestDelete,
}: {
  team: TeamInfoModel | null
  onClose: () => void
  onSave: (teamId: number, payload: AdminTeamModel) => Promise<void>
  onRequestDelete: (team: TeamInfoModel) => void
}) {
  const [draft, setDraft] = useState<AdminTeamModel>({})
  const [saving, setSaving] = useState(false)
  const [failure, setFailure] = useState<string | null>(null)

  useEffect(() => {
    if (!team) return
    setDraft({ name: team.name, bio: team.bio, locked: team.locked })
    setFailure(null)
  }, [team])

  const save = async () => {
    if (!team?.id || !draft.name?.trim() || saving) return
    setSaving(true)
    setFailure(null)
    try {
      await onSave(team.id, { name: draft.name.trim(), bio: draft.bio?.trim(), locked: draft.locked })
      onClose()
    } catch (requestError) {
      setFailure(errorMessage(requestError, '战队信息保存失败。'))
    } finally {
      setSaving(false)
    }
  }

  const members = useMemo(
    () => [...(team?.members ?? [])].sort((left, right) => Number(Boolean(right.captain)) - Number(Boolean(left.captain))),
    [team?.members]
  )

  return (
    <DetailDrawer
      description={team?.id ? `战队 ID ${team.id}` : undefined}
      footer={team ? (
        <div className={styles.drawerActions}>
          <ActionButton disabled={saving} icon={<Trash2 size={16} />} onClick={() => onRequestDelete(team)} tone="danger" type="button">删除战队</ActionButton>
          <ActionButton disabled={saving || !draft.name?.trim()} icon={<Save size={16} />} onClick={() => void save()} tone="primary" type="button">{saving ? '保存中' : '保存修改'}</ActionButton>
        </div>
      ) : null}
      onClose={onClose}
      open={Boolean(team)}
      title={team?.name || '战队详情'}
    >
      {team ? (
        <div className={styles.drawerContent}>
          <TeamIdentity team={team} />
          <div className={styles.formStack}>
            <TextField label="战队名称" maxLength={20} onValueChange={(name) => setDraft({ ...draft, name })} required value={draft.name ?? ''} />
            <TextAreaField label="战队简介" maxLength={72} onValueChange={(bio) => setDraft({ ...draft, bio })} rows={3} value={draft.bio ?? ''} />
            <ToggleField checked={draft.locked ?? false} description="锁定后战队不能继续进行常规成员操作。" label="锁定战队" onChange={(locked) => setDraft({ ...draft, locked })} />
          </div>
          <section className={styles.memberSection}>
            <header><h3>战队成员</h3><span>{members.length} 人</span></header>
            {members.length ? (
              <ul>
                {members.map((member) => (
                  <li key={member.id}>
                    <span>{member.avatar ? <img alt="" src={member.avatar} /> : member.userName?.slice(0, 1).toUpperCase()}</span>
                    <div><strong>{member.userName || '未命名成员'}</strong><small>{member.captain ? '队长' : '成员'}</small></div>
                  </li>
                ))}
              </ul>
            ) : <DataState description="该战队当前没有成员记录。" title="暂无成员" />}
          </section>
          {failure ? <InlineFeedback tone="danger">{failure}</InlineFeedback> : null}
        </div>
      ) : null}
    </DetailDrawer>
  )
}

export function AdminTeamsPage() {
  const queryState = useAdminQueryState(PAGE_SIZE)
  const [query, setQuery] = useState(queryState.params.get('q') ?? '')
  const [selected, setSelected] = useState<TeamInfoModel | null>(null)
  const [deleteTarget, setDeleteTarget] = useState<TeamInfoModel | null>(null)
  const [feedback, setFeedback] = useState<{ tone: 'danger' | 'success'; message: string } | null>(null)
  const keyword = queryState.params.get('q') ?? undefined
  const teamsRequest = useAdminTeams({ page: queryState.page, pageSize: PAGE_SIZE, keyword })

  useVNextPageTitle('战队管理')

  useEffect(() => setQuery(queryState.params.get('q') ?? ''), [queryState.params])
  useEffect(() => {
    const current = queryState.params.get('q') ?? ''
    if (query.trim() === current) return undefined
    const timer = window.setTimeout(() => queryState.update({ q: query.trim() || null }, { replace: true }), 250)
    return () => window.clearTimeout(timer)
  }, [query, queryState])

  const source = teamsRequest.page?.items ?? []
  const metrics = useMemo(() => ({
    open: source.filter((team) => !team.locked).length,
    locked: source.filter((team) => team.locked).length,
    members: source.reduce((total, team) => total + (team.members?.length ?? 0), 0),
  }), [source])

  const columns: AdminDataColumn<TeamInfoModel>[] = [
    { id: 'team', header: '战队', width: 'wide', render: (team) => <TeamIdentity team={team} /> },
    { id: 'status', header: '状态', width: 'compact', render: (team) => <StatusBadge tone={team.locked ? 'danger' : 'success'}>{team.locked ? '已锁定' : '开放'}</StatusBadge> },
    { id: 'members', header: '成员', width: 'compact', render: (team) => `${team.members?.length ?? 0} 人` },
    { id: 'captain', header: '队长', width: 'medium', visibility: 'desktop', render: (team) => team.members?.find((member) => member.captain)?.userName || '未设置' },
    { id: 'bio', header: '简介', width: 'wide', visibility: 'wide', render: (team) => <span className={styles.bioCell}>{team.bio || '未填写战队简介'}</span> },
  ]

  const pageCount = teamsRequest.page?.searchResult ? 1 : Math.max(1, Math.ceil((teamsRequest.page?.total ?? 0) / PAGE_SIZE))

  return (
    <div className={styles.page}>
      <AdminPageHeader description="维护战队资料、成员上下文和锁定状态。" eyebrow="TEAM GOVERNANCE" title="战队管理" />
      <MetricStrip>
        <MetricItem detail={keyword ? '搜索结果' : '服务器记录'} label="战队总数" value={teamsRequest.page?.total ?? 0} />
        <MetricItem detail="当前加载页" label="开放" tone={metrics.open ? 'success' : 'neutral'} value={metrics.open} />
        <MetricItem detail="当前加载页" label="锁定" tone={metrics.locked ? 'danger' : 'neutral'} value={metrics.locked} />
        <MetricItem detail="当前加载页" label="成员" value={metrics.members} />
      </MetricStrip>
      <FilterToolbar>
        <ToolbarGroup grow>
          <label className={styles.searchBox}>
            <Search aria-hidden="true" size={16} />
            <input aria-label="搜索战队" onChange={(event) => setQuery(event.currentTarget.value)} placeholder="输入战队名称" type="search" value={query} />
          </label>
        </ToolbarGroup>
        <RefreshIndicator active={teamsRequest.isRefreshing} label={keyword ? '搜索结果模式' : '列表按需刷新'} />
      </FilterToolbar>
      {keyword ? <InlineFeedback>搜索由服务器返回最多一组匹配结果，清空关键字后恢复常规分页。</InlineFeedback> : null}
      {feedback ? <InlineFeedback tone={feedback.tone}>{feedback.message}</InlineFeedback> : null}
      {teamsRequest.error ? <InlineFeedback tone="danger">{errorMessage(teamsRequest.error, '战队列表加载失败。')}</InlineFeedback> : null}
      {teamsRequest.isLoading ? (
        <DataState description="正在读取战队与成员信息。" loading title="战队列表加载中" />
      ) : (
        <>
          <DataTable caption="平台战队管理列表" columns={columns} onRowClick={setSelected} rowKey={(team) => team.id ?? team.name ?? 'unknown'} rows={source} />
          <PaginationBar onPageChange={queryState.setPage} page={teamsRequest.page?.searchResult ? 1 : queryState.page} pageCount={pageCount} total={teamsRequest.page?.total} />
        </>
      )}
      <TeamDetailDrawer
        onClose={() => setSelected(null)}
        onRequestDelete={setDeleteTarget}
        onSave={async (teamId, payload) => {
          await commonAdminApi.updateTeam(teamId, payload)
          await teamsRequest.mutate()
          setFeedback({ tone: 'success', message: '战队信息已保存并从服务器回读。' })
        }}
        team={selected}
      />
      <VNextConfirmDialog
        confirmationText={deleteTarget?.name || undefined}
        description={`该战队当前包含 ${deleteTarget?.members?.length ?? 0} 名成员，删除操作不能撤销。`}
        message={<>确认永久删除战队 <strong>{deleteTarget?.name}</strong>？</>}
        onClose={() => setDeleteTarget(null)}
        onConfirm={async () => {
          if (!deleteTarget?.id) return false
          try {
            await commonAdminApi.deleteTeam(deleteTarget.id)
            setSelected(null)
            await teamsRequest.mutate()
            setFeedback({ tone: 'success', message: `战队“${deleteTarget.name}”已删除。` })
            return true
          } catch (requestError) {
            setFeedback({ tone: 'danger', message: errorMessage(requestError, '战队删除失败。') })
            return false
          }
        }}
        open={Boolean(deleteTarget)}
        title="删除战队"
      />
    </div>
  )
}
