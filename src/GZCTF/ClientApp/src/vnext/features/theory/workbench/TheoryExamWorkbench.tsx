import {
  AlertTriangle,
  Bookmark,
  Check,
  ChevronLeft,
  ChevronRight,
  Clock3,
  ListChecks,
  Save,
  Send,
  X,
} from 'lucide-react'
import { ReactNode, useEffect, useMemo, useRef, useState } from 'react'
import { TheoryAnswerSheetEditModel, TheoryPlayerPaperModel, TheoryPlayerQuestionModel, TheoryQuestionType } from '@Api'
import { ActionButton, InlineFeedback, VNextDialog } from '../../../shared/Interaction'
import { MarkdownContent } from '../../../shared/MarkdownContent'
import { StatusPill } from '../../../shared/Primitives'
import styles from './TheoryExamWorkbench.module.css'
import { useTheoryExamSession } from './useTheoryExamSession'

function questionTypeLabel(type?: TheoryQuestionType) {
  if (type === TheoryQuestionType.MultipleChoice) return '多选题'
  if (type === TheoryQuestionType.TrueFalse) return '判断题'
  return '单选题'
}

function questionTypeShort(type?: TheoryQuestionType) {
  if (type === TheoryQuestionType.MultipleChoice) return '多选'
  if (type === TheoryQuestionType.TrueFalse) return '判断'
  return '单选'
}

function formatTimestamp(value?: number | null) {
  if (!value) return '--'
  return new Intl.DateTimeFormat('zh-CN', {
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
    hour12: false,
  }).format(value)
}

function optionKey(index: number) {
  return index < 26 ? String.fromCharCode(65 + index) : String(index + 1)
}

function TheoryQuestion({
  question,
  selectedIndexes,
  disabled,
  direction,
  onChange,
  review,
  onToggleReview,
}: {
  question: TheoryPlayerQuestionModel
  selectedIndexes: number[]
  disabled: boolean
  direction: 'forward' | 'backward'
  onChange: (selectedIndexes: number[]) => void
  review: boolean
  onToggleReview: () => void
}) {
  const multiple = question.type === TheoryQuestionType.MultipleChoice
  const questionId = question.id ?? 0
  const selected = new Set(selectedIndexes)

  const changeOption = (index: number, checked: boolean) => {
    if (disabled) return
    if (!multiple) {
      onChange([index])
      return
    }
    const next = new Set(selected)
    if (checked) next.add(index)
    else next.delete(index)
    onChange([...next])
  }

  return (
    <article className={`${styles.question} ${styles[`question_${direction}`]}`} key={questionId}>
      <header className={styles.questionHeader}>
        <div className={styles.questionMeta}>
          <StatusPill tone="info">{questionTypeLabel(question.type)}</StatusPill>
          <span>{question.score ?? 0} 分</span>
        </div>
        {!disabled ? (
          <button className={review ? styles.reviewActive : styles.reviewButton} onClick={onToggleReview} type="button">
            <Bookmark fill={review ? 'currentColor' : 'none'} size={16} />
            {review ? '已标记待检查' : '标记待检查'}
          </button>
        ) : null}
      </header>

      <h2>{question.title || `题目 ${questionId}`}</h2>
      {question.content ? <MarkdownContent source={question.content} /> : null}

      <fieldset className={styles.options} disabled={disabled}>
        <legend className={styles.srOnly}>{questionTypeLabel(question.type)}选项</legend>
        {(question.options ?? []).map((option, index) => {
          const checked = selected.has(index)
          const inputId = `theory-${questionId}-${index}`
          return (
            <label className={checked ? styles.optionSelected : styles.option} htmlFor={inputId} key={inputId}>
              <input
                checked={checked}
                id={inputId}
                name={`theory-${questionId}`}
                onChange={(event) => changeOption(index, event.currentTarget.checked)}
                type={multiple ? 'checkbox' : 'radio'}
                value={index}
              />
              <span className={styles.optionKey}>{optionKey(index)}</span>
              <span className={styles.optionText}>{option}</span>
              {checked ? <Check aria-hidden="true" size={18} /> : null}
            </label>
          )
        })}
      </fieldset>
    </article>
  )
}

