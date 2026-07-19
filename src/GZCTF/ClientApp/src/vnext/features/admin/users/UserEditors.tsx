import { Copy, KeyRound, Save, Trash2, UserPlus } from 'lucide-react'
import { useEffect, useMemo, useState } from 'react'
import {
  type AdminUserInfoModel,
  Role,
  type StudentGroupBriefModel,
  type UserCreateModel,
  type UserInfoModel,
} from '@Api'
import { PasswordField, SelectField, TextAreaField, TextField, ToggleField } from '../../../shared/FormControls'
import { ActionButton, InlineFeedback, VNextDialog } from '../../../shared/Interaction'
import { errorMessage } from '../../../shared/errors'
import { DetailDrawer } from '../shared/AdminWorkbench'
import { adminRoleLabel, assignableRoles, isStudentRole } from './userPresentation'
import styles from './AdminUsersPage.module.css'

const emptyCreateDraft: UserCreateModel = {
  userName: '',
  password: '',
  email: '',
  realName: '',
  stdNumber: '',
  phone: '',
  assignedRole: Role.Student,
  studentGroupIds: [],
}

function GroupSelector({
  groups,
  value,
  onChange,
}: {
  groups: StudentGroupBriefModel[]
  value: number[]
  onChange: (value: number[]) => void
}) {
  if (!groups.length) return <InlineFeedback>当前没有可用的学员组，用户可以稍后在学员组管理中分配。</InlineFeedback>
  return (
    <fieldset className={styles.groupSelector}>
      <legend>所属学员组</legend>
      <div>
        {groups.map((group) => {
          const id = group.id
          if (!id) return null
          return (
            <label key={id}>
              <input
                checked={value.includes(id)}
                onChange={(event) =>
                  onChange(event.currentTarget.checked ? [...value, id] : value.filter((item) => item !== id))
                }
                type="checkbox"
              />
              <span>
                <strong>{group.name}</strong>
                <small>{group.memberCount ?? 0} 名学员</small>
              </span>
            </label>
          )
        })}
      </div>
    </fieldset>
  )
}

function roleOptions(actorRole?: Role | null) {
  return assignableRoles(actorRole).map((role) => ({ label: adminRoleLabel(role), value: role }))
}

export function UserCreateDialog({
  open,
  groups,
  actorRole,
  onClose,
  onCreate,
}: {
  open: boolean
  groups: StudentGroupBriefModel[]
  actorRole?: Role | null
  onClose: () => void
  onCreate: (payload: UserCreateModel) => Promise<void>
}) {
  const [draft, setDraft] = useState<UserCreateModel>(emptyCreateDraft)
  const [saving, setSaving] = useState(false)
  const [failure, setFailure] = useState<string | null>(null)
  const options = useMemo(() => roleOptions(actorRole), [actorRole])
  const valid = draft.userName.trim().length >= 3 && Boolean(draft.password) && Boolean(draft.email.trim())

  useEffect(() => {
    if (!open) {
      setDraft(emptyCreateDraft)
      setFailure(null)
    }
  }, [open])

  const submit = async () => {
    if (!valid || saving) return
    setSaving(true)
    setFailure(null)
    try {
      await onCreate({
        ...draft,
        userName: draft.userName.trim(),
        email: draft.email.trim(),
        realName: draft.realName?.trim() || undefined,
        stdNumber: draft.stdNumber?.trim() || undefined,
        phone: draft.phone?.trim() || undefined,
        studentGroupIds: isStudentRole(draft.assignedRole) ? draft.studentGroupIds : [],
      })
      onClose()
    } catch (requestError) {
      setFailure(errorMessage(requestError, '用户创建失败。'))
    } finally {
      setSaving(false)
    }
  }

  return (
    <VNextDialog
      description="创建单个账号并设置初始身份；批量导入能力留在 API 层。"
      eyebrow="USER CREATE"
      footer={
        <>
          <ActionButton disabled={saving} onClick={onClose} type="button">取消</ActionButton>
          <ActionButton disabled={!valid || saving} icon={<UserPlus size={16} />} onClick={() => void submit()} tone="primary" type="button">
            {saving ? '正在创建' : '创建用户'}
          </ActionButton>
        </>
      }
      onClose={() => { if (!saving) onClose() }}
      open={open}
      title="新建用户"
      wide
    >
      <div className={styles.formGrid}>
        <TextField label="用户名" maxLength={15} minLength={3} onValueChange={(userName) => setDraft({ ...draft, userName })} required value={draft.userName} />
        <PasswordField label="初始密码" onValueChange={(password) => setDraft({ ...draft, password })} required value={draft.password} />
        <TextField label="邮箱" onValueChange={(email) => setDraft({ ...draft, email })} required type="email" value={draft.email} />
        <SelectField label="权限角色" onValueChange={(value) => setDraft({ ...draft, assignedRole: value as Role })} value={draft.assignedRole ?? Role.Student}>
          {options.map((option) => <option key={option.value} value={option.value}>{option.label}</option>)}
        </SelectField>
        <TextField label="真实姓名" maxLength={128} onValueChange={(realName) => setDraft({ ...draft, realName })} value={draft.realName ?? ''} />
        <TextField label="学号" maxLength={64} onValueChange={(stdNumber) => setDraft({ ...draft, stdNumber })} value={draft.stdNumber ?? ''} />
        <TextField label="手机号" onValueChange={(phone) => setDraft({ ...draft, phone })} type="tel" value={draft.phone ?? ''} />
      </div>
      {isStudentRole(draft.assignedRole) ? (
        <GroupSelector groups={groups} onChange={(studentGroupIds) => setDraft({ ...draft, studentGroupIds })} value={draft.studentGroupIds ?? []} />
      ) : null}
      {failure ? <InlineFeedback tone="danger">{failure}</InlineFeedback> : null}
    </VNextDialog>
  )
}

