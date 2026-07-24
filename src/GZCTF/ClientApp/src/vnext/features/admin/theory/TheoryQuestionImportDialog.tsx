import { useEffect, useMemo, useState } from 'react'
import { TheoryQuestionBankItemModel } from '@Api'
import { FileField, SelectField, TextAreaField, TextField } from '../../../shared/FormControls'
import { ActionButton, InlineFeedback, VNextDialog } from '../../../shared/Interaction'
import {
  buildTheoryQuestionImportPlan,
  DEFAULT_THEORY_BANK,
  inspectTheoryQuestionJson,
  TheoryImportDuplicateStrategy,
  theoryQuestionTypeLabel,
} from '../../theory/questionModel'
import { theoryAdminApi } from '../api'
import styles from './TheoryQuestionImportDialog.module.css'

const strategyLabels: Record<TheoryImportDuplicateStrategy, string> = {
  skip: '跳过重复题目',
  overwrite: '覆盖已有题目',
  copy: '全部新增为副本',
}

export function TheoryQuestionImportDialog({
  existing,
  onClose,
  onCompleted,
  onRefresh,
  open,
}: {
  existing: TheoryQuestionBankItemModel[]
  onClose: () => void
  onCompleted: (message: string) => Promise<void>
  onRefresh: () => Promise<unknown>
  open: boolean
}) {
  const [jsonText, setJsonText] = useState('')
  const [defaultBank, setDefaultBank] = useState(DEFAULT_THEORY_BANK)
  const [strategy, setStrategy] = useState<TheoryImportDuplicateStrategy>('skip')
  const [parsed, setParsed] = useState<ReturnType<typeof inspectTheoryQuestionJson> | null>(null)
  const [running, setRunning] = useState(false)
  const [completed, setCompleted] = useState(0)
  const [feedback, setFeedback] = useState<string | null>(null)

  useEffect(() => {
    if (!open) return
    setJsonText('')
    setDefaultBank(DEFAULT_THEORY_BANK)
    setStrategy('skip')
    setParsed(null)
    setRunning(false)
    setCompleted(0)
    setFeedback(null)
  }, [open])

  const plan = useMemo(
    () => parsed && !parsed.issues.length
      ? buildTheoryQuestionImportPlan(parsed.questions, existing, strategy)
      : null,
    [existing, parsed, strategy]
  )
  const actionable = plan?.items.filter((item) => item.action !== 'skip') ?? []

  const parse = () => {
    const inspection = inspectTheoryQuestionJson(jsonText, defaultBank.trim() || DEFAULT_THEORY_BANK)
    setParsed(inspection)
    setCompleted(0)
    setFeedback(inspection.issues.length ? `发现 ${inspection.issues.length} 项格式错误，修复后才能导入。` : null)
  }

  const runImport = async () => {
    if (!plan || !actionable.length || running) return
    setRunning(true)
    setCompleted(0)
    setFeedback(null)
    let applied = 0
    try {
      for (const item of actionable) {
        if (item.action === 'update' && item.existingId) {
          await theoryAdminApi.updateQuestion(item.existingId, item.question)
        } else {
          await theoryAdminApi.createQuestion(item.question)
        }
        applied += 1
        setCompleted(applied)
      }
      await onCompleted(`题库导入完成：新增 ${plan.createCount}，覆盖 ${plan.updateCount}，跳过 ${plan.skipCount}。`)
      onClose()
    } catch {
      await onRefresh()
      setFeedback(`导入在第 ${applied + 1} 个写入操作失败。已完成 ${applied}/${actionable.length}，成功记录不会自动回滚。`)
    } finally {
      setRunning(false)
    }
  }

  return (
    <VNextDialog
      description="先在浏览器中完整校验，再按所选重复策略逐题写入服务器。"
      eyebrow="IMPORT THEORY JSON"
      footer={
        <>
          <ActionButton disabled={running} onClick={onClose} type="button">取消</ActionButton>
          {!plan ? (
            <ActionButton disabled={running || !jsonText.trim()} onClick={parse} tone="primary" type="button">解析预览</ActionButton>
          ) : (
            <ActionButton disabled={running || !actionable.length} onClick={() => void runImport()} tone="primary" type="button">
              {running ? `正在写入 ${completed}/${actionable.length}` : `执行 ${actionable.length} 个写入`}
            </ActionButton>
          )}
        </>
      }
      onClose={() => { if (!running) onClose() }}
      open={open}
      title="JSON 批量导入"
      wide
    >
      <div className={styles.form}>
        <InlineFeedback tone="danger">当前后端没有批量事务。写入失败时已成功记录不会自动回滚，页面会报告准确进度。</InlineFeedback>
        {feedback ? <InlineFeedback tone="danger">{feedback}</InlineFeedback> : null}
        <div className={styles.fieldGrid}>
          <TextField label="默认题库名称" maxLength={128} onValueChange={(value) => { setDefaultBank(value); setParsed(null) }} value={defaultBank} />
          <SelectField label="重复题目策略" onValueChange={(value) => setStrategy(value as TheoryImportDuplicateStrategy)} value={strategy}>
            {Object.entries(strategyLabels).map(([value, label]) => <option key={value} value={value}>{label}</option>)}
          </SelectField>
        </div>
        <FileField
          accept="application/json,.json"
          hint="最大 2 MB"
          label="选择 JSON 文件"
          onChange={(file) => {
            if (!file) return
            if (file.size > 2 * 1024 * 1024) {
              setFeedback('JSON 文件不能超过 2 MB。')
              return
            }
            void file.text().then((text) => {
              setJsonText(text)
              setParsed(null)
              setFeedback(null)
            })
          }}
        />
        <TextAreaField
          label="题库 JSON"
          onValueChange={(value) => { setJsonText(value); setParsed(null); setFeedback(null) }}
          placeholder='{"questions":[{"type":"SingleChoice","bankName":"Web","title":"题干","options":["A","B"],"answerIndexes":[0],"tags":["HTTP"]}]}'
          rows={14}
          value={jsonText}
        />
        {parsed ? (
          <section className={styles.preview}>
            <header>
              <strong>解析结果</strong>
              <span>有效 {parsed.questions.length} · 错误 {parsed.issues.length}</span>
            </header>
            {plan ? <p>新增 {plan.createCount}，覆盖 {plan.updateCount}，跳过 {plan.skipCount}。</p> : null}
            {parsed.issues.slice(0, 20).map((issue, index) => (
              <div data-tone="danger" key={`${issue.index}-${index}`}>
                <span>{issue.index === null ? '文件' : `第 ${issue.index + 1} 题`}</span>
                <strong>{issue.message}</strong>
              </div>
            ))}
            {plan?.items.slice(0, 20).map((item) => (
              <div key={`${item.index}-${item.question.title}`}>
                <span>{item.index + 1}. {theoryQuestionTypeLabel(item.question.type)}</span>
                <strong>{item.question.title}</strong>
                <em data-action={item.action}>{item.action === 'create' ? '新增' : item.action === 'update' ? '覆盖' : '跳过'}</em>
              </div>
            ))}
            {(parsed.issues.length > 20 || (plan?.items.length ?? 0) > 20) ? <small>仅展示前 20 条，统计包含全部记录。</small> : null}
          </section>
        ) : null}
      </div>
    </VNextDialog>
  )
}
