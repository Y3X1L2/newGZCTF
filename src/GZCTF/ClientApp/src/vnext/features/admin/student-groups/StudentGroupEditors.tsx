import { Archive, Save, Search, UserMinus, UserPlus } from 'lucide-react'
import { useEffect, useMemo, useState } from 'react'
import {
  Role,
  StudentGroupManagerRole,
  type StudentGroupBriefModel,
  type StudentGroupDetailModel,
  type UserInfoModel,
} from '@Api'
import { TextAreaField, TextField } from '../../../shared/FormControls'
import { ActionButton, InlineFeedback, VNextDialog } from '../../../shared/Interaction'
import { DataState } from '../../../shared/Primitives'
import { errorMessage } from '../../../shared/errors'
import { commonAdminApi } from '../api'
import { DetailDrawer, StatusBadge } from '../shared/AdminWorkbench'
import { useAdminStudentGroup } from './useAdminStudentGroups'
import styles from './AdminStudentGroupsPage.module.css'

export function StudentGroupCreateDialog({
  open,
  onClose,
  onCreated,
}: {
  open: boolean
  onClose: () => void
  onCreated: (group: StudentGroupDetailModel) => Promise<void>
}) {
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [saving, setSaving] = useState(false)
  const [failure, setFailure] = useState<string | null>(null)

  useEffect(() => {
    if (!open) {
      setName('')
      setDescription('')
      setFailure(null)
    }
  }, [open])

  const create = async () => {
    if (!name.trim() || saving) return
    setSaving(true)
    setFailure(null)
    try {
      await onCreated(await commonAdminApi.createStudentGroup({ name: name.trim(), description: description.trim() }))
      onClose()
    } catch (requestError) {
      setFailure(errorMessage(requestError, '学员组创建失败。'))
    } finally {
      setSaving(false)
    }
  }

  return (
    <VNextDialog
      description="创建后，当前管理员会成为该学员组的负责人。"
      eyebrow="STUDENT GROUP"
      footer={
        <>
          <ActionButton disabled={saving} onClick={onClose} type="button">取消</ActionButton>
          <ActionButton disabled={!name.trim() || saving} icon={<UserPlus size={16} />} onClick={() => void create()} tone="primary" type="button">{saving ? '正在创建' : '创建学员组'}</ActionButton>
        </>
      }
      onClose={() => { if (!saving) onClose() }}
      open={open}
      title="新建学员组"
    >
      <div className={styles.formStack}>
        <TextField label="学员组名称" maxLength={128} onValueChange={setName} required value={name} />
        <TextAreaField label="用途说明" maxLength={512} onValueChange={setDescription} rows={4} value={description} />
        {failure ? <InlineFeedback tone="danger">{failure}</InlineFeedback> : null}
      </div>
    </VNextDialog>
  )
}

