import { Save, Upload, UserRound } from 'lucide-react'
import { ChangeEvent, FormEvent, useEffect, useState } from 'react'
import { ProfileUpdateModel } from '@Api'
import { ActionButton, InlineFeedback } from '../../shared/Interaction'
import { DataState } from '../../shared/Primitives'
import { errorMessage } from '../../shared/errors'
import { useCurrentAccount } from '../account/useCurrentAccount'
import styles from './SettingsPage.module.css'
import { settingsApi } from './settingsApi'

export function ProfileSettings() {
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
      await settingsApi.updateProfile(model)
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
      await settingsApi.uploadAvatar(avatarFile)
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
      const confirmationSent = await settingsApi.changeEmail(email.trim())
      setFeedback({
        tone: 'success',
        message: confirmationSent ? '确认邮件已发送，请完成邮箱验证。' : '邮箱变更请求已提交。',
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
            <ActionButton
              disabled={!avatarFile || saving}
              onClick={() => void uploadAvatar()}
              tone="secondary"
              type="button"
            >
              上传头像
            </ActionButton>
          </div>
        </div>
      </section>
      <form className={styles.form} onSubmit={(event) => void saveProfile(event)}>
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
            onClick={() => void changeEmail()}
            type="button"
          >
            申请变更
          </ActionButton>
        </div>
      </section>
    </div>
  )
}