export function UserDetailDrawer({
  user,
  groups,
  actorRole,
  currentUserId,
  onClose,
  onSave,
  onRequestDelete,
  onRequestReset,
}: {
  user: UserInfoModel | null
  groups: StudentGroupBriefModel[]
  actorRole?: Role | null
  currentUserId?: string | null
  onClose: () => void
  onSave: (userId: string, payload: AdminUserInfoModel) => Promise<void>
  onRequestDelete: (user: UserInfoModel) => void
  onRequestReset: (user: UserInfoModel) => void
}) {
  const [draft, setDraft] = useState<AdminUserInfoModel>({})
  const [saving, setSaving] = useState(false)
  const [failure, setFailure] = useState<string | null>(null)
  const options = useMemo(() => roleOptions(actorRole), [actorRole])
  const isSelf = Boolean(user?.id && user.id === currentUserId)

  useEffect(() => {
    if (!user) return
    setDraft({
      userName: user.userName,
      email: user.email,
      bio: user.bio,
      phone: user.phone,
      realName: user.realName,
      stdNumber: user.stdNumber,
      emailConfirmed: user.emailConfirmed,
      role: user.role,
      studentGroupIds: user.studentGroups?.flatMap((group) => (group.id ? [group.id] : [])) ?? [],
    })
    setFailure(null)
  }, [user])

  const save = async () => {
    if (!user?.id || saving) return
    setSaving(true)
    setFailure(null)
    try {
      await onSave(user.id, {
        ...draft,
        userName: draft.userName?.trim(),
        email: draft.email?.trim(),
        realName: draft.realName?.trim(),
        stdNumber: draft.stdNumber?.trim(),
        phone: draft.phone?.trim(),
        bio: draft.bio?.trim(),
        studentGroupIds: isStudentRole(draft.role) ? draft.studentGroupIds : [],
      })
      onClose()
    } catch (requestError) {
      setFailure(errorMessage(requestError, '用户信息保存失败。'))
    } finally {
      setSaving(false)
    }
  }

  return (
    <DetailDrawer
      description={user?.id ? `用户 ID ${user.id}` : undefined}
      footer={
        user ? (
          <div className={styles.drawerActions}>
            <div>
              <ActionButton disabled={saving} icon={<KeyRound size={16} />} onClick={() => onRequestReset(user)} type="button">重置密码</ActionButton>
              <ActionButton disabled={saving || isSelf} icon={<Trash2 size={16} />} onClick={() => onRequestDelete(user)} tone="danger" type="button">删除</ActionButton>
            </div>
            <ActionButton disabled={saving} icon={<Save size={16} />} onClick={() => void save()} tone="primary" type="button">{saving ? '保存中' : '保存修改'}</ActionButton>
          </div>
        ) : null
      }
      onClose={onClose}
      open={Boolean(user)}
      title={user?.userName || '用户详情'}
    >
      {user ? (
        <div className={styles.drawerContent}>
          <section className={styles.identitySummary}>
            <span className={styles.avatar}>{user.avatar ? <img alt="" src={user.avatar} /> : user.userName?.slice(0, 1).toUpperCase()}</span>
            <div><strong>{user.userName}</strong><small>{adminRoleLabel(user.role)} · {user.emailConfirmed ? '账号已激活' : '账号未激活'}</small></div>
          </section>
          <div className={styles.formGrid}>
            <TextField label="用户名" maxLength={15} minLength={3} onValueChange={(userName) => setDraft({ ...draft, userName })} value={draft.userName ?? ''} />
            <SelectField disabled={isSelf} label="权限角色" onValueChange={(value) => setDraft({ ...draft, role: value as Role })} value={draft.role ?? Role.Student}>
              {options.map((option) => <option key={option.value} value={option.value}>{option.label}</option>)}
            </SelectField>
            <TextField label="邮箱" onValueChange={(email) => setDraft({ ...draft, email })} type="email" value={draft.email ?? ''} />
            <TextField label="手机号" onValueChange={(phone) => setDraft({ ...draft, phone })} type="tel" value={draft.phone ?? ''} />
            <TextField label="真实姓名" maxLength={128} onValueChange={(realName) => setDraft({ ...draft, realName })} value={draft.realName ?? ''} />
            <TextField label="学号" maxLength={64} onValueChange={(stdNumber) => setDraft({ ...draft, stdNumber })} value={draft.stdNumber ?? ''} />
          </div>
          <TextAreaField label="个人简介" maxLength={128} onValueChange={(bio) => setDraft({ ...draft, bio })} rows={3} value={draft.bio ?? ''} />
          <ToggleField checked={draft.emailConfirmed ?? false} description="关闭后该用户无法正常登录平台。" label="账号允许登录" onChange={(emailConfirmed) => setDraft({ ...draft, emailConfirmed })} />
          {isStudentRole(draft.role) ? <GroupSelector groups={groups} onChange={(studentGroupIds) => setDraft({ ...draft, studentGroupIds })} value={draft.studentGroupIds ?? []} /> : null}
          <dl className={styles.factList}>
            <div><dt>最近 IP</dt><dd>{user.ip || '—'}</dd></div>
            <div><dt>注册时间</dt><dd>{user.registerTimeUtc ? new Date(user.registerTimeUtc).toLocaleString('zh-CN') : '—'}</dd></div>
            <div><dt>最后访问</dt><dd>{user.lastVisitedUtc ? new Date(user.lastVisitedUtc).toLocaleString('zh-CN') : '—'}</dd></div>
          </dl>
          {isSelf ? <InlineFeedback>当前账号不能删除自己或修改自己的权限角色。</InlineFeedback> : null}
          {failure ? <InlineFeedback tone="danger">{failure}</InlineFeedback> : null}
        </div>
      ) : null}
    </DetailDrawer>
  )
}

export function PasswordResultDialog({ password, onClose }: { password: string | null; onClose: () => void }) {
  const [copied, setCopied] = useState(false)
  return (
    <VNextDialog
      description="该随机密码只在本次操作结果中展示，请通过安全渠道交付给用户。"
      eyebrow="PASSWORD RESET"
      footer={<ActionButton onClick={onClose} tone="primary" type="button">完成</ActionButton>}
      onClose={onClose}
      open={Boolean(password)}
      title="密码已重置"
    >
      <div className={styles.passwordResult}>
        <code>{password}</code>
        <ActionButton
          icon={<Copy size={16} />}
          onClick={async () => {
            if (!password) return
            await navigator.clipboard.writeText(password)
            setCopied(true)
          }}
          type="button"
        >
          {copied ? '已复制' : '复制密码'}
        </ActionButton>
      </div>
    </VNextDialog>
  )
}
