import { Copy, KeyRound, LockKeyhole, Save, ShieldCheck, Upload, UserRound } from 'lucide-react'
import { ChangeEvent, FormEvent, useEffect, useMemo, useState } from 'react'
import { Link, NavLink, Navigate, useParams } from 'react-router'
import api, { ApiTokenCreateModel, ProfileUpdateModel } from '@Api'
import { ActionButton, InlineFeedback, VNextConfirmDialog, VNextDialog } from '../../shared/Interaction'
import { DataState, PageHeading, StatusPill } from '../../shared/Primitives'
import { errorMessage } from '../../shared/errors'
import { useVNextPageTitle } from '../../shared/useVNextPageTitle'
import { useAccountLogout, useCurrentAccount } from '../account/useCurrentAccount'
import styles from './SettingsPage.module.css'

type SettingsSection = 'profile' | 'security' | 'tokens'

const sections: Array<{ id: SettingsSection; label: string; icon: typeof UserRound }> = [
  { id: 'profile', label: '个人资料', icon: UserRound },
  { id: 'security', label: '账户安全', icon: LockKeyhole },
  { id: 'tokens', label: 'API Token', icon: KeyRound },
]

const scopeOptions = [
  ['images:read', '读取镜像'],
  ['images:write', '写入镜像'],
  ['images:delete', '删除镜像'],
  ['challenges:read', '读取比赛题目'],
  ['challenges:write', '导入比赛题目'],
  ['challenges:delete', '删除比赛题目'],
  ['operations:read', '读取异步操作'],
] as const

function formatTime(value?: number | null) {
  return value ? new Intl.DateTimeFormat('zh-CN', { dateStyle: 'medium', timeStyle: 'short' }).format(value) : '未设置'
}

