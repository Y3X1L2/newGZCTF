import { Plus, X } from 'lucide-react'
import { useEffect, useId, useState } from 'react'
import { TheoryQuestionEditModel, TheoryQuestionType } from '@Api'
import { SelectField, TextAreaField, TextField } from '../../../shared/FormControls'
import { ActionButton, InlineFeedback, VNextDialog } from '../../../shared/Interaction'
import {
  DEFAULT_THEORY_BANK,
  emptyTheoryQuestion,
  normalizeTheoryQuestion,
  theoryQuestionTypeLabel,
  validateTheoryQuestion,
} from '../questionModel'
import styles from './TheoryQuestionDialog.module.css'

export function TheoryQuestionDialog({
  open,
  question,
  loading,
  onClose,
  onSave,
}: {
  open: boolean
  question: TheoryQuestionEditModel | null
  loading: boolean
  onClose: () => void
  onSave: (question: TheoryQuestionEditModel) => Promise<void>
}) {
  const answerGroup = useId()
  const [draft, setDraft] = useState<TheoryQuestionEditModel>(emptyTheoryQuestion)
  const [tagsText, setTagsText] = useState('')
  const [feedback, setFeedback] = useState<string | null>(null)

  const updateDraftField = <Key extends keyof TheoryQuestionEditModel>(
    field: Key,
    value: TheoryQuestionEditModel[Key]
  ) => {
    setDraft((current) => ({ ...current, [field]: value }))
  }

  useEffect(() => {
    if (!open) return
    const normalized = normalizeTheoryQuestion(question ?? emptyTheoryQuestion())
    setDraft(normalized)
    setTagsText((normalized.tags ?? []).join('、'))
    setFeedback(null)
  }, [open, question])

  const options = draft.options ?? []
  const answers = new Set(draft.answerIndexes ?? [])
  const multiple = draft.type === TheoryQuestionType.MultipleChoice
  const trueFalse = draft.type === TheoryQuestionType.TrueFalse

  const setType = (type: TheoryQuestionType) => {
    setDraft(
      normalizeTheoryQuestion({
        ...draft,
        type,
        options: type === TheoryQuestionType.TrueFalse ? ['正确', '错误'] : options,
        answerIndexes: [0],
      })
    )
    setFeedback(null)
  }

  const toggleAnswer = (index: number, checked: boolean) => {
    if (!multiple) setDraft((current) => ({ ...current, answerIndexes: [index] }))
    else
      setDraft((current) => ({
        ...current,
        answerIndexes: checked
          ? [...(current.answerIndexes ?? []), index]
          : (current.answerIndexes ?? []).filter((value) => value !== index),
      }))
  }

  const save = async () => {
    const payload = normalizeTheoryQuestion({
      ...draft,
      tags: tagsText
        .split(/[，,、]/)
        .map((tag) => tag.trim())
        .filter(Boolean),
    })
    const issues = validateTheoryQuestion(payload)
    if (issues.length) {
      setFeedback(issues.join(' '))
      return
    }
    setFeedback(null)
    await onSave(payload)
  }

  return (
    <VNextDialog
      description="多选题必须完全匹配正确答案才得分。"
      eyebrow="THEORY QUESTION"
      footer={
        <>
          <ActionButton disabled={loading} onClick={onClose} type="button">取消</ActionButton>
          <ActionButton disabled={loading} onClick={() => void save()} tone="primary" type="button">
            {loading ? '正在保存' : '保存题目'}
          </ActionButton>
        </>
      }
      onClose={() => { if (!loading) onClose() }}
      open={open}
      title={question ? '编辑理论题目' : '新建理论题目'}
      wide
    >
      <div className={styles.questionForm}>
        {feedback ? <InlineFeedback tone="danger">{feedback}</InlineFeedback> : null}
        <div className={styles.formColumns}>
          <TextField
            label="题库名称"
            maxLength={128}
            onValueChange={(value) => updateDraftField('bankName', value)}
            value={draft.bankName ?? DEFAULT_THEORY_BANK}
          />
          <SelectField label="题型" onValueChange={(value) => setType(value as TheoryQuestionType)} value={draft.type}>
            {Object.values(TheoryQuestionType).map((type) => <option key={type} value={type}>{theoryQuestionTypeLabel(type)}</option>)}
          </SelectField>
        </div>
        <TextField label="题干" onValueChange={(value) => updateDraftField('title', value)} required value={draft.title} />
        <TextAreaField label="说明或解析" onValueChange={(value) => updateDraftField('content', value)} rows={4} value={draft.content} />
        <TextField hint="使用逗号或顿号分隔。" label="标签" onValueChange={setTagsText} value={tagsText} />
        <div className={styles.optionEditor}>
          <header>
            <strong>选项与正确答案</strong>
            {!trueFalse ? (
              <ActionButton
                icon={<Plus size={15} />}
                onClick={() => setDraft((current) => ({
                  ...current,
                  options: [...(current.options ?? []), `选项 ${String.fromCharCode(65 + options.length)}`],
                }))}
                type="button"
              >
                添加选项
              </ActionButton>
            ) : null}
          </header>
          {options.map((option, index) => (
            <div key={index}>
              <input
                aria-label={`选项 ${index + 1} 为正确答案`}
                checked={answers.has(index)}
                name={multiple ? undefined : answerGroup}
                onChange={(event) => toggleAnswer(index, event.currentTarget.checked)}
                type={multiple ? 'checkbox' : 'radio'}
              />
              <input
                aria-label={`选项 ${index + 1}`}
                disabled={trueFalse}
                onChange={(event) => {
                  const value = event.currentTarget.value
                  setDraft((current) => ({
                    ...current,
                    options: (current.options ?? []).map((item, itemIndex) => (itemIndex === index ? value : item)),
                  }))
                }}
                value={option}
              />
              {!trueFalse && options.length > 2 ? (
                <button
                  aria-label={`删除选项 ${index + 1}`}
                  onClick={() => setDraft((current) => normalizeTheoryQuestion({
                    ...current,
                    options: (current.options ?? []).filter((_, itemIndex) => itemIndex !== index),
                    answerIndexes: (current.answerIndexes ?? [])
                      .filter((value) => value !== index)
                      .map((value) => (value > index ? value - 1 : value)),
                  }))}
                  title="删除选项"
                  type="button"
                >
                  <X size={16} />
                </button>
              ) : null}
            </div>
          ))}
        </div>
      </div>
    </VNextDialog>
  )
}
