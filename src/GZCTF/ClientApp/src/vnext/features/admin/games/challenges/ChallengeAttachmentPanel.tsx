import { Download, Link2, Trash2, Upload } from 'lucide-react'
import { useState } from 'react'
import { ChallengeEditDetailModel, ChallengeType, FileType } from '@Api'
import { FileField, TextField } from '../../../../shared/FormControls'
import { ActionButton, VNextConfirmDialog } from '../../../../shared/Interaction'
import { gameAdminApi } from '../../api'
import type { ChallengeEditorFeedback } from './ChallengeFlagPanel'
import styles from './ChallengeEditorPanels.module.css'

function formatFileSize(value?: number | null) {
  if (!value) return '大小未知'
  const units = ['B', 'KB', 'MB', 'GB']
  const index = Math.min(Math.floor(Math.log(value) / Math.log(1024)), units.length - 1)
  return `${(value / 1024 ** index).toFixed(index > 1 ? 1 : 0)} ${units[index]}`
}

function attachmentName(url?: string | null) {
  if (!url) return '题目附件'
  try {
    return decodeURIComponent(new URL(url, window.location.origin).pathname.split('/').filter(Boolean).at(-1) || '题目附件')
  } catch {
    return '题目附件'
  }
}

function validRemoteUrl(value: string) {
  try {
    const url = new URL(value)
    return url.protocol === 'http:' || url.protocol === 'https:'
  } catch {
    return false
  }
}

export function ChallengeAttachmentPanel({
  challenge,
  gameId,
  onChanged,
  onFeedback,
}: {
  challenge: ChallengeEditDetailModel
  gameId: number
  onChanged: () => Promise<unknown>
  onFeedback: ChallengeEditorFeedback
}) {
  const [file, setFile] = useState<File | null>(null)
  const [remoteUrl, setRemoteUrl] = useState('')
  const [pending, setPending] = useState(false)
  const [clearOpen, setClearOpen] = useState(false)

  if (challenge.type === ChallengeType.DynamicAttachment) {
    return (
      <div className={styles.noticePanel}>
        <strong>动态附件由 Flag 管理</strong>
        <p>每条 Flag 绑定一份本地文件或外部链接，平台按队伍分配对应附件。请在下方 Flag 管理中添加。</p>
      </div>
    )
  }

  const bindLocal = async () => {
    if (!file || !challenge.id) return
    setPending(true)
    try {
      const uploaded = await gameAdminApi.uploadAsset(file)
      await gameAdminApi.updateAttachment(gameId, challenge.id, {
        attachmentType: FileType.Local,
        fileHash: uploaded.hash,
      })
      await onChanged()
      setFile(null)
      onFeedback('success', `${file.name} 已上传并绑定。`)
    } catch (requestError) {
      onFeedback('danger', requestError instanceof Error ? requestError.message : '本地附件绑定失败。')
    } finally {
      setPending(false)
    }
  }

  const bindRemote = async () => {
    const value = remoteUrl.trim()
    if (!challenge.id) return
    if (!validRemoteUrl(value)) {
      onFeedback('danger', '请输入有效的 HTTP 或 HTTPS 附件地址。')
      return
    }
    setPending(true)
    try {
      await gameAdminApi.updateAttachment(gameId, challenge.id, {
        attachmentType: FileType.Remote,
        remoteUrl: value,
      })
      await onChanged()
      setRemoteUrl('')
      onFeedback('success', '外部附件已绑定。')
    } catch (requestError) {
      onFeedback('danger', requestError instanceof Error ? requestError.message : '外部附件绑定失败。')
    } finally {
      setPending(false)
    }
  }

  const clear = async () => {
    if (!challenge.id) return false
    setPending(true)
    try {
      await gameAdminApi.updateAttachment(gameId, challenge.id, { attachmentType: FileType.None })
      await onChanged()
      onFeedback('success', '题目附件已清除。')
      return true
    } catch (requestError) {
      onFeedback('danger', requestError instanceof Error ? requestError.message : '附件清除失败。')
      return false
    } finally {
      setPending(false)
    }
  }

  const attachment = challenge.attachment

  return (
    <div className={styles.panelStack}>
      {attachment?.url ? (
        <div className={styles.attachmentSummary}>
          <div>
            <span>{attachment.type === FileType.Remote ? '外部附件' : '本地附件'}</span>
            <strong>{attachmentName(attachment.url)}</strong>
            <small>{formatFileSize(attachment.fileSize)}</small>
          </div>
          <div className={styles.panelActions}>
            <ActionButton icon={<Download size={16} />} onClick={() => window.open(attachment.url as string, '_blank', 'noopener,noreferrer')} type="button">
              检查下载
            </ActionButton>
            <ActionButton disabled={pending} icon={<Trash2 size={16} />} onClick={() => setClearOpen(true)} tone="danger" type="button">
              清除
            </ActionButton>
          </div>
        </div>
      ) : (
        <div className={styles.emptyPanel}>当前题目未绑定普通附件。容器题可附带源码、说明或工具包。</div>
      )}

      <div className={styles.attachmentControls}>
        <div className={styles.bindingBlock}>
          <FileField hint={file?.name} label="本地附件" onChange={setFile} />
          <ActionButton disabled={!file || pending} icon={<Upload size={16} />} onClick={() => void bindLocal()} type="button">
            上传并绑定
          </ActionButton>
        </div>
        <div className={styles.bindingBlock}>
          <TextField label="外部附件 URL" onValueChange={setRemoteUrl} placeholder="https://..." type="url" value={remoteUrl} />
          <ActionButton disabled={!remoteUrl.trim() || pending} icon={<Link2 size={16} />} onClick={() => void bindRemote()} type="button">
            绑定链接
          </ActionButton>
        </div>
      </div>
      <VNextConfirmDialog
        description="附件记录与本地文件引用会解除绑定。"
        message="清除后选手将无法继续下载当前附件。"
        onClose={() => setClearOpen(false)}
        onConfirm={clear}
        open={clearOpen}
        title="确认清除附件"
      />
    </div>
  )
}
