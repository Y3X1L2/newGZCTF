import { ArrowRight, FileQuestion } from 'lucide-react'
import { Link } from 'react-router'
import { TrainingCourseChapterModel } from '@Api'
import { StatusPill } from '../../../shared/Primitives'
import styles from './TrainingChapterPage.module.css'
import { trainingChapterProgress } from './trainingChapterDomain'

interface TrainingChapterTheoryPanelProps {
  courseId: number
  chapterId: number
  chapter: TrainingCourseChapterModel
  progress: ReturnType<typeof trainingChapterProgress>
}

export function TrainingChapterTheoryPanel({
  courseId,
  chapterId,
  chapter,
  progress,
}: TrainingChapterTheoryPanelProps) {
  const paper = chapter.theoryPaper
  if (!paper) return null

  return (
    <section className={styles.theorySection} id="chapter-theory">
      <div className={styles.theoryIdentity}>
        <FileQuestion size={22} />
        <div>
          <span>AFTER-CLASS EXERCISE</span>
          <h2>{paper.title || '课后练习'}</h2>
          <p>
            {paper.questionCount ?? 0} 题 · 满分 {paper.totalScore ?? 0} 分 · 章节要求 {progress.theoryRate}%
          </p>
        </div>
      </div>
      <div className={styles.theoryResult}>
        {progress.theorySubmitted ? (
          <StatusPill tone={progress.theorySatisfied ? 'success' : 'warning'}>
            {progress.theorySatisfied ? '已达到要求' : `得分 ${progress.theoryScore}/${progress.theoryTotal}`}
          </StatusPill>
        ) : (
          <StatusPill>尚未提交</StatusPill>
        )}
        {paper.isPublished ? (
          <Link className={styles.primaryLink} to={`/training/courses/${courseId}/chapters/${chapterId}/theory`}>
            {progress.theorySubmitted ? '查看答卷' : '进入练习'}
            <ArrowRight size={17} />
          </Link>
        ) : null}
      </div>
    </section>
  )
}
