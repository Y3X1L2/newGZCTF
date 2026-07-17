import {
  ArrowLeft,
  ArrowRight,
  BookOpen,
  Check,
  Clock3,
  GraduationCap,
  Pencil,
  Plus,
  Send,
  UserRound,
  UsersRound,
} from 'lucide-react'
import { useEffect, useMemo, useState } from 'react'
import { Link, useParams, useSearchParams } from 'react-router'
import {
  TrainingCourseChapterModel,
  TrainingCourseEnrollmentStatus,
  TrainingCourseProgressStatus,
  TrainingCourseStatus,
} from '@Api'
import { ActionButton, InlineFeedback, VNextDialog } from '../../../shared/Interaction'
import { MarkdownContent } from '../../../shared/MarkdownContent'
import { DataState, GeometricPoster, StatusPill } from '../../../shared/Primitives'
import { errorMessage } from '../../../shared/errors'
import { useVNextPageTitle } from '../../../shared/useVNextPageTitle'
import { trainingLearnerApi, useTrainingCourseDetail } from '../api/trainingLearnerApi'
import {
  courseProgress,
  courseStatusLabel,
  courseStatusTone,
  formatTrainingDate,
} from '../training'
import styles from './TrainingCoursePage.module.css'
import { CourseChallengesPanel } from './management/CourseChallengesPanel'
import { CourseEnvironmentPanel } from './management/CourseEnvironmentPanel'
import { CourseHomeworkPanel } from './management/CourseHomeworkPanel'
import { CourseStudentsPanel } from './management/CoursePeoplePanels'
import { CourseResourcesPanel } from './management/CourseResourcesPanel'
import { CourseTeachersPanel } from './management/CourseTeachersPanel'
import { CourseTheoryBankPanel } from './management/CourseTheoryBankPanel'

type CourseTab =
  | 'about'
  | 'chapters'
  | 'resources'
  | 'progress'
  | 'students'
  | 'teachers'
  | 'environments'
  | 'challenges'
  | 'theoryBank'
  | 'homework'

const tabLabels: Record<CourseTab, string> = {
  about: '课程介绍',
  chapters: '课程章节',
  resources: '课程资源',
  progress: '学习状态',
  students: '学员管理',
  teachers: '授课教师',
  environments: '环境模板',
  challenges: '题目管理',
  theoryBank: '理论题库',
  homework: '课后练习',
}

function chapterState(chapter: TrainingCourseChapterModel) {
  if (chapter.progressStatus === TrainingCourseProgressStatus.Completed)
    return { label: '已完成', tone: 'success' as const }
  if (chapter.progressStatus === TrainingCourseProgressStatus.Learning)
    return { label: '学习中', tone: 'info' as const }
  return { label: '未开始', tone: 'neutral' as const }
}