function QuestionIndex({
  questions,
  answers,
  activeIndex,
  reviewQuestionIds,
  onSelect,
}: {
  questions: TheoryPlayerQuestionModel[]
  answers: Record<number, number[]>
  activeIndex: number
  reviewQuestionIds: Set<number>
  onSelect: (index: number) => void
}) {
  return (
    <div className={styles.indexContent}>
      <div className={styles.indexGrid}>
        {questions.map((question, index) => {
          const questionId = question.id ?? 0
          const answered = (answers[questionId]?.length ?? 0) > 0
          const review = reviewQuestionIds.has(questionId)
          const active = activeIndex === index
          const className = active
            ? styles.indexItemActive
            : review
              ? styles.indexItemReview
              : answered
                ? styles.indexItemAnswered
                : styles.indexItem
          return (
            <button
              aria-label={`第 ${index + 1} 题，${questionTypeLabel(question.type)}，${answered ? '已作答' : '未作答'}${review ? '，待检查' : ''}`}
              className={className}
              key={questionId}
              onClick={() => onSelect(index)}
              type="button"
            >
              <strong>{index + 1}</strong>
              <small>{questionTypeShort(question.type)}</small>
              {answered ? <Check aria-hidden="true" size={13} /> : null}
              {review ? <Bookmark aria-hidden="true" fill="currentColor" size={12} /> : null}
            </button>
          )
        })}
      </div>
      <div className={styles.indexLegend}>
        <span>
          <i className={styles.legendCurrent} />
          当前
        </span>
        <span>
          <i className={styles.legendAnswered} />
          已答
        </span>
        <span>
          <i className={styles.legendReview} />
          待检查
        </span>
      </div>
    </div>
  )
}

function MobileQuestionIndex({ open, onClose, children }: { open: boolean; onClose: () => void; children: ReactNode }) {
  const ref = useRef<HTMLDialogElement>(null)

  useEffect(() => {
    const dialog = ref.current
    if (!dialog) return
    if (open && !dialog.open) dialog.showModal()
    if (!open && dialog.open) dialog.close()
  }, [open])

  return (
    <dialog
      className={styles.indexDialog}
      onCancel={(event) => {
        event.preventDefault()
        onClose()
      }}
      onClick={(event) => {
        if (event.currentTarget === event.target) onClose()
      }}
      ref={ref}
    >
      <div className={styles.indexDialogPanel}>
        <header>
          <div>
            <span>QUESTION INDEX</span>
            <h2>题目索引</h2>
          </div>
          <button aria-label="关闭题目索引" onClick={onClose} type="button">
            <X size={19} />
          </button>
        </header>
        <div>{children}</div>
      </div>
    </dialog>
  )
}