function PersonSearch({
  role,
  excludedIds,
  actionLabel,
  onSelect,
}: {
  role: Role
  excludedIds: Set<string>
  actionLabel: string
  onSelect: (user: UserInfoModel) => Promise<void>
}) {
  const [query, setQuery] = useState('')
  const [results, setResults] = useState<UserInfoModel[]>([])
  const [searching, setSearching] = useState(false)
  const [savingId, setSavingId] = useState<string | null>(null)
  const [failure, setFailure] = useState<string | null>(null)

  const search = async () => {
    if (query.trim().length < 2 || searching) return
    setSearching(true)
    setFailure(null)
    try {
      const response = await commonAdminApi.users({ keyword: query.trim(), role, pageSize: 20 })
      setResults(response.items.filter((user) => Boolean(user.id && !excludedIds.has(user.id))))
    } catch (requestError) {
      setFailure(errorMessage(requestError, '候选用户搜索失败。'))
    } finally {
      setSearching(false)
    }
  }

  return (
    <div className={styles.personSearch}>
      <div className={styles.inlineSearch}>
        <label>
          <Search aria-hidden="true" size={16} />
          <input
            aria-label={role === Role.Teacher ? '搜索教师' : '搜索学员'}
            onChange={(event) => setQuery(event.currentTarget.value)}
            onKeyDown={(event) => { if (event.key === 'Enter') void search() }}
            placeholder={role === Role.Teacher ? '用户名、实名或用户 ID' : '用户名、实名、学号或用户 ID'}
            type="search"
            value={query}
          />
        </label>
        <ActionButton disabled={query.trim().length < 2 || searching} onClick={() => void search()} type="button">{searching ? '搜索中' : '搜索'}</ActionButton>
      </div>
      {failure ? <InlineFeedback tone="danger">{failure}</InlineFeedback> : null}
      {results.length ? (
        <ul className={styles.candidateList}>
          {results.map((user) => (
            <li key={user.id}>
              <div><strong>{user.userName}</strong><small>{user.realName || user.stdNumber || user.email || user.id}</small></div>
              <ActionButton
                disabled={savingId === user.id}
                onClick={async () => {
                  if (!user.id) return
                  setSavingId(user.id)
                  setFailure(null)
                  try {
                    await onSelect(user)
                    setResults((current) => current.filter((item) => item.id !== user.id))
                  } catch (requestError) {
                    setFailure(errorMessage(requestError, `${actionLabel}失败。`))
                  } finally {
                    setSavingId(null)
                  }
                }}
                tone="primary"
                type="button"
              >
                {savingId === user.id ? '处理中' : actionLabel}
              </ActionButton>
            </li>
          ))}
        </ul>
      ) : query.trim().length >= 2 && !searching ? <small className={styles.searchHint}>没有尚未加入的匹配用户。</small> : null}
    </div>
  )
}