function ProfileSettings() {
  const account = useCurrentAccount()
  const [model, setModel] = useState<ProfileUpdateModel>({})
  const [email, setEmail] = useState('')
  const [avatarFile, setAvatarFile] = useState<File | null>(null)
  const [avatarPreview, setAvatarPreview] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const [feedback, setFeedback] = useState<{ tone: 'success' | 'danger'; message: string } | null>(null)

  useEffect(() => {
    if (!account.user) return
    setModel({
      userName: account.user.userName,
      bio: account.user.bio,
      phone: account.user.phone,
      realName: account.user.realName,
      stdNumber: account.user.stdNumber,
    })
    setEmail(account.user.email ?? '')
  }, [account.user])

  useEffect(() => {
    if (!avatarFile) {
      setAvatarPreview(null)
      return undefined
    }
    const url = URL.createObjectURL(avatarFile)
    setAvatarPreview(url)
    return () => URL.revokeObjectURL(url)
  }, [avatarFile])

  if (!account.user && !account.error) return <DataState description="正在读取本人资料。" loading title="资料加载中" />
  if (!account.user) return <DataState description="请先登录后再修改账户资料。" title="需要登录" />

  const updateField =
    (field: keyof ProfileUpdateModel) => (event: ChangeEvent<HTMLInputElement | HTMLTextAreaElement>) => {
      const value = event.currentTarget.value
      setModel((current) => ({ ...current, [field]: value }))
    }

  const saveProfile = async (event: FormEvent) => {
    event.preventDefault()
    setSaving(true)
    setFeedback(null)
    try {
      await api.account.accountUpdate(model)
      await account.mutate()
      setFeedback({ tone: 'success', message: '个人资料已保存，并同步更新全局账户信息。' })
    } catch (error) {
      setFeedback({ tone: 'danger', message: errorMessage(error, '个人资料保存失败。') })
    } finally {
      setSaving(false)
    }
  }

  const uploadAvatar = async () => {
    if (!avatarFile) return
    setSaving(true)
    setFeedback(null)
    try {
      await api.account.accountAvatar({ file: avatarFile })
      await account.mutate()
      setAvatarFile(null)
      setFeedback({ tone: 'success', message: '头像已更新。' })
    } catch (error) {
      setFeedback({ tone: 'danger', message: errorMessage(error, '头像上传失败。') })
    } finally {
      setSaving(false)
    }
  }

  const changeEmail = async () => {
    if (!email.trim() || email === account.user?.email) return
    setSaving(true)
    setFeedback(null)
    try {
      const response = await api.account.accountChangeEmail({ newMail: email.trim() })
      setFeedback({
        tone: 'success',
        message: response.data.data ? '确认邮件已发送，请完成邮箱验证。' : '邮箱变更请求已提交。',
      })
    } catch (error) {
      setFeedback({ tone: 'danger', message: errorMessage(error, '邮箱变更请求失败。') })
    } finally {
      setSaving(false)
    }
  }

  return (
    <div className={styles.settingsContent}>
      <section className={styles.sectionIntro}>
        <span>PROFILE</span>
        <h2>个人资料</h2>
        <p>公开资料用于平台内展示；真实姓名、学号和手机号仅用于内部管理。</p>
      </section>

      {feedback ? <InlineFeedback tone={feedback.tone}>{feedback.message}</InlineFeedback> : null}

      <section className={styles.avatarSection}>
        <div className={styles.avatarPreview}>
          {avatarPreview || account.user.avatar ? (
            <img alt="头像预览" src={avatarPreview || account.user.avatar || ''} />
          ) : (
            <UserRound size={28} />
          )}
        </div>
        <div>
          <strong>头像</strong>
          <p>支持常见图片格式，文件不超过 3MB。</p>
          <div className={styles.inlineActions}>
            <label className={styles.fileButton}>
              <Upload size={16} />
              选择图片
              <input
                accept="image/*"
                onChange={(event) => setAvatarFile(event.currentTarget.files?.[0] ?? null)}
                type="file"
              />
            </label>
            <ActionButton disabled={!avatarFile || saving} onClick={uploadAvatar} tone="secondary" type="button">
              上传头像
            </ActionButton>
          </div>
        </div>
      </section>

      <form className={styles.form} onSubmit={saveProfile}>
        <div className={styles.formGrid}>
          <label>
            <span>用户名</span>
            <input
              maxLength={15}
              minLength={3}
              onChange={updateField('userName')}
              required
              value={model.userName ?? ''}
            />
          </label>
          <label>
            <span>
              真实姓名 <small>内部资料</small>
            </span>
            <input maxLength={128} onChange={updateField('realName')} value={model.realName ?? ''} />
          </label>
          <label>
            <span>
              学号 <small>内部资料</small>
            </span>
            <input maxLength={64} onChange={updateField('stdNumber')} value={model.stdNumber ?? ''} />
          </label>
          <label>
            <span>
              手机号 <small>内部资料</small>
            </span>
            <input onChange={updateField('phone')} value={model.phone ?? ''} />
          </label>
        </div>
        <label>
          <span>个人简介</span>
          <textarea maxLength={128} onChange={updateField('bio')} rows={4} value={model.bio ?? ''} />
        </label>
        <div className={styles.formActions}>
          <ActionButton disabled={saving} icon={<Save size={16} />} tone="primary" type="submit">
            保存资料
          </ActionButton>
        </div>
      </form>

      <section className={styles.emailSection}>
        <div>
          <strong>登录邮箱</strong>
          <p>修改后需要通过新邮箱完成确认。</p>
        </div>
        <div className={styles.emailControl}>
          <input onChange={(event) => setEmail(event.currentTarget.value)} type="email" value={email} />
          <ActionButton
            disabled={saving || !email.trim() || email === account.user.email}
            onClick={changeEmail}
            type="button"
          >
            申请变更
          </ActionButton>
        </div>
      </section>
    </div>
  )
}

function SecuritySettings() {
  const logout = useAccountLogout()
  const [oldPassword, setOldPassword] = useState('')
  const [newPassword, setNewPassword] = useState('')
  const [confirmation, setConfirmation] = useState('')
  const [saving, setSaving] = useState(false)
  const [feedback, setFeedback] = useState<string | null>(null)

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    if (newPassword !== confirmation) {
      setFeedback('两次输入的新密码不一致。')
      return
    }
    setSaving(true)
    setFeedback(null)
    try {
      await api.account.accountChangePassword({ old: oldPassword, new: newPassword })
      await logout({ redirectTo: '/account/login' })
    } catch (error) {
      setFeedback(errorMessage(error, '密码修改失败。'))
    } finally {
      setSaving(false)
    }
  }

  return (
    <div className={styles.settingsContent}>
      <section className={styles.sectionIntro}>
        <span>SECURITY</span>
        <h2>账户安全</h2>
        <p>修改密码后当前会话会退出，需要使用新密码重新登录。</p>
      </section>
      {feedback ? <InlineFeedback tone="danger">{feedback}</InlineFeedback> : null}
      <form className={styles.form} onSubmit={submit}>
        <label>
          <span>当前密码</span>
          <input
            autoComplete="current-password"
            minLength={6}
            onChange={(event) => setOldPassword(event.currentTarget.value)}
            required
            type="password"
            value={oldPassword}
          />
        </label>
        <div className={styles.formGrid}>
          <label>
            <span>新密码</span>
            <input
              autoComplete="new-password"
              minLength={6}
              onChange={(event) => setNewPassword(event.currentTarget.value)}
              required
              type="password"
              value={newPassword}
            />
          </label>
          <label>
            <span>确认新密码</span>
            <input
              autoComplete="new-password"
              minLength={6}
              onChange={(event) => setConfirmation(event.currentTarget.value)}
              required
              type="password"
              value={confirmation}
            />
          </label>
        </div>
        <div className={styles.formActions}>
          <ActionButton
            disabled={saving || !oldPassword || !newPassword || !confirmation}
            icon={<ShieldCheck size={16} />}
            tone="primary"
            type="submit"
          >
            修改并退出
          </ActionButton>
        </div>
      </form>
    </div>
  )
}

