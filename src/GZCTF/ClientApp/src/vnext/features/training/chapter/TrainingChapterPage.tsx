import {
  ArrowLeft,
  ArrowRight,
  BookOpen,
  Check,
  CheckCircle2,
  Circle,
  Clock3,
  Download,
  ExternalLink,
  FileQuestion,
  FlaskConical,
  PlayCircle,
} from 'lucide-react'
import { useCallback, useEffect, useMemo, useState } from 'react'
import { Link, useLocation, useParams } from 'react-router'
import api, {
  ChallengeType,
  EnvironmentType,
  TheoryAnswerSheetStatus,
  TrainingCourseChallengeDetailModel,
  TrainingCourseChallengeModel,
  TrainingCourseChapterModel,
  TrainingCourseProgressStatus,
  TrainingCourseVideoProvider,
} from '@Api'
import { ActionButton, InlineFeedback } from '../../../shared/Interaction'
import { MarkdownContent, markdownOutline } from '../../../shared/MarkdownContent'
import { DataState, StatusPill } from '../../../shared/Primitives'
import { errorMessage } from '../../../shared/errors'
import { useVNextPageTitle } from '../../../shared/useVNextPageTitle'
import { FlagSubmission } from '../../challenge-runtime/FlagSubmission'
import { InstanceControl } from '../../challenge-runtime/InstanceControl'
import { formatFileSize } from '../training'
import styles from './TrainingChapterPage.module.css'
import { useTrainingFlagSubmission } from './useTrainingFlagSubmission'
import { useTrainingInstance } from './useTrainingInstance'

function challengeTypeLabel(type?: ChallengeType) {
  if (type === ChallengeType.DynamicContainer) return '动态容器'
  if (type === ChallengeType.StaticContainer) return '静态容器'
  if (type === ChallengeType.DynamicAttachment) return '动态附件'
  return '静态附件'
}

function environmentLabel(environment?: EnvironmentType) {
  if (environment === EnvironmentType.WindowsVM) return 'Windows'
  if (environment === EnvironmentType.Docker) return 'Docker'
  return '无运行环境'
}

function chapterDepth(chapter: TrainingCourseChapterModel, chapters: TrainingCourseChapterModel[]) {
  const parents = new Map(chapters.filter((item) => item.id !== undefined).map((item) => [item.id as number, item]))
  let parentId = chapter.parentId
  let depth = 0
  while (parentId && parents.has(parentId) && depth < 2) {
    depth += 1
    parentId = parents.get(parentId)?.parentId
  }
  return depth
}

function videoEmbedUrl(url?: string | null) {
  if (!url) return null
  try {
    const parsed = new URL(url, window.location.origin)
    if (/\.(mp4|webm|ogg)(?:$|\?)/i.test(parsed.pathname)) return null
    if (parsed.hostname === 'youtu.be') {
      const id = parsed.pathname.split('/').filter(Boolean)[0]
      return id ? `https://www.youtube-nocookie.com/embed/${id}` : null
    }
    if (parsed.hostname.endsWith('youtube.com')) {
      const id = parsed.searchParams.get('v')
      return id ? `https://www.youtube-nocookie.com/embed/${id}` : null
    }
    if (parsed.hostname === 'player.bilibili.com') return parsed.toString()
    if (parsed.hostname.endsWith('bilibili.com')) {
      const bvid = parsed.pathname.match(/\/video\/(BV[\w]+)/i)?.[1]
      return bvid ? `https://player.bilibili.com/player.html?bvid=${bvid}` : null
    }
  } catch {
    return null
  }
  return null
}

function directVideoUrl(chapter: TrainingCourseChapterModel) {
  if (chapter.videoProvider === TrainingCourseVideoProvider.LocalFile) return chapter.videoFileUrl ?? null
  if (
    chapter.videoProvider === TrainingCourseVideoProvider.ExternalUrl &&
    /\.(mp4|webm|ogg)(?:$|\?)/i.test(chapter.videoUrl ?? '')
  ) {
    return chapter.videoUrl ?? null
  }
  return null
}

