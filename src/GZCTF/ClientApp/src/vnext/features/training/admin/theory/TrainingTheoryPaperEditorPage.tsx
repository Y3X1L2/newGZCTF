import { ArrowDown, ArrowUp, Dice5, Plus, Save, Trash2 } from 'lucide-react'
import { useEffect, useMemo, useState } from 'react'
import { useParams } from 'react-router'
import api, {
  TheoryQuestionType,
  TrainingCourseChapterTheoryPaperEditModel,
  TrainingCourseTheoryPaperQuestionEditModel,
  TrainingCourseTheoryQuestionModel,
} from '@Api'
import { SelectField, TextAreaField, TextField, ToggleField } from '../../../../shared/FormControls'
import { ActionButton, InlineFeedback } from '../../../../shared/Interaction'
import { DataState, StatusPill } from '../../../../shared/Primitives'
import { errorMessage } from '../../../../shared/errors'
import { useVNextPageTitle } from '../../../../shared/useVNextPageTitle'
import { DEFAULT_THEORY_BANK, theoryQuestionTypeLabel } from '../../../theory/questionModel'
import { EditorActionBar, EditorSection, TrainingEditorShell } from '../TrainingEditorShell'
import styles from './TrainingTheoryPaperEditorPage.module.css'

function toPaperQuestion(
  question: TrainingCourseTheoryQuestionModel,
  score: number,
  order: number
): TrainingCourseTheoryPaperQuestionEditModel {
  return {
    sourceQuestionId: question.id,
    type: question.type,
    bankName: question.bankName,
    title: question.title,
    content: question.content,
    options: question.options,
    answerIndexes: question.answerIndexes,
    tags: question.tags,
    score,
    order,
  }
}

function normalizeOrder(questions: TrainingCourseTheoryPaperQuestionEditModel[]) {
  return questions.map((question, index) => ({ ...question, order: index + 1 }))
}

