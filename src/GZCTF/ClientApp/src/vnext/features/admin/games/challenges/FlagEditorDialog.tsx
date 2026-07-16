import { FormEvent, useEffect, useId, useState } from 'react'
import {
  AnswerType,
  FileType,
  FlagInfoModel,
  FlagScoreMode,
} from '@Api'
import { FileField, SelectField, TextAreaField, TextField } from '../../../../shared/FormControls'
import { ActionButton, InlineFeedback, VNextDialog } from '../../../../shared/Interaction'
import { errorMessage } from '../../../../shared/errors'
import {
  emptyFlagEditorDraft,
  flagEditorDraft,
  type FlagEditorDraft,
  validateFlagEditorDraft,
} from './flagEditorModel'
import styles from './FlagEditorDialog.module.css'

export function FlagEditorDialog({
  dynamicAttachment,
  flag,
  nextOrderIndex,
  onClose,
  onSubmit,
  open,
}: {
  dynamicAttachment: boolean
  flag: FlagInfoModel | null
  nextOrderIndex: number
  onClose: () => void
  onSubmit: (draft: FlagEditorDraft, file: File | null) => Promise<void>
  open: boolean
}) {
  const formId = useId()
  const [draft, setDraft] = useState<FlagEditorDraft>(() => emptyFlagEditorDraft(nextOrderIndex))
  const [file, setFile] = useState<File | null>(null)
  const [issues, setIssues] = useState<string[]>([])
  const [submitting, setSubmitting] = useState(false)
  const editing = Boolean(flag?.id)

  useEffect(() => {
    if (!open) return
    const next = flag ? flagEditorDraft(flag) : emptyFlagEditorDraft(nextOrderIndex)
    if (!flag && dynamicAttachment) next.attachmentType = FileType.Local
    setDraft(next)
    setFile(null)
    setIssues([])
    setSubmitting(false)
  }, [dynamicAttachment, flag, nextOrderIndex, open])

  const update = <Key extends keyof FlagEditorDraft>(field: Key, value: FlagEditorDraft[Key]) => {
    setDraft((current) => ({ ...current, [field]: value }))
  }

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    const validation = validateFlagEditorDraft(draft, {
      dynamicAttachment,
      existingAttachment: editing,
      hasLocalFile: Boolean(file),
    })
    if (validation.length) {
      setIssues(validation)
      return
    }
    setSubmitting(true)
    setIssues([])
    try {
      await onSubmit(draft, file)
      onClose()
    } catch (requestError) {
      setIssues([errorMessage(requestError, editing ? 'Flag 保存失败。' : 'Flag 添加失败。')])
    } finally {
      setSubmitting(false)
    }
  }

  const attachmentUrl = flag?.attachment?.url

  return (
    <VNextDialog
      description="判定方式、分值和尝试次数均作用于当前 Flag；0 次表示不限制。"
      eyebrow="FLAG RULE"
      footer={
        <>
          <ActionButton disabled={submitting} onClick={onClose} type="button">取消</ActionButton>
          <ActionButton disabled={submitting} form={formId} tone="primary" type="submit">
            {submitting ? '正在保存' : editing ? '保存 Flag' : '添加 Flag'}
          </ActionButton>
        </>
      }
      onClose={() => {
        if (!submitting) onClose()
      }}
      open={open}
      title={editing ? '编辑 Flag' : '添加 Flag'}
      wide
    >
      <form className={styles.form} id={formId} onSubmit={(event) => void submit(event)}>
        {issues.length ? <InlineFeedback tone="danger">{issues.join(' ')}</InlineFeedback> : null}
        <div className={styles.grid}>
          <TextField
            label={draft.answerType === AnswerType.Flag ? 'Flag' : '判定值'}
            maxLength={127}
            onValueChange={(value) => update('flag', value)}
            required
            value={draft.flag}
          />
          <TextField
            hint="用于多阶段题目区分不同检查点。"
            label="显示名称"
            maxLength={64}
            onValueChange={(value) => update('customName', value)}
            value={draft.customName}
          />
          <TextField
            hint="0 表示自动排在现有 Flag 末尾。"
            label="显示顺序"
            min={0}
            onValueChange={(value) => update('orderIndex', Number(value))}
            type="number"
            value={draft.orderIndex}
          />
          <TextField
            hint="0 表示不限制。"
            label="最大尝试次数"
            min={0}
            onValueChange={(value) => update('maxAttempts', Number(value))}
            type="number"
            value={draft.maxAttempts}
          />
          <SelectField
            label="计分方式"
            onValueChange={(value) => update('scoreMode', value as FlagScoreMode)}
            value={draft.scoreMode}
          >
            <option value={FlagScoreMode.InheritDecay}>继承题目动态分值</option>
            <option value={FlagScoreMode.FixedScore}>固定分值</option>
          </SelectField>
          {draft.scoreMode === FlagScoreMode.FixedScore ? (
            <TextField
              label="固定分值"
              min={1}
              onValueChange={(value) => update('fixedScore', Number(value))}
              type="number"
              value={draft.fixedScore}
            />
          ) : null}
          <SelectField
            label="答案类型"
            onValueChange={(value) => update('answerType', value as AnswerType)}
            value={draft.answerType}
          >
            <option value={AnswerType.Flag}>Flag 文本</option>
            <option value={AnswerType.File}>文件内容</option>
            <option value={AnswerType.Custom}>自定义文本</option>
          </SelectField>
          {draft.answerType === AnswerType.File ? (
            <TextField
              hint="选手提交文件内容的 SHA256，用于判定答案。"
              label="答案文件 SHA256"
              maxLength={64}
              onValueChange={(value) => update('attachmentHash', value)}
              value={draft.attachmentHash}
            />
          ) : null}
        </div>
        <TextAreaField
          label="Flag 描述"
          maxLength={512}
          onValueChange={(value) => update('description', value)}
          rows={4}
          value={draft.description}
        />

        {dynamicAttachment ? (
          <section className={styles.attachmentSection}>
            <header>
              <strong>队伍附件</strong>
              <p>动态附件按 Flag 分发；这里的附件与“文件答案 SHA256”是两个独立概念。</p>
            </header>
            {editing ? (
              <div className={styles.immutableAttachment}>
                <span>{attachmentUrl ? '当前 Flag 已绑定附件' : '当前 Flag 未绑定附件'}</span>
                {attachmentUrl ? <a href={attachmentUrl} rel="noreferrer" target="_blank">检查附件</a> : null}
                <small>后端不支持原位替换 Flag 附件；无提交记录时可删除后重新添加。</small>
              </div>
            ) : (
              <div className={styles.grid}>
                <SelectField
                  label="附件来源"
                  onValueChange={(value) => update('attachmentType', value as FileType)}
                  value={draft.attachmentType}
                >
                  <option value={FileType.Local}>本地上传</option>
                  <option value={FileType.Remote}>外部链接</option>
                </SelectField>
                {draft.attachmentType === FileType.Local ? (
                  <FileField hint={file?.name} label="选择附件" onChange={setFile} />
                ) : (
                  <TextField
                    label="附件 URL"
                    onValueChange={(value) => update('remoteUrl', value)}
                    placeholder="https://..."
                    type="url"
                    value={draft.remoteUrl}
                  />
                )}
              </div>
            )}
          </section>
        ) : null}
      </form>
    </VNextDialog>
  )
}