function ChapterVideo({ chapter }: { chapter: TrainingCourseChapterModel }) {
  if (!chapter.videoProvider || chapter.videoProvider === TrainingCourseVideoProvider.None) return null
  const source = directVideoUrl(chapter)
  const embed =
    chapter.videoProvider === TrainingCourseVideoProvider.ExternalUrl ? videoEmbedUrl(chapter.videoUrl) : null

  return (
    <section className={styles.videoSection} id="chapter-video">
      <header className={styles.sectionHeader}>
        <div>
          <span>LESSON VIDEO</span>
          <h2>章节视频</h2>
        </div>
        <PlayCircle size={20} />
      </header>
      {source ? (
        <video className={styles.videoPlayer} controls preload="metadata" src={source} />
      ) : embed ? (
        <div className={styles.videoFrame}>
          <iframe
            allow="accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture; web-share"
            allowFullScreen
            referrerPolicy="strict-origin-when-cross-origin"
            src={embed}
            title={`${chapter.title || '章节'}视频`}
          />
        </div>
      ) : chapter.videoUrl ? (
        <a className={styles.externalVideo} href={chapter.videoUrl} rel="noreferrer noopener" target="_blank">
          <PlayCircle size={22} />
          <span>
            <strong>在新窗口打开章节视频</strong>
            <small>{chapter.videoUrl}</small>
          </span>
          <ExternalLink size={18} />
        </a>
      ) : (
        <InlineFeedback>视频资源尚未就绪，请联系课程教师。</InlineFeedback>
      )}
    </section>
  )
}

function TrainingLabCard({
  courseId,
  chapterId,
  challenge,
  refreshProgress,
}: {
  courseId: number
  chapterId: number
  challenge: TrainingCourseChallengeModel
  refreshProgress: () => Promise<void>
}) {
  const challengeId = challenge.exerciseChallengeId ?? 0
  const challengeRequest = api.trainingCourse.useTrainingCourseChallenge(
    courseId,
    challengeId,
    { chapterId },
    { revalidateOnFocus: false },
    challengeId > 0
  )
  const detail = challengeRequest.data
  const [flagValue, setFlagValue] = useState('')
  const [activeFlagId, setActiveFlagId] = useState<number | null>(null)

  const updateChallenge = useCallback(
    (next: TrainingCourseChallengeDetailModel) => {
      void challengeRequest.mutate(next, { revalidate: false })
    },
    [challengeRequest]
  )
  const refreshChallenge = useCallback(async () => challengeRequest.mutate(), [challengeRequest])
  const instance = useTrainingInstance({
    courseId,
    chapterId,
    challenge: detail,
    updateChallenge,
    refreshChallenge,
  })
  const flagSubmission = useTrainingFlagSubmission({
    courseId,
    chapterId,
    challenge: detail,
    updateChallenge,
    refreshChallenge,
    onAccepted: refreshProgress,
  })

  useEffect(() => {
    if (activeFlagId || !detail?.flags?.length) return
    setActiveFlagId(detail.flags[0].id ?? null)
  }, [activeFlagId, detail?.flags])

  const solvedFlagIds = useMemo(
    () => new Set(detail?.solved ? (detail.flags ?? []).map((flag, index) => flag.id ?? index + 1) : []),
    [detail?.flags, detail?.solved]
  )
  const pending = detail ? flagSubmission.isPending(detail.id ?? challengeId, activeFlagId, flagValue) : false
  const submissionLocked = detail?.limit && (detail.attempts ?? 0) >= detail.limit ? '该实验的提交次数已用完。' : null
  const attachmentUrl = detail?.context?.url

  return (
    <article className={styles.labItem}>
      <header className={styles.labHeader}>
        <div>
          <div className={styles.labTags}>
            <StatusPill tone="info">{String(challenge.category ?? '实验')}</StatusPill>
            <StatusPill>{challengeTypeLabel(challenge.type)}</StatusPill>
            <StatusPill>{environmentLabel(challenge.environment)}</StatusPill>
            <StatusPill tone={challenge.isRequired ? 'warning' : 'neutral'}>
              {challenge.isRequired ? '必做' : '选做'}
            </StatusPill>
          </div>
          <h3>{challenge.displayTitle || challenge.title || `实验 ${challengeId}`}</h3>
        </div>
        <StatusPill tone={detail?.solved || challenge.solved ? 'success' : 'neutral'}>
          {detail?.solved || challenge.solved ? '已完成' : '待完成'}
        </StatusPill>
      </header>

      {!detail && !challengeRequest.error ? (
        <DataState description="正在读取实验配置、附件和运行状态。" loading title="实验加载中" />
      ) : challengeRequest.error || !detail ? (
        <DataState description="实验配置暂时无法读取，请刷新页面后重试。" title="实验加载失败" />
      ) : (
        <>
          {detail.content ? <MarkdownContent className={styles.labDescription} source={detail.content} /> : null}

          {attachmentUrl ? (
            <a className={styles.attachment} href={attachmentUrl} rel="noreferrer noopener" target="_blank">
              <span>
                <Download size={18} />
                <span>
                  <strong>{challenge.attachmentFileName || '下载实验附件'}</strong>
                  <small>{formatFileSize(detail.context?.fileSize)}</small>
                </span>
              </span>
              <ExternalLink size={17} />
            </a>
          ) : challenge.hasAttachment ? (
            <InlineFeedback>实验已绑定附件，但当前下载地址暂不可用。</InlineFeedback>
          ) : null}

          <InstanceControl controller={instance} />

          <FlagSubmission
            activeFlagId={activeFlagId}
            challenge={detail}
            disabledReason={submissionLocked}
            feedback={flagSubmission.feedback}
            onFlagChange={setActiveFlagId}
            onSubmit={() =>
              void flagSubmission.submit({
                challengeId: detail.id ?? challengeId,
                flagId: activeFlagId,
                value: flagValue,
              })
            }
            onValueChange={setFlagValue}
            pending={pending}
            solved={Boolean(detail.solved)}
            solvedFlagIds={solvedFlagIds}
            value={flagValue}
          />
        </>
      )}
    </article>
  )
}

