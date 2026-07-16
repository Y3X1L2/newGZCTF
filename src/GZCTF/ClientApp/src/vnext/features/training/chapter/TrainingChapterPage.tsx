import { ArrowLeft, ArrowRight, BookOpen, FlaskConical } from 'lucide-react'
import { Link, useLocation, useParams } from 'react-router'
import { MarkdownContent } from '../../../shared/MarkdownContent'
import { DataState, StatusPill } from '../../../shared/Primitives'
import { useVNextPageTitle } from '../../../shared/useVNextPageTitle'
import { TrainingChapterCompletionPanel } from './TrainingChapterCompletionPanel'
import { ChapterOutline, ChapterTree } from './TrainingChapterNavigation'
import styles from './TrainingChapterPage.module.css'
import { TrainingChapterTheoryPanel } from './TrainingChapterTheoryPanel'
import { TrainingChapterVideo } from './TrainingChapterVideo'
import { TrainingLabCard } from './TrainingLabCard'
import { useTrainingChapterController } from './useTrainingChapterController'

export function TrainingChapterPage() {
  const { courseId, chapterId } = useParams()
  const location = useLocation()
  const courseNumber = Number(courseId)
  const chapterNumber = Number(chapterId)
  const controller = useTrainingChapterController(courseNumber, chapterNumber, location.hash)
  useVNextPageTitle(controller.chapter?.title || '课程章节')

  if (!controller.validIds) {
    return <DataState description="课程或章节编号不是有效数字。" title="章节参数错误" />
  }
  if (controller.loading) {
    return <DataState description="正在读取课程目录、章节正文和学习状态。" loading title="章节加载中" />
  }
  if (!controller.course || !controller.chapter || !controller.progress) {
    return <DataState description="章节不存在，或当前账户尚未获得课程学习权限。" title="无法打开章节" />
  }

  const { course, chapter, progress } = controller

  return (
    <div className={styles.page}>
      <div className={styles.learningLayout}>
        <ChapterTree
          chapters={controller.orderedChapters}
          courseId={courseNumber}
          courseTitle={course.title}
          currentChapterId={chapterNumber}
        />

        <main className={styles.articleColumn}>
          <header className={styles.chapterHeader}>
            <span className={styles.chapterEyebrow}>
              CHAPTER {String(Math.max(1, controller.currentIndex + 1)).padStart(2, '0')} /{' '}
              {progress.completed ? 'COMPLETED' : 'LEARNING'}
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
              <StatusPill tone={progress.completed ? 'success' : 'info'}>
                {progress.completed ? '已完成' : '学习中'}
              </StatusPill>
            </div>
          </header>

          <TrainingChapterVideo chapter={chapter} />
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
                <StatusPill tone={progress.challengesSatisfied ? 'success' : 'info'}>
                  {progress.solvedChallengeCount} / {progress.requiredChallengeCount || chapter.challenges?.length || 0}
                </StatusPill>
              </header>
              <div className={styles.labList}>
                {[...(chapter.challenges ?? [])]
                  .sort((left, right) => (left.order ?? 0) - (right.order ?? 0))
                  .map((challenge) => (
                    <TrainingLabCard
                      challenge={challenge}
                      chapterId={chapterNumber}
                      courseId={courseNumber}
                      key={challenge.exerciseChallengeId}
                      refreshProgress={controller.refreshProgress}
                    />
                  ))}
              </div>
            </section>
          ) : null}

          <TrainingChapterTheoryPanel
            chapter={chapter}
            chapterId={chapterNumber}
            courseId={courseNumber}
            progress={progress}
          />
          <TrainingChapterCompletionPanel
            completing={controller.completing}
            feedback={controller.completionFeedback}
            onComplete={controller.completeChapter}
            progress={progress}
          />

          <nav aria-label="前后章节" className={styles.chapterPagination}>
            {controller.previousChapter?.id ? (
              <Link to={`/training/courses/${courseNumber}/chapters/${controller.previousChapter.id}`}>
                <ArrowLeft size={17} />
                <span>
                  <small>上一章</small>
                  <strong>{controller.previousChapter.title}</strong>
                </span>
              </Link>
            ) : (
              <span />
            )}
            {controller.nextChapter?.id ? (
              <Link to={`/training/courses/${courseNumber}/chapters/${controller.nextChapter.id}`}>
                <span>
                  <small>下一章</small>
                  <strong>{controller.nextChapter.title}</strong>
                </span>
                <ArrowRight size={17} />
              </Link>
            ) : null}
          </nav>
        </main>

        <ChapterOutline
          completed={progress.completed}
          items={controller.outline}
          readPercent={chapter.readPercent ?? 0}
        />
      </div>
    </div>
  )
}