export function TrainingTheoryPaperEditorPage() {
  const { courseId, chapterId } = useParams()
  const courseNumber = Number(courseId)
  const chapterNumber = Number(chapterId)
  const validIds =
    Number.isInteger(courseNumber) && courseNumber > 0 && Number.isInteger(chapterNumber) && chapterNumber > 0
  const courseRequest = api.trainingCourseAdmin.useTrainingCourseAdminCourse(
    courseNumber,
    { revalidateOnFocus: false },
    validIds
  )
  const chapterRequest = api.trainingCourse.useTrainingCourseChapter(
    courseNumber,
    chapterNumber,
    { revalidateOnFocus: false },
    validIds
  )
  const paperRequest = api.trainingCourseAdmin.useTrainingCourseAdminChapterTheoryPaper(
    courseNumber,
    chapterNumber,
    { revalidateOnFocus: false },
    validIds
  )
  const questionsRequest = api.trainingCourseAdmin.useTrainingCourseAdminTheoryQuestions(
    courseNumber,
    { count: 5000 },
    { revalidateOnFocus: false },
    validIds
  )
  const course = courseRequest.data
  const chapter = chapterRequest.data
  const [paper, setPaper] = useState<TrainingCourseChapterTheoryPaperEditModel | null>(null)
  const [keyword, setKeyword] = useState('')
  const [typeFilter, setTypeFilter] = useState('All')
  const [bankFilter, setBankFilter] = useState('All')
  const [selectedIds, setSelectedIds] = useState<Set<number>>(new Set())
  const [uniformScore, setUniformScore] = useState(5)
  const [randomCount, setRandomCount] = useState(5)
  const [saving, setSaving] = useState(false)
  const [feedback, setFeedback] = useState<{ tone: 'success' | 'danger'; message: string } | null>(null)

  useVNextPageTitle(`${chapter?.title || '章节'}课后练习配置`)

  useEffect(() => {
    if (!paperRequest.data) return
    setPaper({
      title: paperRequest.data.title || `${chapter?.title || '章节'}课后练习`,
      description: paperRequest.data.description || '',
      passRate: paperRequest.data.passRate ?? 60,
      allowRetake: paperRequest.data.allowRetake ?? false,
      showCorrectAnswerAfterSubmit: paperRequest.data.showCorrectAnswerAfterSubmit ?? true,
      isPublished: paperRequest.data.isPublished ?? false,
      questions: normalizeOrder(paperRequest.data.questions ?? []),
    })
  }, [chapter?.title, paperRequest.data])

  const allQuestions = questionsRequest.data ?? []
  const banks = useMemo(
    () => [...new Set(allQuestions.map((question) => question.bankName || DEFAULT_THEORY_BANK))].sort(),
    [allQuestions]
  )
  const filteredQuestions = useMemo(() => {
    const search = keyword.trim().toLocaleLowerCase('zh-CN')
    return allQuestions.filter((question) => {
      if (typeFilter !== 'All' && question.type !== typeFilter) return false
      if (bankFilter !== 'All' && (question.bankName || DEFAULT_THEORY_BANK) !== bankFilter) return false
      if (!search) return true
      return [question.title, question.content, question.bankName]
        .filter(Boolean)
        .some((value) => String(value).toLocaleLowerCase('zh-CN').includes(search))
    })
  }, [allQuestions, bankFilter, keyword, typeFilter])
  const selectedQuestions = paper?.questions ?? []
  const selectedSourceIds = useMemo(
    () => new Set(selectedQuestions.map((question) => question.sourceQuestionId).filter(Boolean)),
    [selectedQuestions]
  )
  const totalScore = selectedQuestions.reduce((total, question) => total + (question.score ?? 0), 0)

  const updatePaperField = <Key extends keyof TrainingCourseChapterTheoryPaperEditModel>(
    field: Key,
    value: TrainingCourseChapterTheoryPaperEditModel[Key]
  ) => {
    setPaper((current) => (current ? { ...current, [field]: value } : current))
  }

  const setQuestions = (questions: TrainingCourseTheoryPaperQuestionEditModel[]) => {
    setPaper((current) => (current ? { ...current, questions: normalizeOrder(questions) } : current))
  }

  const addQuestions = (questions: TrainingCourseTheoryQuestionModel[]) => {
    const additions = questions
      .filter((question) => question.id && !selectedSourceIds.has(question.id))
      .map((question, index) => toPaperQuestion(question, uniformScore, selectedQuestions.length + index + 1))
    setQuestions([...selectedQuestions, ...additions])
    setSelectedIds(new Set())
  }

  const addRandom = () => {
    const pool = filteredQuestions.filter((question) => question.id && !selectedSourceIds.has(question.id))
    const shuffled = [...pool]
    for (let index = shuffled.length - 1; index > 0; index -= 1) {
      const target = Math.floor(Math.random() * (index + 1))
      ;[shuffled[index], shuffled[target]] = [shuffled[target], shuffled[index]]
    }
    addQuestions(shuffled.slice(0, Math.max(1, randomCount)))
  }

  const moveQuestion = (index: number, direction: -1 | 1) => {
    const target = index + direction
    if (target < 0 || target >= selectedQuestions.length) return
    const next = [...selectedQuestions]
    ;[next[index], next[target]] = [next[target], next[index]]
    setQuestions(next)
  }

  const save = async (published: boolean) => {
    if (!paper || saving) return
    if (!paper.title.trim()) {
      setFeedback({ tone: 'danger', message: '请输入试卷标题。' })
      return
    }
    if (!selectedQuestions.length) {
      setFeedback({ tone: 'danger', message: '试卷至少需要一道题目。' })
      return
    }
    setSaving(true)
    setFeedback(null)
    try {
      const response = await api.trainingCourseAdmin.trainingCourseAdminSaveChapterTheoryPaper(
        courseNumber,
        chapterNumber,
        {
          ...paper,
          title: paper.title.trim(),
          description: paper.description?.trim() || '',
          passRate: Math.min(100, Math.max(1, Number(paper.passRate) || 60)),
          isPublished: published,
          questions: normalizeOrder(selectedQuestions),
        }
      )
      await Promise.all([paperRequest.mutate(response.data, { revalidate: false }), courseRequest.mutate()])
      setFeedback({ tone: 'success', message: published ? '课后练习已保存并发放。' : '课后练习草稿已保存。' })
    } catch (requestError) {
      setFeedback({ tone: 'danger', message: errorMessage(requestError, '课后练习保存失败。') })
    } finally {
      setSaving(false)
    }
  }

  if (!validIds) return <DataState description="课程或章节编号不是有效数字。" title="参数错误" />
  if (!course || !chapter || !paper || !questionsRequest.data) {
    const failed = courseRequest.error || chapterRequest.error || paperRequest.error || questionsRequest.error
    return failed ? (
      <DataState description="课程、章节、试卷或题库数据读取失败。" title="无法打开课后练习配置" />
    ) : (
      <DataState description="正在读取课程题库和章节试卷。" loading title="试卷编辑器加载中" />
    )
  }
  if (!course.canEdit)
    return <DataState description="只有课程教师和管理员可以配置课后练习。" title="没有试卷编辑权限" />

  return (
    <TrainingEditorShell
      backLabel="返回课后练习"
      backTo={`/training/courses/${courseNumber}?tab=homework`}
      description="从当前课程题库选择题目、统一设置分值，并将一套试卷发放到当前章节。"
      eyebrow="CHAPTER ASSESSMENT"
      meta={
        <>
          <StatusPill tone={paper.isPublished ? 'success' : 'warning'}>
            {paper.isPublished ? '已发放' : '草稿'}
          </StatusPill>
          <StatusPill>
            {selectedQuestions.length} 题 · {totalScore} 分
          </StatusPill>
        </>
      }
      title={`${chapter.title || '章节'} · 课后练习`}
    >
      {feedback ? <InlineFeedback tone={feedback.tone}>{feedback.message}</InlineFeedback> : null}
      <div className={styles.workspace}>
        <main className={styles.paperColumn}>
          <EditorSection description="保存草稿不会对学生开放，发放后学生可从章节末尾进入。" title="试卷配置">
            <div className={styles.formGrid}>
              <TextField
                label="试卷标题"
                onValueChange={(value) => updatePaperField('title', value)}
                required
                value={paper.title}
              />
              <TextField
                label="通过线 (%)"
                max={100}
                min={1}
                onValueChange={(value) => updatePaperField('passRate', Number(value))}
                type="number"
                value={paper.passRate ?? 60}
              />
              <TextAreaField
                label="试卷说明"
                onValueChange={(value) => updatePaperField('description', value)}
                rows={3}
                value={paper.description}
              />
              <div className={styles.toggles}>
                <ToggleField
                  checked={paper.allowRetake ?? false}
                  description="提交后允许生成新的答卷再次作答。"
                  label="允许重做"
                  onChange={(checked) =>
                    setPaper((current) => (current ? { ...current, allowRetake: checked } : current))
                  }
                />
                <ToggleField
                  checked={paper.showCorrectAnswerAfterSubmit ?? true}
                  description="最终提交后向学生展示正确答案。"
                  label="提交后显示答案"
                  onChange={(checked) =>
                    setPaper((current) => (current ? { ...current, showCorrectAnswerAfterSubmit: checked } : current))
                  }
                />
              </div>
            </div>
          </EditorSection>
          <section className={styles.selectedSection}>
            <header>
              <div>
                <span>PAPER STRUCTURE</span>
                <h2>已选题目</h2>
              </div>
              <div>
                <TextField
                  label="统一分值"
                  min={1}
                  onValueChange={(value) => setUniformScore(Math.max(1, Number(value) || 1))}
                  type="number"
                  value={uniformScore}
                />
                <ActionButton
                  onClick={() =>
                    setQuestions(selectedQuestions.map((question) => ({ ...question, score: uniformScore })))
                  }
                  type="button"
                >
                  应用全部
                </ActionButton>
              </div>
            </header>
            {selectedQuestions.length ? (
              <div className={styles.selectedList}>
                {selectedQuestions.map((question, index) => (
                  <article key={`${question.sourceQuestionId}-${index}`}>
                    <span>{String(index + 1).padStart(2, '0')}</span>
                    <div>
                      <strong>{question.title}</strong>
                      <small>
                        {theoryQuestionTypeLabel(question.type)} · {question.bankName || DEFAULT_THEORY_BANK}
                      </small>
                    </div>
                    <TextField
                      aria-label="题目分值"
                      label="分值"
                      min={1}
                      onValueChange={(value) =>
                        setQuestions(
                          selectedQuestions.map((item, itemIndex) =>
                            itemIndex === index ? { ...item, score: Math.max(1, Number(value) || 1) } : item
                          )
                        )
                      }
                      type="number"
                      value={question.score ?? uniformScore}
                    />
                    <div className={styles.itemActions}>
                      <button
                        aria-label="上移"
                        disabled={index === 0}
                        onClick={() => moveQuestion(index, -1)}
                        type="button"
                      >
                        <ArrowUp size={15} />
                      </button>
                      <button
                        aria-label="下移"
                        disabled={index === selectedQuestions.length - 1}
                        onClick={() => moveQuestion(index, 1)}
                        type="button"
                      >
                        <ArrowDown size={15} />
                      </button>
                      <button
                        aria-label="移除"
                        onClick={() => setQuestions(selectedQuestions.filter((_, itemIndex) => itemIndex !== index))}
                        type="button"
                      >
                        <Trash2 size={15} />
                      </button>
                    </div>
                  </article>
                ))}
              </div>
            ) : (
              <DataState description="从右侧课程题库选择题目，或按筛选条件随机抽取。" title="试卷尚无题目" />
            )}
          </section>
        </main>

        <aside className={styles.bankColumn}>
          <header>
            <span>COURSE QUESTION BANK</span>
            <h2>课程题库</h2>
          </header>
          <div className={styles.bankFilters}>
            <TextField label="搜索" onValueChange={setKeyword} value={keyword} />
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
          <div className={styles.randomBar}>
            <TextField
              label="随机数量"
              min={1}
              onValueChange={(value) => setRandomCount(Math.max(1, Number(value) || 1))}
              type="number"
              value={randomCount}
            />
            <ActionButton icon={<Dice5 size={16} />} onClick={addRandom} type="button">
              随机抽取
            </ActionButton>
          </div>
          <div className={styles.bankList}>
            {filteredQuestions.map((question) => {
              const id = question.id ?? 0
              const added = selectedSourceIds.has(id)
              return (
                <label key={id} data-added={added}>
                  <input
                    checked={selectedIds.has(id)}
                    disabled={added}
                    onChange={(event) => {
                      const checked = event.currentTarget.checked
                      setSelectedIds((current) => {
                        const next = new Set(current)
                        if (checked) next.add(id)
                        else next.delete(id)
                        return next
                      })
                    }}
                    type="checkbox"
                  />
                  <span>
                    <strong>{question.title}</strong>
                    <small>
                      {theoryQuestionTypeLabel(question.type)} · {question.bankName || DEFAULT_THEORY_BANK}
                    </small>
                  </span>
                  <StatusPill tone={added ? 'success' : 'neutral'}>{added ? '已加入' : '可选择'}</StatusPill>
                </label>
              )
            })}
          </div>
          <ActionButton
            disabled={!selectedIds.size}
            icon={<Plus size={16} />}
            onClick={() => addQuestions(allQuestions.filter((question) => question.id && selectedIds.has(question.id)))}
            tone="primary"
            type="button"
          >
            加入选中题目
          </ActionButton>
        </aside>
      </div>

      <EditorActionBar
        status={`当前 ${selectedQuestions.length} 题，总分 ${totalScore}，通过线 ${paper.passRate ?? 60}%`}
      >
        <ActionButton disabled={saving} icon={<Save size={16} />} onClick={() => void save(false)} type="button">
          保存草稿
        </ActionButton>
        <ActionButton
          disabled={saving}
          icon={<Save size={16} />}
          onClick={() => void save(true)}
          tone="primary"
          type="button"
        >
          保存并发放
        </ActionButton>
      </EditorActionBar>
    </TrainingEditorShell>
  )
}