export function TrainingCoursePage() {
  const { courseId } = useParams()
  const id = Number(courseId)
  const validId = Number.isInteger(id) && id > 0
  const [searchParams, setSearchParams] = useSearchParams()
  const courseRequest = useTrainingCourseDetail(id, validId)
  const course = courseRequest.data
  useVNextPageTitle(course?.title || '课程详情')
  const [enrollOpen, setEnrollOpen] = useState(false)
  const [applyReason, setApplyReason] = useState('')
  const [submitting, setSubmitting] = useState(false)
  const [feedback, setFeedback] = useState<{ tone: 'success' | 'danger'; message: string } | null>(null)

  const canOpenLearning = Boolean(course?.canLearn || course?.canEdit)
  const availableTabs = useMemo<CourseTab[]>(() => {
    const tabs: CourseTab[] = canOpenLearning ? ['about', 'chapters', 'resources', 'progress'] : ['about', 'resources']
    if (course?.canManageEnrollments) tabs.push('students')
    if (course?.canEdit) tabs.push('teachers', 'environments', 'challenges', 'theoryBank', 'homework')
    return tabs
  }, [canOpenLearning, course?.canEdit, course?.canManageEnrollments])
  const rawTab = searchParams.get('tab') as CourseTab | null
  const activeTab = rawTab && availableTabs.includes(rawTab) ? rawTab : 'about'

  useEffect(() => {
    if (!course || !rawTab || availableTabs.includes(rawTab)) return
    const next = new URLSearchParams(searchParams)
    next.delete('tab')
    setSearchParams(next, { replace: true })
  }, [availableTabs, course, rawTab, searchParams, setSearchParams])

  const chapters = useMemo(
    () =>
      [...(course?.chapters ?? [])]
        .filter((chapter) => chapter.id !== undefined && (course?.canEdit || chapter.isPublished))
        .sort((left, right) => (left.order ?? 0) - (right.order ?? 0)),
    [course]
  )
  const resources = useMemo(
    () =>
      [...(course?.resources ?? [])]
        .filter((resource) => resource.isVisible !== false)
        .sort((a, b) => (a.order ?? 0) - (b.order ?? 0)),
    [course?.resources]
  )
  const progress = course ? courseProgress(course) : { completed: 0, total: 0, percent: 0 }
  const firstChapter = chapters[0]

  const setTab = (tab: CourseTab) => {
    const next = new URLSearchParams(searchParams)
    if (tab === 'about') next.delete('tab')
    else next.set('tab', tab)
    setSearchParams(next, { replace: true })
  }

  const enroll = async () => {
    if (!course || submitting) return
    setSubmitting(true)
    setFeedback(null)
    try {
      await trainingLearnerApi.enroll(id, applyReason.trim())
      await courseRequest.mutate()
      setEnrollOpen(false)
      setApplyReason('')
      setFeedback({ tone: 'success', message: '报名申请已提交，课程状态已更新。' })
    } catch (requestError) {
      setFeedback({ tone: 'danger', message: errorMessage(requestError, '报名申请提交失败。') })
    } finally {
      setSubmitting(false)
    }
  }

  const cancelEnrollment = async () => {
    if (!course || submitting) return
    setSubmitting(true)
    setFeedback(null)
    try {
      await trainingLearnerApi.cancelEnrollment(id)
      await courseRequest.mutate()
      setFeedback({ tone: 'success', message: '报名申请已撤回。' })
    } catch (requestError) {
      setFeedback({ tone: 'danger', message: errorMessage(requestError, '撤回报名申请失败。') })
    } finally {
      setSubmitting(false)
    }
  }

  if (!validId) return <DataState description="课程编号不是有效数字。" title="课程参数错误" />
  if (!course && !courseRequest.error)
    return <DataState description="正在读取课程、章节和学习状态。" loading title="课程加载中" />
  if (!course) return <DataState description="课程不存在，或当前账户没有查看权限。" title="无法打开课程" />

  const enrollmentAction = (() => {
    if (course.canEdit || course.enrollmentStatus === TrainingCourseEnrollmentStatus.Approved || course.canLearn) {
      return firstChapter ? (
        <Link className={styles.primaryLink} to={`/training/courses/${id}/chapters/${firstChapter.id}`}>
          <BookOpen size={17} />
          {progress.percent > 0 ? '继续学习' : '开始学习'}
          <ArrowRight size={17} />
        </Link>
      ) : null
    }
    if (course.enrollmentStatus === TrainingCourseEnrollmentStatus.Pending) {
      return (
        <ActionButton disabled={submitting} onClick={() => void cancelEnrollment()} type="button">
          {submitting ? '正在撤回' : '撤回申请'}
        </ActionButton>
      )
    }
    return (
      <ActionButton icon={<Send size={17} />} onClick={() => setEnrollOpen(true)} tone="primary" type="button">
        {course.enrollmentStatus === TrainingCourseEnrollmentStatus.Rejected ? '重新申请' : '申请报名'}
      </ActionButton>
    )
  })()

  return (
    <div className={styles.page}>
      <Link className={styles.backLink} to="/training">
        <ArrowLeft size={16} />
        返回培训
      </Link>

      <section className={styles.hero}>
        <div className={styles.poster}>
          <GeometricPoster alt={`${course.title || '课程'}课程海报`} src={course.coverUrl} tone="blue" />
        </div>
        <div className={styles.heroBody}>
          <div className={styles.heroStatus}>
            <StatusPill tone={courseStatusTone(course)}>{courseStatusLabel(course)}</StatusPill>
            <StatusPill tone={course.status === TrainingCourseStatus.Published ? 'success' : 'warning'}>
              {course.status === TrainingCourseStatus.Published
                ? '已发布'
                : course.status === TrainingCourseStatus.Archived
                  ? '已归档'
                  : '草稿'}
            </StatusPill>
          </div>
          <h1>{course.title || `课程 ${id}`}</h1>
          <p>{course.summary || '课程简介尚未填写。'}</p>
          <div className={styles.tags}>
            {(course.tags ?? []).map((tag) => (
              <StatusPill key={tag}>{tag}</StatusPill>
            ))}
          </div>
          <div className={styles.heroMeta}>
            <span>
              <UserRound size={16} />
              任课教师：
              {(course.teachers ?? []).length
                ? course.teachers?.map((teacher, index) => (
                    <span key={teacher.teacherId || teacher.userName}>
                      {index > 0 ? '、' : ''}
                      {teacher.teacherId ? (
                        <Link to={`/users/${teacher.teacherId}`}>{teacher.realName || teacher.userName}</Link>
                      ) : (
                        teacher.realName || teacher.userName
                      )}
                    </span>
                  ))
                : '暂未指定教师'}
            </span>
            <span>
              <UsersRound size={16} />
              {course.enrollmentCount ?? 0} 名学员
            </span>
            <span>
              <BookOpen size={16} />
              {course.chapterCount ?? 0} 个章节
            </span>
          </div>
          <div className={styles.heroProgress}>
            <div>
              <span>我的学习进度</span>
              <strong>{progress.percent}%</strong>
            </div>
            <progress aria-label="课程学习进度" max={100} value={progress.percent} />
          </div>
          <div className={styles.heroActions}>
            {course.canEdit ? (
              <Link className={styles.secondaryLink} to={`/training/courses/${id}/edit`}>
                <Pencil size={16} />
                编辑课程
              </Link>
            ) : null}
            {enrollmentAction}
          </div>
        </div>
      </section>

      {feedback ? <InlineFeedback tone={feedback.tone}>{feedback.message}</InlineFeedback> : null}
      {course.enrollmentStatus === TrainingCourseEnrollmentStatus.Rejected ? (
        <InlineFeedback tone="danger">上次报名未通过，可以修改申请理由后重新提交。</InlineFeedback>
      ) : null}
      {!canOpenLearning ? (
        <InlineFeedback>当前只能查看课程介绍和资源摘要。报名审核通过后将开放章节与学习状态。</InlineFeedback>
      ) : null}

      <nav aria-label="课程内容" className={styles.tabs}>
        {availableTabs.map((tab) => (
          <button
            className={activeTab === tab ? styles.tabActive : styles.tab}
            key={tab}
            onClick={() => setTab(tab)}
            type="button"
          >
            {tabLabels[tab]}
          </button>
        ))}
      </nav>

      <div className={styles.tabContent}>
        {activeTab === 'about' ? (
          <div className={styles.aboutLayout}>
            <article className={styles.article}>
              <header>
                <span>ABOUT THIS COURSE</span>
                <h2>课程介绍</h2>
              </header>
              <MarkdownContent source={course.description || course.summary || '课程介绍尚未填写。'} />
            </article>
            <aside className={styles.courseFacts}>
              <div>
                <strong>{course.chapterCount ?? 0}</strong>
                <span>章节</span>
              </div>
              <div>
                <strong>{course.challenges?.length ?? 0}</strong>
                <span>实验</span>
              </div>
              <div>
                <strong>{chapters.filter((chapter) => chapter.theoryPaper?.isPublished).length}</strong>
                <span>理论练习</span>
              </div>
              <div>
                <strong>{course.resourceCount ?? resources.length}</strong>
                <span>资源</span>
              </div>
            </aside>
          </div>
        ) : null}

        {activeTab === 'chapters' ? (
          <section className={styles.chapterSection}>
            <header>
              <div>
                <span>SYLLABUS</span>
                <h2>课程章节</h2>
              </div>
              <div className={styles.sectionActions}>
                <StatusPill tone="info">
                  已完成 {progress.completed} / {progress.total}
                </StatusPill>
                {course.canEdit ? (
                  <Link className={styles.secondaryLink} to={`/training/courses/${id}/chapters/new`}>
                    <Plus size={16} />
                    添加章节
                  </Link>
                ) : null}
              </div>
            </header>
            {chapters.length ? (
              <div className={styles.chapterList}>
                {chapters.map((chapter, index) => {
                  const state = chapterState(chapter)
                  return (
                    <article className={styles.chapterRow} key={chapter.id}>
                      <span className={styles.chapterNumber}>{String(index + 1).padStart(2, '0')}</span>
                      <Link className={styles.chapterIdentity} to={`/training/courses/${id}/chapters/${chapter.id}`}>
                        <strong>{chapter.title || `章节 ${index + 1}`}</strong>
                        <small>{chapter.summary || '暂无章节摘要。'}</small>
                      </Link>
                      <span className={styles.chapterFeatures}>
                        {(chapter.challenges?.length ?? 0) > 0 ? <span>实验 {chapter.challenges?.length}</span> : null}
                        {chapter.theoryPaper?.isPublished ? <span>课后测试</span> : null}
                      </span>
                      <StatusPill tone={state.tone}>{state.label}</StatusPill>
                      {course.canEdit ? (
                        <Link
                          aria-label={`编辑章节 ${chapter.title || index + 1}`}
                          className={styles.chapterEditLink}
                          to={`/training/courses/${id}/chapters/${chapter.id}/edit`}
                        >
                          <Pencil size={16} />
                        </Link>
                      ) : (
                        <Link
                          aria-label={`打开章节 ${chapter.title || index + 1}`}
                          className={styles.chapterOpenLink}
                          to={`/training/courses/${id}/chapters/${chapter.id}`}
                        >
                          <ArrowRight size={17} />
                        </Link>
                      )}
                    </article>
                  )
                })}
              </div>
            ) : (
              <DataState description="课程教师尚未发布章节。" title="暂无课程章节" />
            )}
          </section>
        ) : null}

        {activeTab === 'resources' ? (
          <CourseResourcesPanel
            canOpenLearning={canOpenLearning}
            course={course}
            onCourseChanged={() => courseRequest.mutate()}
          />
        ) : null}

        {activeTab === 'progress' ? (
          <section className={styles.progressSection}>
            <header>
              <div>
                <span>MY LEARNING</span>
                <h2>学习状态</h2>
              </div>
              <StatusPill tone={progress.percent === 100 ? 'success' : 'info'}>{progress.percent}%</StatusPill>
            </header>
            <div className={styles.progressSummary}>
              <div>
                <GraduationCap size={19} />
                <span>课程进度</span>
                <strong>{progress.percent}%</strong>
              </div>
              <div>
                <Check size={19} />
                <span>完成章节</span>
                <strong>{progress.completed}</strong>
              </div>
              <div>
                <Clock3 size={19} />
                <span>最近学习</span>
                <strong>{formatTrainingDate(course.lastStudiedAt)}</strong>
              </div>
            </div>
            <div className={styles.progressChapters}>
              {chapters.map((chapter, index) => {
                const state = chapterState(chapter)
                return (
                  <div key={chapter.id}>
                    <span>{String(index + 1).padStart(2, '0')}</span>
                    <strong>{chapter.title || `章节 ${index + 1}`}</strong>
                    <progress
                      aria-label={`${chapter.title || '章节'}阅读进度`}
                      max={100}
                      value={
                        chapter.readPercent ??
                        (chapter.progressStatus === TrainingCourseProgressStatus.Completed ? 100 : 0)
                      }
                    />
                    <StatusPill tone={state.tone}>{state.label}</StatusPill>
                  </div>
                )
              })}
            </div>
          </section>
        ) : null}

        {activeTab === 'students' ? <CourseStudentsPanel course={course} /> : null}

        {activeTab === 'teachers' ? (
          <CourseTeachersPanel course={course} onCourseChanged={() => courseRequest.mutate()} />
        ) : null}

        {activeTab === 'environments' ? <CourseEnvironmentPanel course={course} /> : null}

        {activeTab === 'challenges' ? (
          <CourseChallengesPanel course={course} onCourseChanged={() => courseRequest.mutate()} />
        ) : null}

        {activeTab === 'theoryBank' ? <CourseTheoryBankPanel course={course} /> : null}

        {activeTab === 'homework' ? <CourseHomeworkPanel course={course} /> : null}
      </div>

      <VNextDialog
        description={
          course.enrollmentStatus === TrainingCourseEnrollmentStatus.Rejected
            ? '修改申请理由后重新提交，教师将再次审核。'
            : '提交后可在课程页查看审核状态，审核通过前只能查看课程简介和资源摘要。'
        }
        eyebrow="COURSE ENROLLMENT"
        footer={
          <>
            <ActionButton disabled={submitting} onClick={() => setEnrollOpen(false)} type="button">
              取消
            </ActionButton>
            <ActionButton
              disabled={submitting}
              icon={<Send size={16} />}
              onClick={() => void enroll()}
              tone="primary"
              type="button"
            >
              {submitting ? '正在提交' : '提交申请'}
            </ActionButton>
          </>
        }
        onClose={() => {
          if (!submitting) setEnrollOpen(false)
        }}
        open={enrollOpen}
        title="申请加入课程"
      >
        <label className={styles.enrollReason}>
          <span>申请理由</span>
          <textarea
            maxLength={512}
            onChange={(event) => setApplyReason(event.currentTarget.value)}
            placeholder="说明希望学习本课程的原因，可留空。"
            rows={5}
            value={applyReason}
          />
          <small>{applyReason.length} / 512</small>
        </label>
      </VNextDialog>
    </div>
  )
}
