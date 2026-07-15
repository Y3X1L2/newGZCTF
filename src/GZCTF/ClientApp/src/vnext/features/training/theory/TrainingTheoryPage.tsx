import { ArrowLeft, BookOpenCheck, CheckCircle2, RotateCcw } from 'lucide-react'
import { useCallback, useState } from 'react'
import { Link, useParams } from 'react-router'
import api, {
  TheoryAnswerSheetEditModel,
  TheoryAnswerSheetStatus,
  TheoryPlayerPaperModel,
  TrainingCourseChapterTheoryPlayerPaperModel,
} from '@Api'
import { ActionButton, InlineFeedback } from '../../../shared/Interaction'
import { DataState, StatusPill } from '../../../shared/Primitives'
import { errorMessage } from '../../../shared/errors'
import { useVNextPageTitle } from '../../../shared/useVNextPageTitle'
import { TheoryExamWorkbench } from '../../theory/workbench/TheoryExamWorkbench'
import styles from './TrainingTheoryPage.module.css'

export function TrainingTheoryPage() {
  const { courseId, chapterId } = useParams()
  const courseNumber = Number(courseId)
  const chapterNumber = Number(chapterId)
  const validIds =
    Number.isInteger(courseNumber) && courseNumber > 0 && Number.isInteger(chapterNumber) && chapterNumber > 0
  const courseRequest = api.trainingCourse.useTrainingCourseCourse(courseNumber, { revalidateOnFocus: false }, validIds)
  const chapterRequest = api.trainingCourse.useTrainingCourseChapter(
    courseNumber,
    chapterNumber,
    { revalidateOnFocus: false },
    validIds
  )
  const paperRequest = api.trainingCourse.useTrainingCourseChapterTheory(
    courseNumber,
    chapterNumber,
    { revalidateOnFocus: false, shouldRetryOnError: false },
    validIds
  )
  const course = courseRequest.data
  const chapter = chapterRequest.data
  const paper = paperRequest.data
  useVNextPageTitle(paper?.title || chapter?.title || '课后练习')
  const [retrying, setRetrying] = useState(false)
  const [retryError, setRetryError] = useState<string | null>(null)

  const saveDraft = useCallback(
    async (data: TheoryAnswerSheetEditModel) => {
      const response = await api.trainingCourse.trainingCourseSaveChapterTheoryDraft(courseNumber, chapterNumber, data)
      return response.data
    },
    [chapterNumber, courseNumber]
  )

  const submit = useCallback(
    async (data: TheoryAnswerSheetEditModel) => {
      const response = await api.trainingCourse.trainingCourseSubmitChapterTheory(courseNumber, chapterNumber, data)
      return response.data
    },
    [chapterNumber, courseNumber]
  )

  const onSubmitted = useCallback(
    (submittedPaper: TheoryPlayerPaperModel) => {
      void paperRequest.mutate(submittedPaper as TrainingCourseChapterTheoryPlayerPaperModel, { revalidate: false })
      void Promise.all([chapterRequest.mutate(), courseRequest.mutate()])
    },
    [chapterRequest, courseRequest, paperRequest]
  )

  const retry = async () => {
    if (!paper?.allowRetake || retrying) return
    setRetrying(true)
    setRetryError(null)
    try {
      const response = await api.trainingCourse.trainingCourseRetryChapterTheory(courseNumber, chapterNumber)
      await paperRequest.mutate(response.data, { revalidate: false })
      await chapterRequest.mutate()
    } catch (requestError) {
      setRetryError(errorMessage(requestError, '无法开始新的作答，请稍后重试。'))
    } finally {
      setRetrying(false)
    }
  }

  if (!validIds) return <DataState description="课程或章节编号不是有效数字。" title="课后练习参数错误" />
  if ((!course || !chapter || !paper) && !courseRequest.error && !chapterRequest.error && !paperRequest.error) {
    return <DataState description="正在读取课程、章节、试卷和服务端草稿。" loading title="课后练习加载中" />
  }
  if (!course || !chapter || !paper) {
    return <DataState description="课后练习尚未发布，或当前账户没有课程学习权限。" title="课后练习暂不可用" />
  }

  const submitted = paper.status === TheoryAnswerSheetStatus.Submitted

  return (
    <div className={styles.page}>
      <header className={styles.contextHeader}>
        <nav aria-label="课程位置" className={styles.breadcrumbs}>
          <Link to={`/training/courses/${courseNumber}`}>
            <ArrowLeft size={15} />
            {course.title || '课程详情'}
          </Link>
          <span>/</span>
          <Link to={`/training/courses/${courseNumber}/chapters/${chapterNumber}`}>{chapter.title || '课程章节'}</Link>
        </nav>
        <div className={styles.contextSummary}>
          <span>章节课后练习</span>
          <StatusPill tone="info">第 {paper.attemptNumber ?? 1} 次作答</StatusPill>
        </div>
      </header>

      {submitted ? (
        <section className={styles.submittedPanel}>
          <div>
            <CheckCircle2 size={21} />
            <span>
              <strong>
                得分 {paper.score ?? 0} / {paper.totalScore ?? 0}
              </strong>
              <small>{paper.passed ? '已通过课后练习' : `尚未达到 ${paper.passRate ?? 0}% 通过线`}</small>
            </span>
          </div>
          <div className={styles.submittedActions}>
            <StatusPill tone={paper.passed ? 'success' : 'warning'}>{paper.passed ? '已通过' : '未通过'}</StatusPill>
            {paper.allowRetake ? (
              <ActionButton
                disabled={retrying}
                icon={<RotateCcw size={16} />}
                onClick={() => void retry()}
                type="button"
              >
                {retrying ? '正在准备' : '重新作答'}
              </ActionButton>
            ) : null}
            <Link
              className={styles.returnLink}
              to={`/training/courses/${courseNumber}/chapters/${chapterNumber}#chapter-completion`}
            >
              <BookOpenCheck size={17} />
              返回章节末尾
            </Link>
          </div>
        </section>
      ) : null}

      {retryError ? <InlineFeedback tone="danger">{retryError}</InlineFeedback> : null}

      <TheoryExamWorkbench
        initialPaper={paper}
        key={`${paper.paperId ?? 0}:${paper.attemptNumber ?? 0}:${paper.status ?? 'draft'}`}
        onSubmitted={onSubmitted}
        saveDraft={saveDraft}
        submit={submit}
      />
    </div>
  )
}