export function TheoryExamWorkbench({
  initialPaper,
  saveDraft,
  submit,
  deadline,
  onSubmitted,
}: {
  initialPaper: TheoryPlayerPaperModel
  saveDraft: (data: TheoryAnswerSheetEditModel) => Promise<TheoryPlayerPaperModel>
  submit: (data: TheoryAnswerSheetEditModel) => Promise<TheoryPlayerPaperModel>
  deadline?: number | null
  onSubmitted?: (paper: TheoryPlayerPaperModel) => void
}) {
  const session = useTheoryExamSession({ initialPaper, saveDraft, submit, onSubmitted })
  const questions = useMemo(
    () =>
      [...(session.paper.questions ?? [])]
        .filter((question) => question.id !== undefined)
        .sort((a, b) => (a.order ?? 0) - (b.order ?? 0)),
    [session.paper.questions]
  )
  const [currentIndex, setCurrentIndex] = useState(0)
  const [direction, setDirection] = useState<'forward' | 'backward'>('forward')
  const [confirmOpen, setConfirmOpen] = useState(false)
  const [indexOpen, setIndexOpen] = useState(false)

  useEffect(() => {
    if (!questions.length) return
    setCurrentIndex((current) => Math.min(current, questions.length - 1))
  }, [questions.length])

  const answeredCount = questions.filter((question) => (session.answers[question.id ?? 0]?.length ?? 0) > 0).length
  const unansweredCount = Math.max(0, questions.length - answeredCount)
  const progress = questions.length ? Math.round((answeredCount / questions.length) * 100) : 0
  const currentQuestion = questions[currentIndex]
  const currentQuestionId = currentQuestion?.id ?? 0

  const goToQuestion = (nextIndex: number) => {
    const bounded = Math.min(Math.max(nextIndex, 0), questions.length - 1)
    if (bounded === currentIndex) return
    setDirection(bounded > currentIndex ? 'forward' : 'backward')
    setCurrentIndex(bounded)
    setIndexOpen(false)
    void session.saveDraftNow()
  }

  const saveLabel =
    session.saveState === 'saving'
      ? '正在保存'
      : session.saveState === 'dirty'
        ? '有未保存修改'
        : session.saveState === 'error'
          ? '保存失败'
          : session.savedAt
            ? `已保存 ${formatTimestamp(session.savedAt)}`
            : '尚未修改'

  const confirmSubmit = async () => {
    const succeeded = await session.submitAnswers()
    if (succeeded) setConfirmOpen(false)
  }

  if (!questions.length) {
    return <InlineFeedback tone="danger">试卷中没有可作答题目，请联系比赛管理员检查试卷配置。</InlineFeedback>
  }

  return (
    <div className={styles.workbench}>
      <header className={styles.examHeader}>
        <div className={styles.examIdentity}>
          <span>THEORY EXAM</span>
          <h1>{session.paper.title || '理论考试'}</h1>
          {session.paper.description ? <p>{session.paper.description}</p> : null}
        </div>
        {!session.submitted ? (
          <div className={styles.examActions}>
            <ActionButton
              disabled={session.saveState === 'saving' || session.submitting}
              icon={<Save size={17} />}
              onClick={() => void session.saveDraftNow()}
              type="button"
            >
              保存草稿
            </ActionButton>
            <ActionButton
              disabled={session.submitting}
              icon={<Send size={17} />}
              onClick={() => setConfirmOpen(true)}
              tone="primary"
              type="button"
            >
              {session.submitting ? '正在提交' : '最终提交'}
            </ActionButton>
          </div>
        ) : null}
        <div className={styles.progressRow}>
          <div>
            <span>
              {answeredCount} / {questions.length} 已作答
            </span>
            <strong>{progress}%</strong>
          </div>
          <progress aria-label="答题进度" max={questions.length} value={answeredCount} />
        </div>
        <div className={styles.examFacts}>
          <span>
            <strong>{session.paper.totalScore ?? 0}</strong> 满分
          </span>
          <span>
            <Clock3 size={15} />
            截止 {formatTimestamp(deadline)}
          </span>
          <span data-save-state={session.saveState}>{saveLabel}</span>
          {session.submitted ? (
            <StatusPill tone="success">
              得分 {session.paper.score ?? 0} / {session.paper.totalScore ?? 0}
            </StatusPill>
          ) : null}
        </div>
      </header>

      {session.saveError ? (
        <div className={styles.feedbackRow}>
          <InlineFeedback tone="danger">{session.saveError}</InlineFeedback>
          <ActionButton onClick={() => void session.saveDraftNow()} type="button">
            重试保存
          </ActionButton>
        </div>
      ) : null}
      {session.submitError ? <InlineFeedback tone="danger">{session.submitError}</InlineFeedback> : null}
      {session.submitted ? (
        <InlineFeedback tone="success">
          答卷已于 {formatTimestamp(session.paper.submittedAt)} 提交，当前页面为只读状态。
        </InlineFeedback>
      ) : null}

      <div className={styles.mobileIndexBar}>
        <span>
          第 {currentIndex + 1} 题，共 {questions.length} 题
        </span>
        <ActionButton icon={<ListChecks size={17} />} onClick={() => setIndexOpen(true)} type="button">
          题目索引
        </ActionButton>
      </div>

      <div className={styles.examLayout}>
        <main className={styles.questionColumn}>
          {currentQuestion ? (
            <TheoryQuestion
              direction={direction}
              disabled={session.submitted || session.submitting}
              key={currentQuestionId}
              onChange={(selectedIndexes) => session.updateAnswer(currentQuestionId, selectedIndexes)}
              onToggleReview={() => session.toggleReview(currentQuestionId)}
              question={currentQuestion}
              review={session.reviewQuestionIds.has(currentQuestionId)}
              selectedIndexes={session.answers[currentQuestionId] ?? []}
            />
          ) : null}

          <nav aria-label="题目切换" className={styles.questionNavigation}>
            <ActionButton
              disabled={currentIndex <= 0}
              icon={<ChevronLeft size={17} />}
              onClick={() => goToQuestion(currentIndex - 1)}
              type="button"
            >
              上一题
            </ActionButton>
            <span>
              第 {currentIndex + 1} / {questions.length} 题
            </span>
            <ActionButton
              disabled={currentIndex >= questions.length - 1}
              icon={<ChevronRight size={17} />}
              onClick={() => goToQuestion(currentIndex + 1)}
              type="button"
            >
              下一题
            </ActionButton>
          </nav>
        </main>

        <aside className={styles.indexRail}>
          <header>
            <span>QUESTION INDEX</span>
            <h2>题目索引</h2>
          </header>
          <QuestionIndex
            activeIndex={currentIndex}
            answers={session.answers}
            onSelect={goToQuestion}
            questions={questions}
            reviewQuestionIds={session.reviewQuestionIds}
          />
        </aside>
      </div>

      <VNextDialog
        description={`当前已作答 ${answeredCount} 题，仍有 ${unansweredCount} 题未作答。提交后答案不可修改。`}
        eyebrow="FINAL SUBMISSION"
        footer={
          <>
            <ActionButton disabled={session.submitting} onClick={() => setConfirmOpen(false)} type="button">
              继续检查
            </ActionButton>
            <ActionButton
              disabled={session.submitting}
              icon={<Send size={17} />}
              onClick={() => void confirmSubmit()}
              tone="primary"
              type="button"
            >
              {session.submitting ? '正在提交' : '确认最终提交'}
            </ActionButton>
          </>
        }
        onClose={() => {
          if (!session.submitting) setConfirmOpen(false)
        }}
        open={confirmOpen && !session.submitted}
        title="确认提交答卷"
      >
        <div className={styles.submitSummary}>
          <AlertTriangle size={21} />
          <div>
            <strong>最终提交将立即判分</strong>
            <p>系统只接受一次最终提交。单选、多选和判断题均不计算部分分数，多选题必须完全一致才得分。</p>
          </div>
          <dl>
            <div>
              <dt>题目总数</dt>
              <dd>{questions.length}</dd>
            </div>
            <div>
              <dt>已作答</dt>
              <dd>{answeredCount}</dd>
            </div>
            <div>
              <dt>未作答</dt>
              <dd>{unansweredCount}</dd>
            </div>
          </dl>
        </div>
      </VNextDialog>

      <MobileQuestionIndex open={indexOpen} onClose={() => setIndexOpen(false)}>
        <QuestionIndex
          activeIndex={currentIndex}
          answers={session.answers}
          onSelect={goToQuestion}
          questions={questions}
          reviewQuestionIds={session.reviewQuestionIds}
        />
      </MobileQuestionIndex>
    </div>
  )
}
