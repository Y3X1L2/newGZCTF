import { ShieldCheck } from 'lucide-react'
import { FormEvent, useState } from 'react'
import { ActionButton, InlineFeedback } from '../../shared/Interaction'
import { errorMessage } from '../../shared/errors'
import { useAccountLogout } from '../account/useCurrentAccount'
import styles from './SettingsPage.module.css'
import { settingsApi } from './settingsApi'

export function SecuritySettings() {
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
      await settingsApi.changePassword(oldPassword, newPassword)
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
      <form className={styles.form} onSubmit={(event) => void submit(event)}>
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
