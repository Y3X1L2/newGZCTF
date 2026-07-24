import { LogOut, Settings, Trash2 } from 'lucide-react'
import { ActionButton } from '../../shared/Interaction'
import { TeamConfirmation } from './TeamConfirmationDialog'
import styles from './TeamsPage.module.css'

interface TeamSettingsPanelProps {
  editName: string
  editBio: string
  avatarFile: File | null
  submitting: boolean
  onEditName: (value: string) => void
  onEditBio: (value: string) => void
  onAvatarFile: (file: File | null) => void
  onSave: () => Promise<void>
  onUploadAvatar: () => Promise<void>
  onConfirm: (confirmation: TeamConfirmation) => void
}

export function TeamSettingsPanel({
  editName,
  editBio,
  avatarFile,
  submitting,
  onEditName,
  onEditBio,
  onAvatarFile,
  onSave,
  onUploadAvatar,
  onConfirm,
}: TeamSettingsPanelProps) {
  return (
    <div className={styles.settingsStack}>
      <section className={styles.teamSettings}>
        <header>
          <span>TEAM PROFILE</span>
          <h3>战队资料</h3>
        </header>
        <label>
          <span>战队名称</span>
          <input maxLength={20} onChange={(event) => onEditName(event.currentTarget.value)} value={editName} />
        </label>
        <label>
          <span>战队简介</span>
          <textarea
            maxLength={72}
            onChange={(event) => onEditBio(event.currentTarget.value)}
            rows={4}
            value={editBio}
          />
        </label>
        <div className={styles.settingsActions}>
          <ActionButton
            disabled={submitting || !editName.trim()}
            icon={<Settings size={15} />}
            onClick={() => void onSave()}
            tone="primary"
            type="button"
          >
            保存资料
          </ActionButton>
        </div>
      </section>
      <section className={styles.avatarUpload}>
        <div>
          <span>TEAM AVATAR</span>
          <h3>战队头像</h3>
          <p>选择图片后单独上传，不会覆盖未保存的文字资料。</p>
        </div>
        <label className={styles.fileButton}>
          选择图片
          <input
            accept="image/*"
            onChange={(event) => onAvatarFile(event.currentTarget.files?.[0] ?? null)}
            type="file"
          />
        </label>
        <ActionButton disabled={!avatarFile || submitting} onClick={() => void onUploadAvatar()} type="button">
          上传
        </ActionButton>
      </section>
      <section className={styles.dangerZone}>
        <div>
          <span>DANGER ZONE</span>
          <h3>高风险操作</h3>
          <p>删除战队不可恢复；队长必须先转让权限才能退出。</p>
        </div>
        <div>
          <ActionButton
            icon={<LogOut size={15} />}
            onClick={() => onConfirm({ kind: 'leave' })}
            tone="secondary"
            type="button"
          >
            退出战队
          </ActionButton>
          <ActionButton
            icon={<Trash2 size={15} />}
            onClick={() => onConfirm({ kind: 'delete' })}
            tone="danger"
            type="button"
          >
            删除战队
          </ActionButton>
        </div>
      </section>
    </div>
  )
}
