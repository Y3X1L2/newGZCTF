import { useEffect, useState } from 'react'
import { GameNotice } from '@Api'
import { TextAreaField } from '../../../../shared/FormControls'
import { ActionButton, InlineFeedback, VNextDialog } from '../../../../shared/Interaction'
import { MarkdownContent } from '../../../../shared/MarkdownContent'
import { errorMessage } from '../../../../shared/errors'
import { gameOperationsAdminApi } from '../../api'
import styles from '../GameOperations.module.css'
import { noticeContent } from '../gameOperationsPresentation'

export function GameNoticeDialog({
  gameId,
  notice,
  open,
  onClose,
  onSaved,
}: {
  gameId: number
  notice: GameNotice | null
  open: boolean
  onClose: () => void
  onSaved: () => Promise<unknown>
}) {
  const [content, setContent] = useState('')
  const [saving, setSaving] = useState(false)
  const [feedback, setFeedback] = useState<string | null>(null)

  useEffect(() => {
    if (!open) return
    setContent(notice ? noticeContent(notice) : '')
    setSaving(false)
    setFeedback(null)
  }, [notice, open])

  const save = async () => {
    const normalized = content.trim()
    if (!normalized) {
      setFeedback('请输入公告内容。')
      return
    }
    if (notice && normalized === noticeContent(notice)) {
      setFeedback('公告内容没有变化。')
      return
    }
    setSaving(true)
    setFeedback(null)
    try {
      if (notice) await gameOperationsAdminApi.updateNotice(gameId, notice.id, { content: normalized })
      else await gameOperationsAdminApi.createNotice(gameId, { content: normalized })
      await onSaved()
      onClose()
    } catch (requestError) {
      setFeedback(errorMessage(requestError, `公告${notice ? '保存' : '发布'}失败。`))
    } finally {
      setSaving(false)
    }
  }

  return (
    <VNextDialog
      description="公告支持 Markdown；保存成功后由服务器通过现有实时通道通知选手。"
      eyebrow="GAME NOTICE"
      footer={<><ActionButton disabled={saving} onClick={onClose} type="button">取消</ActionButton><ActionButton disabled={saving} onClick={() => void save()} tone="primary" type="button">{saving ? '正在保存' : notice ? '保存公告' : '发布公告'}</ActionButton></>}
      onClose={() => { if (!saving) onClose() }}
      open={open}
      title={notice ? '编辑比赛公告' : '发布比赛公告'}
      wide
    >
      <div className={styles.formStack}>
        {feedback ? <InlineFeedback tone="danger">{feedback}</InlineFeedback> : null}
        <div className={styles.markdownGrid}>
          <TextAreaField label="公告内容 Markdown" onValueChange={setContent} required rows={16} value={content} />
          <article className={styles.preview}><header>实时预览</header><MarkdownContent source={content || '暂无公告内容。'} /></article>
        </div>
      </div>
    </VNextDialog>
  )
}
