import { Edit3, ExternalLink, Flag, Plus, Trash2 } from 'lucide-react'
import { useMemo, useState } from 'react'
import {
  AnswerType,
  ChallengeEditDetailModel,
  ChallengeType,
  FileType,
  FlagInfoModel,
  FlagScoreMode,
  TaskStatus,
} from '@Api'
import { ActionButton, VNextConfirmDialog } from '../../../../shared/Interaction'
import { gameAdminApi } from '../../api'
import { StatusBadge } from '../../shared/AdminWorkbench'
import { FlagEditorDialog } from './FlagEditorDialog'
import { flagCreatePayload, type FlagEditorDraft } from './flagEditorModel'
import styles from './ChallengeEditorPanels.module.css'

export type ChallengeEditorFeedback = (tone: 'success' | 'danger' | 'neutral', message: string) => void

function answerTypeLabel(answerType?: AnswerType) {
  if (answerType === AnswerType.File) return '文件答案'
  if (answerType === AnswerType.Custom) return '自定义文本'
  return 'Flag 文本'
}

function flagLabel(flag: FlagInfoModel) {
  return flag.customName?.trim() || `Flag #${flag.id ?? '—'}`
}

export function ChallengeFlagPanel({
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
  const [dialogOpen, setDialogOpen] = useState(false)
  const [editingFlag, setEditingFlag] = useState<FlagInfoModel | null>(null)
  const [deletingFlag, setDeletingFlag] = useState<FlagInfoModel | null>(null)
  const [pending, setPending] = useState(false)
  const dynamicAttachment = challenge.type === ChallengeType.DynamicAttachment
  const flags = useMemo(
    () => [...challenge.flags].sort((left, right) => (left.orderIndex ?? 0) - (right.orderIndex ?? 0)),
    [challenge.flags]
  )

  if (challenge.type === ChallengeType.DynamicContainer) {
    return (
      <div className={styles.noticePanel}>
        <strong>动态容器使用 Flag 模板</strong>
        <p>平台按队伍生成唯一 Flag，判题值来自运行环境中的动态 Flag 模板；后端不会在详情接口中返回普通 Flag。</p>
      </div>
    )
  }
  const nextOrderIndex = Math.max(0, ...flags.map((item) => item.orderIndex ?? 0)) + 1

  const saveFlag = async (draft: FlagEditorDraft, file: File | null) => {
    if (!challenge.id) throw new Error('题目编号缺失。')
    setPending(true)
    try {
      if (editingFlag?.id) {
        await gameAdminApi.updateFlag(
          gameId,
          challenge.id,
          editingFlag.id,
          flagCreatePayload({ ...draft, attachmentType: FileType.None })
        )
      } else {
        const uploaded = file ? await gameAdminApi.uploadAsset(file) : null
        await gameAdminApi.addFlags(gameId, challenge.id, [flagCreatePayload(draft, uploaded?.hash)])
      }
      await onChanged()
      onFeedback('success', editingFlag ? 'Flag 配置已保存。' : 'Flag 已添加。')
    } finally {
      setPending(false)
    }
  }

  const removeFlag = async () => {
    if (!challenge.id || !deletingFlag?.id) return false
    setPending(true)
    try {
      const result = await gameAdminApi.removeFlag(gameId, challenge.id, deletingFlag.id)
      if (result !== TaskStatus.Success) {
        throw new Error(result === TaskStatus.Failed ? '该 Flag 已产生解题记录，服务端拒绝删除。' : `删除未完成：${result}`)
      }
      await onChanged()
      onFeedback('success', `${flagLabel(deletingFlag)} 已删除。`)
      return true
    } finally {
      setPending(false)
    }
  }

  return (
    <div className={styles.panelStack}>
      <div className={styles.panelToolbar}>
        <div>
          <strong>{flags.length} 条判题规则</strong>
          <span>{dynamicAttachment ? '每条 Flag 对应一份队伍附件。' : '支持多阶段 Flag、固定分值和独立尝试次数。'}</span>
        </div>
        <ActionButton
          disabled={pending}
          icon={<Plus size={16} />}
          onClick={() => {
            setEditingFlag(null)
            setDialogOpen(true)
          }}
          type="button"
        >
          添加 Flag
        </ActionButton>
      </div>

      {flags.length ? (
        <div className={styles.flagList}>
          {flags.map((item) => (
            <article className={styles.flagRow} key={item.id ?? `${item.orderIndex}-${item.flag}`}>
              <div className={styles.flagIdentity}>
                <span className={styles.flagIcon}><Flag size={15} /></span>
                <div>
                  <strong>{flagLabel(item)}</strong>
                  <code title={item.flag}>{item.flag || '未配置判定值'}</code>
                </div>
              </div>
              <div className={styles.flagMeta}>
                <StatusBadge tone="info">{answerTypeLabel(item.answerType)}</StatusBadge>
                <span>{item.scoreMode === FlagScoreMode.FixedScore ? `${item.fixedScore ?? 0} 分` : '继承动态分值'}</span>
                <span>{item.maxAttempts ? `${item.maxAttempts} 次` : '不限尝试'}</span>
                {item.attachment?.url ? (
                  <a href={item.attachment.url} rel="noreferrer" target="_blank">
                    附件 <ExternalLink size={13} />
                  </a>
                ) : null}
              </div>
              <div className={styles.rowActions}>
                <button
                  aria-label={`编辑 ${flagLabel(item)}`}
                  disabled={pending}
                  onClick={() => {
                    setEditingFlag(item)
                    setDialogOpen(true)
                  }}
                  type="button"
                >
                  <Edit3 size={15} />
                </button>
                <button
                  aria-label={`删除 ${flagLabel(item)}`}
                  disabled={pending}
                  onClick={() => setDeletingFlag(item)}
                  type="button"
                >
                  <Trash2 size={15} />
                </button>
              </div>
            </article>
          ))}
        </div>
      ) : (
        <div className={styles.emptyPanel}>当前没有 Flag。除动态容器题外，题目发布前应至少配置一条判题规则。</div>
      )}

      <FlagEditorDialog
        dynamicAttachment={dynamicAttachment}
        flag={editingFlag}
        nextOrderIndex={nextOrderIndex}
        onClose={() => setDialogOpen(false)}
        onSubmit={saveFlag}
        open={dialogOpen}
      />
      <VNextConfirmDialog
        description="已有解题记录的 Flag 会被服务端拒绝删除。"
        message={deletingFlag ? `将删除“${flagLabel(deletingFlag)}”及其动态附件。` : ''}
        onClose={() => setDeletingFlag(null)}
        onConfirm={removeFlag}
        open={Boolean(deletingFlag)}
        title="确认删除 Flag"
      />
    </div>
  )
}