export function TrainingChapterPage() {
  const { courseId, chapterId } = useParams()
  const location = useLocation()
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
  const course = courseRequest.data
  const chapter = chapterRequest.data
  useVNextPageTitle(chapter?.title || '课程章节')
  const [completing, setCompleting] = useState(false)
  const [completionFeedback, setCompletionFeedback] = useState<{ tone: 'success' | 'danger'; message: string } | null>(
    null
  )

  const orderedChapters = useMemo(
    () =>
      [...(course?.chapters ?? [])]
        .filter((item) => item.id !== undefined && (course?.canEdit || item.isPublished))
        .sort((left, right) => (left.order ?? 0) - (right.order ?? 0) || (left.id ?? 0) - (right.id ?? 0)),
    [course]
  )
  const currentIndex = orderedChapters.findIndex((item) => item.id === chapterNumber)
  const previousChapter = currentIndex > 0 ? orderedChapters[currentIndex - 1] : null
  const nextChapter =
    currentIndex >= 0 && currentIndex < orderedChapters.length - 1 ? orderedChapters[currentIndex + 1] : null

  const refreshProgress = useCallback(async () => {
    await Promise.all([chapterRequest.mutate(), courseRequest.mutate()])
  }, [chapterRequest, courseRequest])

  useEffect(() => {
    window.scrollTo({ top: 0 })
  }, [chapterNumber])

  useEffect(() => {
    if (!chapter || !location.hash) return
    const encodedTargetId = location.hash.slice(1)
    let targetId = encodedTargetId

    try {
      targetId = decodeURIComponent(encodedTargetId)
    } catch {
      // Keep the raw fragment when a malformed external URL cannot be decoded.
    }

    const timer = window.setTimeout(() => document.getElementById(targetId)?.scrollIntoView({ block: 'start' }), 80)
    return () => window.clearTimeout(timer)
  }, [chapter, location.hash])

  if (!validIds) return <DataState description="课程或章节编号不是有效数字。" title="章节参数错误" />
  if ((!course || !chapter) && !courseRequest.error && !chapterRequest.error) {
    return <DataState description="正在读取课程目录、章节正文和学习状态。" loading title="章节加载中" />
  }
  if (!course || !chapter) {
    return <DataState description="章节不存在，或当前账户尚未获得课程学习权限。" title="无法打开章节" />
  }

  const policy = chapter.completionPolicy ?? {}
  const candidateChallenges = policy.requireAllRequiredChallenges
    ? (chapter.challenges ?? []).filter((item) => item.isRequired)
    : (chapter.challenges ?? [])
  const requiredChallengeCount = policy.requireAllRequiredChallenges
    ? candidateChallenges.length
    : Math.min(policy.requiredChallengeCount ?? 0, candidateChallenges.length)
  const solvedChallengeCount = candidateChallenges.filter((item) => item.solved).length
  const challengesSatisfied = solvedChallengeCount >= requiredChallengeCount
  const theoryRequired = Boolean(chapter.theoryPaper?.isPublished)
  const theoryTotal = chapter.theoryPaper?.totalScore ?? 0
  const theoryScore = chapter.theoryPaper?.score ?? 0
  const theoryRate = policy.theoryPassRate ?? chapter.theoryPaper?.passRate ?? 0
  const theorySubmitted = chapter.theoryPaper?.status === TheoryAnswerSheetStatus.Submitted
  const theorySatisfied =
    !theoryRequired || (theorySubmitted && (theoryTotal === 0 || theoryScore * 100 >= theoryTotal * theoryRate))
  const contentSatisfied = !policy.requireContentRead || (chapter.readPercent ?? 0) >= 100
  const completed = chapter.progressStatus === TrainingCourseProgressStatus.Completed || Boolean(chapter.completedAt)
  const blockingConditions = [challengesSatisfied, theorySatisfied].every(Boolean)

  const outline = [
    ...(chapter.videoProvider && chapter.videoProvider !== TrainingCourseVideoProvider.None
      ? [{ id: 'chapter-video', label: '章节视频', level: 2 as const }]
      : []),
    { id: 'chapter-content', label: '章节正文', level: 2 as const },
    ...markdownOutline(chapter.content ?? ''),
    ...((chapter.challenges?.length ?? 0) > 0 ? [{ id: 'chapter-labs', label: '章节实验', level: 2 as const }] : []),
    ...(chapter.theoryPaper ? [{ id: 'chapter-theory', label: '课后练习', level: 2 as const }] : []),
    { id: 'chapter-completion', label: '章节完成', level: 2 as const },
  ]

  const completeChapter = async () => {
    if (completed || completing || !blockingConditions) return
    setCompleting(true)
    setCompletionFeedback(null)
    try {
      await api.trainingCourse.trainingCourseCompleteChapter(courseNumber, chapterNumber)
      await refreshProgress()
      setCompletionFeedback({ tone: 'success', message: '章节已经完成，课程进度已刷新。' })
    } catch (requestError) {
      setCompletionFeedback({ tone: 'danger', message: errorMessage(requestError, '章节完成条件尚未满足。') })
      await refreshProgress()
    } finally {
      setCompleting(false)
    }
  }

  return (
    <div className={styles.page}>
      <div className={styles.learningLayout}>
        <aside className={styles.chapterTree}>
          <Link className={styles.courseBack} to={`/training/courses/${courseNumber}`}>
            <ArrowLeft size={16} />
            <span>{course.title || '返回课程'}</span>
          </Link>
          <div className={styles.treeHeading}>
            <span>CHAPTERS</span>
            <h2>课程目录</h2>
          </div>
          <nav aria-label="课程章节" className={styles.chapterList}>
            {orderedChapters.map((item, index) => {
              const itemId = item.id ?? 0
              const active = itemId === chapterNumber
              const itemCompleted =
                item.progressStatus === TrainingCourseProgressStatus.Completed || Boolean(item.completedAt)
              const depth = chapterDepth(item, orderedChapters)
              return (
                <Link
                  className={`${active ? styles.chapterLinkActive : styles.chapterLink} ${depth === 1 ? styles.chapterDepthOne : depth === 2 ? styles.chapterDepthTwo : ''}`}
                  key={itemId}
                  to={`/training/courses/${courseNumber}/chapters/${itemId}`}
                >
                  <span>{itemCompleted ? <Check size={14} /> : String(index + 1).padStart(2, '0')}</span>
                  <strong>{item.title || `章节 ${index + 1}`}</strong>
                </Link>
              )
            })}
          </nav>
        </aside>

        <main className={styles.articleColumn}>
          <header className={styles.chapterHeader}>
            <span className={styles.chapterEyebrow}>
              CHAPTER {String(Math.max(1, currentIndex + 1)).padStart(2, '0')} / {completed ? 'COMPLETED' : 'LEARNING'}
            </span>
            <h1>{chapter.title || `章节 ${chapterNumber}`}</h1>
            <p>{chapter.summary || '本章节暂未填写摘要，请按正文和学习任务完成本节内容。'}</p>
            <div className={styles.chapterMeta}>
              <span>
                <BookOpen size={16} />
                知识与实践
              </span>
              <span>
                <FlaskConical size={16} />
                {chapter.challenges?.length ?? 0} 个实验
              </span>
              <StatusPill tone={completed ? 'success' : 'info'}>{completed ? '已完成' : '学习中'}</StatusPill>
            </div>
          </header>

          <ChapterVideo chapter={chapter} />

          <article className={styles.chapterArticle} id="chapter-content">
            <MarkdownContent source={chapter.content || '暂无章节正文。'} />
          </article>

          {(chapter.challenges?.length ?? 0) > 0 ? (
            <section className={styles.labSection} id="chapter-labs">
              <header className={styles.sectionHeader}>
                <div>
                  <span>HANDS-ON LABS</span>
                  <h2>章节实验</h2>
                  <p>完成实验并提交正确 Flag 后，学习状态会自动刷新。</p>
                </div>
                <StatusPill tone={challengesSatisfied ? 'success' : 'info'}>
                  {solvedChallengeCount} / {requiredChallengeCount || chapter.challenges?.length || 0}
                </StatusPill>
              </header>
              <div className={styles.labList}>
                {(chapter.challenges ?? [])
                  .slice()
                  .sort((left, right) => (left.order ?? 0) - (right.order ?? 0))
                  .map((challenge) => (
                    <TrainingLabCard
                      challenge={challenge}
                      chapterId={chapterNumber}
                      courseId={courseNumber}
                      key={challenge.exerciseChallengeId}
                      refreshProgress={refreshProgress}
                    />
                  ))}
              </div>
            </section>
          ) : null}

          {chapter.theoryPaper ? (
            <section className={styles.theorySection} id="chapter-theory">
              <div className={styles.theoryIdentity}>
                <FileQuestion size={22} />
                <div>
                  <span>AFTER-CLASS EXERCISE</span>
                  <h2>{chapter.theoryPaper.title || '课后练习'}</h2>
                  <p>
                    {chapter.theoryPaper.questionCount ?? 0} 题 · 满分 {chapter.theoryPaper.totalScore ?? 0} 分 ·
                    章节要求
                    {theoryRate}%
                  </p>
                </div>
              </div>
              <div className={styles.theoryResult}>
                {theorySubmitted ? (
                  <StatusPill tone={theorySatisfied ? 'success' : 'warning'}>
                    {theorySatisfied ? '已达到要求' : `得分 ${theoryScore}/${theoryTotal}`}
                  </StatusPill>
                ) : (
                  <StatusPill>尚未提交</StatusPill>
                )}
                {chapter.theoryPaper.isPublished ? (
                  <Link
                    className={styles.primaryLink}
                    to={`/training/courses/${courseNumber}/chapters/${chapterNumber}/theory`}
                  >
                    {theorySubmitted ? '查看答卷' : '进入练习'}
                    <ArrowRight size={17} />
                  </Link>
                ) : null}
              </div>
            </section>
          ) : null}

          <section className={styles.completionSection} id="chapter-completion">
            <header className={styles.sectionHeader}>
              <div>
                <span>CHAPTER COMPLETION</span>
                <h2>章节完成</h2>
                <p>平台会按教师配置的阅读、实验和理论练习条件确认章节状态。</p>
              </div>
              <StatusPill tone={completed ? 'success' : blockingConditions ? 'info' : 'warning'}>
                {completed ? '已完成' : blockingConditions ? '可以确认' : '条件未满足'}
              </StatusPill>
            </header>

            <div className={styles.conditionList}>
              <div className={contentSatisfied || completed ? styles.conditionMet : styles.conditionPending}>
                {contentSatisfied || completed ? <CheckCircle2 size={18} /> : <Circle size={18} />}
                <span>
                  <strong>章节阅读</strong>
                  <small>{contentSatisfied || completed ? '阅读状态已记录' : '点击完成按钮时确认已经阅读正文'}</small>
                </span>
              </div>
              {requiredChallengeCount > 0 ? (
                <div className={challengesSatisfied ? styles.conditionMet : styles.conditionPending}>
                  {challengesSatisfied ? <CheckCircle2 size={18} /> : <Circle size={18} />}
                  <span>
                    <strong>必做实验</strong>
                    <small>
                      已完成 {solvedChallengeCount} / {requiredChallengeCount}
                    </small>
                  </span>
                </div>
              ) : null}
              {theoryRequired ? (
                <div className={theorySatisfied ? styles.conditionMet : styles.conditionPending}>
                  {theorySatisfied ? <CheckCircle2 size={18} /> : <Circle size={18} />}
                  <span>
                    <strong>课后练习</strong>
                    <small>
                      {theorySatisfied
                        ? `已达到 ${theoryRate}% 要求`
                        : completed
                          ? `当前成绩未达到 ${theoryRate}%，章节已历史完成`
                          : `需要达到 ${theoryRate}%`}
                    </small>
                  </span>
                </div>
              ) : null}
            </div>

            {completionFeedback ? (
              <InlineFeedback tone={completionFeedback.tone}>{completionFeedback.message}</InlineFeedback>
            ) : null}
            <div className={styles.completionActions}>
              <ActionButton
                disabled={completed || completing || !blockingConditions}
                icon={completed ? <Check size={17} /> : <BookOpen size={17} />}
                onClick={() => void completeChapter()}
                tone="primary"
                type="button"
              >
                {completed
                  ? '章节已完成'
                  : completing
                    ? '正在确认'
                    : contentSatisfied
                      ? '标记章节完成'
                      : '确认阅读并完成'}
              </ActionButton>
              {!completed && !blockingConditions ? <small>请先完成上方标记为未满足的条件。</small> : null}
            </div>
          </section>

          <nav aria-label="前后章节" className={styles.chapterPagination}>
            {previousChapter?.id ? (
              <Link to={`/training/courses/${courseNumber}/chapters/${previousChapter.id}`}>
                <ArrowLeft size={17} />
                <span>
                  <small>上一章</small>
                  <strong>{previousChapter.title}</strong>
                </span>
              </Link>
            ) : (
              <span />
            )}
            {nextChapter?.id ? (
              <Link to={`/training/courses/${courseNumber}/chapters/${nextChapter.id}`}>
                <span>
                  <small>下一章</small>
                  <strong>{nextChapter.title}</strong>
                </span>
                <ArrowRight size={17} />
              </Link>
            ) : null}
          </nav>
        </main>

        <aside className={styles.outlineRail}>
          <span>ON THIS PAGE</span>
          <h2>文章目录</h2>
          <nav aria-label="文章目录">
            {outline.map((item) => (
              <a
                className={item.level === 3 ? styles.outlineNested : styles.outlineLink}
                href={`#${item.id}`}
                key={`${item.id}-${item.label}`}
              >
                {item.label}
              </a>
            ))}
          </nav>
          <div className={styles.readingStatus}>
            <Clock3 size={16} />
            <span>
              <strong>{completed ? 100 : (chapter.readPercent ?? 0)}%</strong>
              阅读进度
            </span>
          </div>
        </aside>
      </div>
    </div>
  )
}
