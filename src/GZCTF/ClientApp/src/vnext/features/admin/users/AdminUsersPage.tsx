import { Plus, Search } from 'lucide-react'
import { useEffect, useMemo, useState } from 'react'
import { Role, type UserInfoModel } from '@Api'
import { ActionButton, InlineFeedback, VNextConfirmDialog } from '../../../shared/Interaction'
import { DataState } from '../../../shared/Primitives'
import { errorMessage } from '../../../shared/errors'
import { useVNextPageTitle } from '../../../shared/useVNextPageTitle'
import { useCurrentAccount } from '../../account/useCurrentAccount'
import { commonAdminApi } from '../api'
import {
  type AdminDataColumn,
  AdminPageHeader,
  DataTable,
  FilterToolbar,
  MetricItem,
  MetricStrip,
  PaginationBar,
  RefreshIndicator,
  StatusBadge,
  ToolbarGroup,
} from '../shared/AdminWorkbench'
import { formatAdminDate } from '../shared/adminFormat'
import { positiveInteger, useAdminQueryState } from '../shared/useAdminQueryState'
import styles from './AdminUsersPage.module.css'
import { PasswordResultDialog, UserCreateDialog, UserDetailDrawer } from './UserEditors'
import { adminRoleLabel, adminRoleTone } from './userPresentation'
import { useAdminUsers, useStudentGroupOptions } from './useAdminUsers'

const PAGE_SIZE = 30
const allowedRoles = new Set<Role>([Role.Student, Role.Teacher, Role.Admin, Role.SuperAdmin, Role.Banned])

function userIdentity(user: UserInfoModel) {
  return (
    <div className={styles.userIdentity}>
      <span className={styles.avatar}>{user.avatar ? <img alt="" src={user.avatar} /> : user.userName?.slice(0, 1).toUpperCase()}</span>
      <span><strong>{user.userName || '未命名用户'}</strong><small>{user.email || user.id || '—'}</small></span>
    </div>
  )
}