export function StudentGroupDetailDrawer({
  group,
  onClose,
  onUpdated,
  onRequestArchive,
}: {
  group: StudentGroupBriefModel | null
  onClose: () => void
  onUpdated: () => Promise<void>
  onRequestArchive: (group: StudentGroupBriefModel) => void
}) {
  const detailRequest = useAdminStudentGroup(group?.id ?? null)
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [saving, setSaving] = useState(false)
  const [failure, setFailure] = useState<string | null>(null)
  const detail = detailRequest.group
  const archived = Boolean(detail?.isArchived || group?.isArchived)

  useEffect(() => {
    if (!detail) return
    setName(detail.name ?? '')
    setDescription(detail.description ?? '')
    setFailure(null)
  }, [detail])

  const refresh = async () => {
    await detailRequest.mutate()
    await onUpdated()
  }

  const memberIds = useMemo(() => new Set(detail?.members?.flatMap((member) => member.studentId ? [member.studentId] : []) ?? []), [detail?.members])
  const managerIds = useMemo(() => new Set(detail?.managers?.flatMap((manager) => manager.teacherId ? [manager.teacherId] : []) ?? []), [detail?.managers])

  const save = async () => {
    if (!group?.id || !name.trim() || saving || archived) return
    setSaving(true)
    setFailure(null)
    try {
      await commonAdminApi.updateStudentGroup(group.id, { name: name.trim(), description: description.trim() })
      await refresh()
    } catch (requestError) {
      setFailure(errorMessage(requestError, '学员组信息保存失败。'))
    } finally {
      setSaving(false)
    }
  }

  return (
    <DetailDrawer
      description={group?.id ? `学员组 ID ${group.id}` : undefined}
      footer={group && !archived ? (
        <div className={styles.drawerActions}>
          <ActionButton disabled={saving} icon={<Archive size={16} />} onClick={() => onRequestArchive(group)} tone="danger" type="button">归档学员组</ActionButton>
          <ActionButton disabled={saving || !name.trim()} icon={<Save size={16} />} onClick={() => void save()} tone="primary" type="button">{saving ? '保存中' : '保存基本信息'}</ActionButton>
        </div>
      ) : null}
      onClose={onClose}
      open={Boolean(group)}
      title={group?.name || '学员组详情'}
    >
      {detailRequest.isLoading ? <DataState description="正在读取成员与负责教师。" loading title="学员组详情加载中" /> : null}
      {detailRequest.error ? <InlineFeedback tone="danger">{errorMessage(detailRequest.error, '学员组详情加载失败。')}</InlineFeedback> : null}
      {detail ? (
        <div className={styles.drawerContent}>
          {archived ? <InlineFeedback>该学员组已归档。当前后端没有恢复接口，因此详情仅供查看。</InlineFeedback> : null}
          <section className={styles.detailSection}>
            <header><div><h3>基本信息</h3><p>名称和用途说明用于课程报名与教师管理。</p></div><StatusBadge tone={archived ? 'neutral' : 'success'}>{archived ? '已归档' : '使用中'}</StatusBadge></header>
            <div className={styles.formStack}>
              <TextField disabled={archived} label="学员组名称" maxLength={128} onValueChange={setName} required value={name} />
              <TextAreaField disabled={archived} label="用途说明" maxLength={512} onValueChange={setDescription} rows={3} value={description} />
            </div>
          </section>
          <section className={styles.detailSection}>
            <header><div><h3>组内学员</h3><p>学员可以同时属于多个培训分组。</p></div><span>{detail.members?.length ?? 0} 人</span></header>
            {detail.members?.length ? (
              <ul className={styles.personList}>
                {detail.members.map((member) => (
                  <li key={member.studentId}>
                    <div><strong>{member.userName}</strong><small>{member.realName || member.stdNumber || member.studentId}</small></div>
                    {!archived && member.studentId ? (
                      <button
                        aria-label={`移除学员 ${member.userName}`}
                        onClick={async () => {
                          try {
                            await commonAdminApi.removeStudentGroupMember(group!.id!, member.studentId!)
                            await refresh()
                          } catch (requestError) {
                            setFailure(errorMessage(requestError, '移除学员失败。'))
                          }
                        }}
                        type="button"
                      ><UserMinus size={16} /></button>
                    ) : null}
                  </li>
                ))}
              </ul>
            ) : <DataState description="尚未向该组添加学员。" title="暂无学员" />}
            {!archived ? (
              <PersonSearch
                actionLabel="加入学员组"
                excludedIds={memberIds}
                onSelect={async (user) => {
                  await commonAdminApi.addStudentGroupMember(group!.id!, { studentId: user.id ?? undefined })
                  await refresh()
                }}
                role={Role.Student}
              />
            ) : null}
          </section>
          <section className={styles.detailSection}>
            <header><div><h3>负责教师</h3><p>负责人拥有组内课程和学员管理上下文。</p></div><span>{detail.managers?.length ?? 0} 人</span></header>
            {detail.managers?.length ? (
              <ul className={styles.personList}>
                {detail.managers.map((manager) => (
                  <li key={manager.teacherId}>
                    <div><strong>{manager.userName}</strong><small>{manager.realName || manager.teacherId}</small></div>
                    <div className={styles.personAction}>
                      <StatusBadge tone={manager.roleInGroup === StudentGroupManagerRole.Owner ? 'warning' : 'info'}>{manager.roleInGroup === StudentGroupManagerRole.Owner ? '负责人' : '协作教师'}</StatusBadge>
                      {!archived && manager.teacherId && manager.roleInGroup !== StudentGroupManagerRole.Owner ? (
                        <button
                          aria-label={`移除负责教师 ${manager.userName}`}
                          onClick={async () => {
                            try {
                              await commonAdminApi.removeStudentGroupManager(group!.id!, manager.teacherId!)
                              await refresh()
                            } catch (requestError) {
                              setFailure(errorMessage(requestError, '移除负责教师失败。'))
                            }
                          }}
                          type="button"
                        ><UserMinus size={16} /></button>
                      ) : null}
                    </div>
                  </li>
                ))}
              </ul>
            ) : <DataState description="该组当前没有负责教师记录。" title="暂无负责教师" />}
            {!archived ? (
              <PersonSearch
                actionLabel="设为协作教师"
                excludedIds={managerIds}
                onSelect={async (user) => {
                  await commonAdminApi.addStudentGroupManager(group!.id!, { teacherId: user.id ?? undefined, roleInGroup: StudentGroupManagerRole.Assistant })
                  await refresh()
                }}
                role={Role.Teacher}
              />
            ) : null}
          </section>
          {failure ? <InlineFeedback tone="danger">{failure}</InlineFeedback> : null}
        </div>
      ) : null}
    </DetailDrawer>
  )
}
