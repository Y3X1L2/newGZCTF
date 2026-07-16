import { FileUp, Pencil, Plus, RefreshCw, Search, Trash2 } from 'lucide-react'
import { useMemo, useState } from 'react'
import api, {
  TheoryQuestionEditModel,
  TheoryQuestionType,
  TrainingCourseModel,
  TrainingCourseTheoryQuestionModel,
} from '@Api'
import { FileField, SelectField, TextAreaField, TextField } from '../../../../shared/FormControls'
import { ActionButton, InlineFeedback, VNextDialog } from '../../../../shared/Interaction'
import { DataState, StatusPill } from '../../../../shared/Primitives'
import { errorMessage } from '../../../../shared/errors'
import {
  DEFAULT_THEORY_BANK,
  normalizeTheoryQuestion,
  parseTheoryQuestionJson,
  theoryAnswerLabel,
  theoryQuestionTypeLabel,
} from '../../../theory/questionModel'
import { CourseManagementPanelHeader } from './CourseManagementPanelHeader'
import styles from './CourseTheoryBankPanel.module.css'
import { TheoryQuestionDialog } from '../../../theory/admin/TheoryQuestionDialog'

export function CourseTheoryBankPanel({ course }: { course: TrainingCourseModel }) {
  const courseId = course.id ?? 0
  const questionsRequest = api.trainingCourseAdmin.useTrainingCourseAdminTheoryQuestions(
    courseId,
    { count: 5000 },
    { revalidateOnFocus: false },
    Boolean(course.canEdit && courseId)
  )
  const questions = questionsRequest.data ?? []
  const [keyword, setKeyword] = useState('')
  const [typeFilter, setTypeFilter] = useState('All')
  const [bankFilter, setBankFilter] = useState('All')
  const [editorOpen, setEditorOpen] = useState(false)
  const [activeQuestion, setActiveQuestion] = useState<TrainingCourseTheoryQuestionModel | null>(null)
  const [deleteQuestion, setDeleteQuestion] = useState<TrainingCourseTheoryQuestionModel | null>(null)
  const [importOpen, setImportOpen] = useState(false)
  const [jsonText, setJsonText] = useState('')
  const [jsonBank, setJsonBank] = useState(DEFAULT_THEORY_BANK)
  const [previewQuestions, setPreviewQuestions] = useState<TheoryQuestionEditModel[]>([])
  const [saving, setSaving] = useState(false)
  const [feedback, setFeedback] = useState<{ tone: 'success' | 'danger'; message: string } | null>(null)

  const banks = useMemo(
    () => [...new Set(questions.map((question) => question.bankName || DEFAULT_THEORY_BANK))].sort(),
    [questions]
  )
  const visibleQuestions = useMemo(() => {
    const search = keyword.trim().toLocaleLowerCase('zh-CN')
    return questions.filter((question) => {
      if (typeFilter !== 'All' && question.type !== typeFilter) return false
      if (bankFilter !== 'All' && (question.bankName || DEFAULT_THEORY_BANK) !== bankFilter) return false
      if (!search) return true
      return [question.title, question.content, question.bankName, ...(question.tags ?? [])]
        .filter(Boolean)
        .some((value) => String(value).toLocaleLowerCase('zh-CN').includes(search))
    })
  }, [bankFilter, keyword, questions, typeFilter])

  const saveQuestion = async (draft: TheoryQuestionEditModel) => {
    setSaving(true)
    setFeedback(null)
    try {
      const payload = normalizeTheoryQuestion(draft)
      if (!payload.title) throw new Error('请输入题干。')
      if (activeQuestion?.id) {
        await api.trainingCourseAdmin.trainingCourseAdminUpdateTheoryQuestion(courseId, activeQuestion.id, payload)
      } else {
        await api.trainingCourseAdmin.trainingCourseAdminCreateTheoryQuestion(courseId, payload)
      }
      await questionsRequest.mutate()
      setEditorOpen(false)
      setActiveQuestion(null)
      setFeedback({ tone: 'success', message: activeQuestion ? '理论题目已更新。' : '理论题目已创建。' })
    } catch (requestError) {
      setFeedback({ tone: 'danger', message: errorMessage(requestError, '理论题目保存失败。') })
    } finally {
      setSaving(false)
    }
  }

  const removeQuestion = async () => {
    if (!deleteQuestion?.id || saving) return
    setSaving(true)
    try {
      await api.trainingCourseAdmin.trainingCourseAdminDeleteTheoryQuestion(courseId, deleteQuestion.id)
      await questionsRequest.mutate()
      setDeleteQuestion(null)
      setFeedback({ tone: 'success', message: '理论题目已删除。' })
    } catch (requestError) {
      setFeedback({ tone: 'danger', message: errorMessage(requestError, '题目可能已被试卷引用，删除失败。') })
    } finally {
      setSaving(false)
    }
  }

  const parseImport = () => {
    try {
      setPreviewQuestions(parseTheoryQuestionJson(jsonText, jsonBank.trim() || DEFAULT_THEORY_BANK))
      setFeedback(null)
    } catch (parseError) {
      setPreviewQuestions([])
      setFeedback({ tone: 'danger', message: errorMessage(parseError, 'JSON 解析失败。') })
    }
  }

  const importQuestions = async () => {
    if (!previewQuestions.length || saving) return
    setSaving(true)
    setFeedback(null)
    try {
      for (const question of previewQuestions) {
        await api.trainingCourseAdmin.trainingCourseAdminCreateTheoryQuestion(courseId, question)
      }
      await questionsRequest.mutate()
      setImportOpen(false)
      setJsonText('')
      setPreviewQuestions([])
      setFeedback({ tone: 'success', message: `已导入 ${previewQuestions.length} 道理论题目。` })
    } catch (requestError) {
      setFeedback({ tone: 'danger', message: errorMessage(requestError, '题库导入过程中发生错误。') })
    } finally {
      setSaving(false)
    }
  }

  if (!course.canEdit)
    return <DataState description="只有课程教师和管理员可以维护课程理论题库。" title="无法管理理论题库" />

  return (
    <section className={styles.panel}>
      <CourseManagementPanelHeader
        actions={
          <>
            <ActionButton icon={<RefreshCw size={16} />} onClick={() => void questionsRequest.mutate()} type="button">
              刷新
            </ActionButton>
            <ActionButton icon={<FileUp size={16} />} onClick={() => setImportOpen(true)} type="button">
              JSON 导入
            </ActionButton>
            <ActionButton
              icon={<Plus size={16} />}
              onClick={() => {
                setActiveQuestion(null)
                setEditorOpen(true)
              }}
              type="button"
            >
              新建题目
            </ActionButton>
          </>
        }
        description="题库在当前课程内共享，可被多个章节课后练习引用。"
        eyebrow="COURSE THEORY BANK"
        title="课程理论题库"
      />
      {feedback ? <InlineFeedback tone={feedback.tone}>{feedback.message}</InlineFeedback> : null}
      <div className={styles.filters}>
        <label className={styles.searchBox}>
          <Search size={16} />
          <input
            onChange={(event) => setKeyword(event.currentTarget.value)}
            placeholder="搜索题干、说明或标签"
            value={keyword}
          />
        </label>
        <SelectField label="题型" onValueChange={setTypeFilter} value={typeFilter}>
          <option value="All">全部题型</option>
          {Object.values(TheoryQuestionType).map((type) => (
            <option key={type} value={type}>
              {theoryQuestionTypeLabel(type)}
            </option>
          ))}
        </SelectField>
        <SelectField label="题库" onValueChange={setBankFilter} value={bankFilter}>
          <option value="All">全部题库</option>
          {banks.map((bank) => (
            <option key={bank} value={bank}>
              {bank}
            </option>
          ))}
        </SelectField>
      </div>
      {!questionsRequest.data && !questionsRequest.error ? (
        <DataState description="正在读取课程理论题库。" loading title="题库加载中" />
      ) : questionsRequest.error ? (
        <DataState description="课程题库接口暂时不可用。" title="题库加载失败" />
      ) : visibleQuestions.length ? (
        <div className={styles.questionList}>
          {visibleQuestions.map((question) => (
            <article key={question.id}>
              <div className={styles.questionMeta}>
                <StatusPill tone="info">{theoryQuestionTypeLabel(question.type)}</StatusPill>
                <StatusPill>{question.bankName || DEFAULT_THEORY_BANK}</StatusPill>
              </div>
              <div className={styles.questionBody}>
                <strong>{question.title}</strong>
                <small>{question.content || '暂无题目说明。'}</small>
              </div>
              <span className={styles.answer}>{theoryAnswerLabel(question)}</span>
              <div className={styles.rowActions}>
                <button
                  aria-label="编辑理论题目"
                  onClick={() => {
                    setActiveQuestion(question)
                    setEditorOpen(true)
                  }}
                  title="编辑"
                  type="button"
                >
                  <Pencil size={16} />
                </button>
                <button
                  aria-label="删除理论题目"
                  onClick={() => setDeleteQuestion(question)}
                  title="删除"
                  type="button"
                >
                  <Trash2 size={16} />
                </button>
              </div>
            </article>
          ))}
        </div>
      ) : (
        <DataState description="当前筛选条件下没有理论题目。" title="暂无题目" />
      )}

      <TheoryQuestionDialog
        loading={saving}
        onClose={() => {
          setEditorOpen(false)
          setActiveQuestion(null)
        }}
        onSave={saveQuestion}
        open={editorOpen}
        question={activeQuestion}
      />

      <VNextDialog
        description="先解析并预览，确认题目数量和题型后再写入课程题库。"
        eyebrow="IMPORT THEORY JSON"
        footer={
          <>
            <ActionButton onClick={() => setImportOpen(false)} type="button">
              取消
            </ActionButton>
            {previewQuestions.length ? (
              <ActionButton disabled={saving} onClick={() => void importQuestions()} tone="primary" type="button">
                {saving ? '正在导入' : `确认导入 ${previewQuestions.length} 题`}
              </ActionButton>
            ) : (
              <ActionButton onClick={parseImport} tone="primary" type="button">
                解析预览
              </ActionButton>
            )}
          </>
        }
        onClose={() => setImportOpen(false)}
        open={importOpen}
        title="JSON 批量导入"
        wide
      >
        <div className={styles.importForm}>
          <TextField label="默认题库名称" onValueChange={setJsonBank} value={jsonBank} />
          <FileField
            accept="application/json,.json"
            label="选择 JSON 文件"
            onChange={(file) => {
              if (!file) return
              void file.text().then((text) => {
                setJsonText(text)
                setPreviewQuestions([])
              })
            }}
          />
          <TextAreaField
            label="题库 JSON"
            onValueChange={(value) => {
              setJsonText(value)
              setPreviewQuestions([])
            }}
            placeholder='{"questions":[{"type":"SingleChoice","title":"题干","options":["A","B"],"answer":"A"}]}'
            rows={14}
            value={jsonText}
          />
          {previewQuestions.length ? (
            <div className={styles.importPreview}>
              <strong>解析成功，共 {previewQuestions.length} 题</strong>
              {previewQuestions.slice(0, 6).map((question, index) => (
                <span key={`${question.title}-${index}`}>
                  {index + 1}. [{theoryQuestionTypeLabel(question.type)}] {question.title}
                </span>
              ))}
            </div>
          ) : null}
        </div>
      </VNextDialog>

      <VNextDialog
        description="已被章节试卷引用的题目通常不能删除。"
        eyebrow="DELETE THEORY QUESTION"
        footer={
          <>
            <ActionButton onClick={() => setDeleteQuestion(null)} type="button">
              取消
            </ActionButton>
            <ActionButton
              disabled={saving}
              icon={<Trash2 size={16} />}
              onClick={() => void removeQuestion()}
              tone="danger"
              type="button"
            >
              确认删除
            </ActionButton>
          </>
        }
        onClose={() => setDeleteQuestion(null)}
        open={Boolean(deleteQuestion)}
        title={`删除题目“${deleteQuestion?.title ?? ''}”`}
      >
        <InlineFeedback tone="danger">删除操作不可撤销。</InlineFeedback>
      </VNextDialog>
    </section>
  )
}