function TokenSettings() {
  const { data: tokens, error, mutate } = api.apiTokens.useApiTokensList({ revalidateOnFocus: false })
  const [createOpen, setCreateOpen] = useState(false)
  const [name, setName] = useState('')
  const [scopes, setScopes] = useState<string[]>(['images:read'])
  const [requestsPerMinute, setRequestsPerMinute] = useState(60)
  const [expiresAt, setExpiresAt] = useState('')
  const [secret, setSecret] = useState<string | null>(null)
  const [revokeId, setRevokeId] = useState<string | null>(null)
  const [submitting, setSubmitting] = useState(false)
  const [feedback, setFeedback] = useState<string | null>(null)

  const activeTokens = useMemo(() => (tokens ?? []).filter((token) => !token.revokedAt), [tokens])

  const issue = async () => {
    const model: ApiTokenCreateModel = {
      name: name.trim(),
      scopes,
      requestsPerMinute,
      expiresAt: expiresAt ? new Date(expiresAt).getTime() : null,
      resources: [],
    }
    setSubmitting(true)
    setFeedback(null)
    try {
      const response = await api.apiTokens.apiTokensIssue(model)
      setCreateOpen(false)
      setSecret(response.data.plainTextToken ?? '')
      setName('')
      setScopes(['images:read'])
      setRequestsPerMinute(60)
      setExpiresAt('')
      await mutate()
    } catch (error) {
      setFeedback(errorMessage(error, 'Token 创建失败。'))
    } finally {
      setSubmitting(false)
    }
  }

  const revoke = async () => {
    if (!revokeId) return false
    setSubmitting(true)
    setFeedback(null)
    try {
      await api.apiTokens.apiTokensRevoke(revokeId)
      await mutate()
      return true
    } catch (requestError) {
      setFeedback(errorMessage(requestError, 'Token 撤销失败。'))
      return false
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <div className={styles.settingsContent}>
      <section className={styles.sectionIntroRow}>
        <div>
          <span>API ACCESS</span>
          <h2>API Token</h2>
          <p>用于镜像、题目和异步操作接口。Token 明文仅在创建成功后显示一次。</p>
        </div>
        <ActionButton icon={<KeyRound size={16} />} onClick={() => setCreateOpen(true)} tone="primary" type="button">
          创建 Token
        </ActionButton>
      </section>
      {feedback || error ? <InlineFeedback tone="danger">{feedback || 'Token 列表加载失败。'}</InlineFeedback> : null}
      {!tokens && !error ? (
        <DataState description="正在读取 Token 列表。" loading title="Token 加载中" />
      ) : (
        <div className={styles.tableWrap}>
          <table className={styles.table}>
            <thead>
              <tr>
                <th>名称</th>
                <th>权限范围</th>
                <th>配额</th>
                <th>最后使用</th>
                <th>过期时间</th>
                <th>状态</th>
                <th />
              </tr>
            </thead>
            <tbody>
              {activeTokens.map((token) => {
                const expired = Boolean(token.expiresAt && token.expiresAt <= Date.now())
                return (
                  <tr key={token.id}>
                    <td>
                      <strong>{token.name || '未命名'}</strong>
                      <small>{token.id}</small>
                    </td>
                    <td>
                      <div className={styles.scopeList}>
                        {(token.scopes ?? []).map((scope) => (
                          <span key={scope}>{scope}</span>
                        ))}
                      </div>
                    </td>
                    <td>{token.requestsPerMinute ?? '-'} / min</td>
                    <td>{formatTime(token.lastUsedAt)}</td>
                    <td>{formatTime(token.expiresAt)}</td>
                    <td>
                      <StatusPill tone={expired ? 'neutral' : 'success'}>{expired ? '已过期' : '有效'}</StatusPill>
                    </td>
                    <td>
                      <button
                        className={styles.revokeButton}
                        disabled={expired}
                        onClick={() => setRevokeId(token.id ?? null)}
                        type="button"
                      >
                        撤销
                      </button>
                    </td>
                  </tr>
                )
              })}
              {!activeTokens.length ? (
                <tr>
                  <td className={styles.emptyCell} colSpan={7}>
                    尚未创建 API Token。
                  </td>
                </tr>
              ) : null}
            </tbody>
          </table>
        </div>
      )}

      <VNextDialog
        description="选择最小必要权限；镜像写入和题目导入建议使用不同 Token。"
        eyebrow="ISSUE TOKEN"
        footer={
          <>
            <ActionButton onClick={() => setCreateOpen(false)} type="button">
              取消
            </ActionButton>
            <ActionButton
              disabled={submitting || !name.trim() || !scopes.length}
              onClick={issue}
              tone="primary"
              type="button"
            >
              确认创建
            </ActionButton>
          </>
        }
        onClose={() => setCreateOpen(false)}
        open={createOpen}
        title="创建 API Token"
      >
        <div className={styles.dialogForm}>
          <label>
            <span>名称</span>
            <input
              maxLength={128}
              onChange={(event) => setName(event.currentTarget.value)}
              placeholder="例如：镜像上传脚本"
              value={name}
            />
          </label>
          <fieldset>
            <legend>权限范围</legend>
            <div className={styles.checkGrid}>
              {scopeOptions.map(([value, label]) => (
                <label key={value}>
                  <input
                    checked={scopes.includes(value)}
                    onChange={(event) => {
                      const checked = event.currentTarget.checked
                      setScopes((current) => (checked ? [...current, value] : current.filter((item) => item !== value)))
                    }}
                    type="checkbox"
                  />
                  <span>
                    <strong>{label}</strong>
                    <small>{value}</small>
                  </span>
                </label>
              ))}
            </div>
          </fieldset>
          <div className={styles.formGrid}>
            <label>
              <span>每分钟请求数</span>
              <input
                max={10000}
                min={1}
                onChange={(event) => setRequestsPerMinute(Number(event.currentTarget.value) || 60)}
                type="number"
                value={requestsPerMinute}
              />
            </label>
            <label>
              <span>过期时间</span>
              <input
                onChange={(event) => setExpiresAt(event.currentTarget.value)}
                type="datetime-local"
                value={expiresAt}
              />
            </label>
          </div>
          {feedback ? <InlineFeedback tone="danger">{feedback}</InlineFeedback> : null}
        </div>
      </VNextDialog>

      <VNextDialog
        description="关闭后无法再次查看明文，请立即存放到安全位置。"
        eyebrow="TOKEN ISSUED"
        footer={
          <ActionButton onClick={() => setSecret(null)} tone="primary" type="button">
            我已保存
          </ActionButton>
        }
        onClose={() => setSecret(null)}
        open={secret !== null}
        title="API Token 已创建"
      >
        <div className={styles.secretBox}>
          <code>{secret}</code>
          <ActionButton
            icon={<Copy size={16} />}
            onClick={() => secret && navigator.clipboard.writeText(secret)}
            type="button"
          >
            复制
          </ActionButton>
        </div>
      </VNextDialog>

      <VNextConfirmDialog
        confirmLabel="撤销 Token"
        message="撤销后使用此凭据的脚本和集成会立即失去访问权限，并且无法恢复。"
        onClose={() => setRevokeId(null)}
        onConfirm={revoke}
        open={revokeId !== null}
        title="确认撤销此 Token？"
      />
    </div>
  )
}

export function SettingsPage() {
  const { section = 'profile' } = useParams()
  const validSection = sections.some((item) => item.id === section) ? (section as SettingsSection) : null
  useVNextPageTitle('账户设置')

  if (!validSection) return <Navigate replace to="/settings/profile" />

  return (
    <div className={styles.page}>
      <PageHeading description="管理个人资料、账户安全和程序化访问凭据。" eyebrow="ACCOUNT CONTROL" title="账户设置" />
      <div className={styles.layout}>
        <nav aria-label="设置分类" className={styles.sideNav}>
          {sections.map((item) => {
            const Icon = item.icon
            return (
              <NavLink
                className={({ isActive }) => (isActive ? styles.sideLinkActive : styles.sideLink)}
                key={item.id}
                to={`/settings/${item.id}`}
              >
                <Icon size={17} />
                {item.label}
              </NavLink>
            )
          })}
          <Link className={styles.backHome} to="/">
            返回平台首页
          </Link>
        </nav>
        <main className={styles.panel}>
          {validSection === 'profile' ? <ProfileSettings /> : null}
          {validSection === 'security' ? <SecuritySettings /> : null}
          {validSection === 'tokens' ? <TokenSettings /> : null}
        </main>
      </div>
    </div>
  )
}