export function AdminUsersPage() {
  const account = useCurrentAccount()
  const queryState = useAdminQueryState(PAGE_SIZE)
  const [query, setQuery] = useState(queryState.params.get('q') ?? '')
  const [selected, setSelected] = useState<UserInfoModel | null>(null)
  const [createOpen, setCreateOpen] = useState(false)
  const [deleteTarget, setDeleteTarget] = useState<UserInfoModel | null>(null)
  const [resetTarget, setResetTarget] = useState<UserInfoModel | null>(null)
  const [passwordResult, setPasswordResult] = useState<string | null>(null)
  const [feedback, setFeedback] = useState<{ tone: 'danger' | 'success'; message: string } | null>(null)
  const roleParam = queryState.params.get('role') as Role | null
  const role = roleParam && allowedRoles.has(roleParam) ? roleParam : undefined
  const groupId = positiveInteger(queryState.params.get('groupId'), 0) || undefined
  const usersRequest = useAdminUsers({
    page: queryState.page,
    pageSize: PAGE_SIZE,
    keyword: queryState.params.get('q') ?? undefined,
    role,
    groupId,
  })
  const groupRequest = useStudentGroupOptions()

  useVNextPageTitle('用户管理')

  useEffect(() => setQuery(queryState.params.get('q') ?? ''), [queryState.params])
  useEffect(() => {
    const current = queryState.params.get('q') ?? ''
    if (query.trim() === current) return undefined
    const timer = window.setTimeout(() => queryState.update({ q: query.trim() || null }, { replace: true }), 250)
    return () => window.clearTimeout(timer)
  }, [query, queryState])

  const source = usersRequest.page?.items ?? []
  const metrics = useMemo(() => ({
    active: source.filter((user) => user.emailConfirmed && user.role !== Role.Banned).length,
    disabled: source.filter((user) => !user.emailConfirmed || user.role === Role.Banned).length,
    privileged: source.filter((user) => user.role === Role.Admin || user.role === Role.SuperAdmin).length,
  }), [source])

  const columns: AdminDataColumn<UserInfoModel>[] = [
    { id: 'user', header: '用户', width: 'wide', render: userIdentity },
    { id: 'role', header: '角色', width: 'compact', render: (user) => <StatusBadge tone={adminRoleTone(user.role)}>{adminRoleLabel(user.role)}</StatusBadge> },
    { id: 'status', header: '账号', width: 'compact', render: (user) => <StatusBadge tone={user.emailConfirmed && user.role !== Role.Banned ? 'success' : 'danger'}>{user.emailConfirmed && user.role !== Role.Banned ? '可登录' : '已停用'}</StatusBadge> },
    { id: 'identity', header: '实名信息', width: 'medium', visibility: 'desktop', render: (user) => <div className={styles.textStack}><strong>{user.realName || '未填写'}</strong><small>{user.stdNumber || '无学号'}</small></div> },
    { id: 'groups', header: '学员组', width: 'wide', visibility: 'wide', render: (user) => <div className={styles.tagList}>{user.studentGroups?.length ? user.studentGroups.map((group) => <span key={group.id}>{group.name}</span>) : <small>未分组</small>}</div> },
    { id: 'visited', header: '最后访问', width: 'medium', visibility: 'desktop', render: (user) => <time className={styles.mono}>{formatAdminDate(user.lastVisitedUtc, false)}</time> },
  ]

  const pageCount = Math.max(1, Math.ceil((usersRequest.page?.total ?? 0) / PAGE_SIZE))

  return (
    <div className={styles.page}>
      <AdminPageHeader
        actions={<ActionButton icon={<Plus size={16} />} onClick={() => setCreateOpen(true)} tone="primary" type="button">新建用户</ActionButton>}
        description="维护平台身份、账号状态、权限角色和培训分组。"
        eyebrow="IDENTITY GOVERNANCE"
        title="用户管理"
      />
      <MetricStrip>
        <MetricItem detail="服务器记录" label="用户总数" value={usersRequest.page?.total ?? 0} />
        <MetricItem detail="当前加载页" label="可登录" tone={metrics.active ? 'success' : 'neutral'} value={metrics.active} />
        <MetricItem detail="当前加载页" label="停用" tone={metrics.disabled ? 'danger' : 'neutral'} value={metrics.disabled} />
        <MetricItem detail="管理员与超级管理员" label="高权限" tone={metrics.privileged ? 'warning' : 'neutral'} value={metrics.privileged} />
      </MetricStrip>
      <FilterToolbar>
        <ToolbarGroup grow>
          <label className={styles.searchBox}>
            <Search aria-hidden="true" size={16} />
            <input aria-label="搜索用户" onChange={(event) => setQuery(event.currentTarget.value)} placeholder="用户名、实名、邮箱、学号或用户 ID" type="search" value={query} />
          </label>
          <select aria-label="筛选角色" onChange={(event) => queryState.update({ role: event.currentTarget.value || null })} value={role ?? ''}>
            <option value="">全部角色</option>
            {[Role.Student, Role.Teacher, Role.Admin, Role.SuperAdmin, Role.Banned].map((value) => <option key={value} value={value}>{adminRoleLabel(value)}</option>)}
          </select>
          <select aria-label="筛选学员组" onChange={(event) => queryState.update({ groupId: event.currentTarget.value || null })} value={groupId ?? ''}>
            <option value="">全部学员组</option>
            {groupRequest.groups.map((group) => group.id ? <option key={group.id} value={group.id}>{group.name}</option> : null)}
          </select>
        </ToolbarGroup>
        <RefreshIndicator active={usersRequest.isRefreshing} label="列表按需刷新" />
      </FilterToolbar>
      {feedback ? <InlineFeedback tone={feedback.tone}>{feedback.message}</InlineFeedback> : null}
      {usersRequest.error ? <InlineFeedback tone="danger">{errorMessage(usersRequest.error, '用户列表加载失败。')}</InlineFeedback> : null}
      {groupRequest.error ? <InlineFeedback tone="danger">{errorMessage(groupRequest.error, '学员组选项加载失败。')}</InlineFeedback> : null}
      {usersRequest.isLoading ? (
        <DataState description="正在读取用户和权限信息。" loading title="用户列表加载中" />
      ) : (
        <>
          <DataTable caption="平台用户管理列表" columns={columns} onRowClick={setSelected} rowKey={(user) => user.id ?? user.userName ?? 'unknown'} rows={source} />
          <PaginationBar onPageChange={queryState.setPage} page={queryState.page} pageCount={pageCount} total={usersRequest.page?.total} />
        </>
      )}
      <UserCreateDialog
        actorRole={account.user?.role}
        groups={groupRequest.groups}
        onClose={() => setCreateOpen(false)}
        onCreate={async (payload) => {
          await commonAdminApi.createUser(payload)
          await usersRequest.mutate()
          setFeedback({ tone: 'success', message: `用户“${payload.userName}”已创建。` })
        }}
        open={createOpen}
      />
      <UserDetailDrawer
        actorRole={account.user?.role}
        currentUserId={account.user?.userId}
        groups={groupRequest.groups}
        onClose={() => setSelected(null)}
        onRequestDelete={(user) => setDeleteTarget(user)}
        onRequestReset={(user) => setResetTarget(user)}
        onSave={async (userId, payload) => {
          await commonAdminApi.updateUser(userId, payload)
          await usersRequest.mutate()
          setFeedback({ tone: 'success', message: '用户信息已保存并从服务器回读。' })
        }}
        user={selected}
      />
      <VNextConfirmDialog
        confirmationText={deleteTarget?.userName || undefined}
        description="删除后该账号不能恢复；队长、最后一名超级管理员和当前账号会被后端拒绝删除。"
        message={<>确认永久删除用户 <strong>{deleteTarget?.userName}</strong>？</>}
        onClose={() => setDeleteTarget(null)}
        onConfirm={async () => {
          if (!deleteTarget?.id) return false
          try {
            await commonAdminApi.deleteUser(deleteTarget.id)
            setSelected(null)
            await usersRequest.mutate()
            setFeedback({ tone: 'success', message: `用户“${deleteTarget.userName}”已删除。` })
            return true
          } catch (requestError) {
            setFeedback({ tone: 'danger', message: errorMessage(requestError, '用户删除失败。') })
            return false
          }
        }}
        open={Boolean(deleteTarget)}
        title="删除用户"
      />
      <VNextConfirmDialog
        confirmLabel="重置密码"
        description="旧密码会立即失效，并生成只展示一次的随机密码。"
        message={<>确认为 <strong>{resetTarget?.userName}</strong> 重置密码？</>}
        onClose={() => setResetTarget(null)}
        onConfirm={async () => {
          if (!resetTarget?.id) return false
          try {
            setPasswordResult(await commonAdminApi.resetUserPassword(resetTarget.id))
            return true
          } catch (requestError) {
            setFeedback({ tone: 'danger', message: errorMessage(requestError, '密码重置失败。') })
            return false
          }
        }}
        open={Boolean(resetTarget)}
        title="重置用户密码"
      />
      <PasswordResultDialog onClose={() => setPasswordResult(null)} password={passwordResult} />
    </div>
  )
}
