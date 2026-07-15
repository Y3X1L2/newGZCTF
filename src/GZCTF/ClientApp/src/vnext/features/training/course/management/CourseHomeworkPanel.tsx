import { BookOpenCheck, Pencil } from 'lucide-react'
import { Link } from 'react-router'
import { TrainingCourseModel } from '@Api'
import { DataState, StatusPill } from '../../../../shared/Primitives'
import styles from './CourseHomeworkPanel.module.css'

export function CourseHomeworkPanel({ course }: { course: TrainingCourseModel }) {
  const courseId = course.id ?? 0
  const chapters = [...(course.chapters ?? [])].sort((left, right) => (left.order ?? 0) - (right.order ?? 0))

  return (
    <section className={styles.panel}>
      <header>
        <span>CHAPTER ASSESSMENTS</span>
        <h2>课后练习</h2>
        <p>每个章节最多配置一套理论试卷，课程题库可被不同章节重复引用。</p>
      </header>
      {chapters.length ? (
        <div className={styles.homeworkList}>
          {chapters.map((chapter, index) => {
            const configured = Boolean(chapter.theoryPaper?.id || chapter.theoryPaper?.questionCount)
            return (
              <article key={chapter.id}>
                <span className={styles.chapterNumber}>{String(index + 1).padStart(2, '0')}</span>
                <div>
                  <strong>{chapter.title || `章节 ${index + 1}`}</strong>
                  <small>
                    {configured
                      ? `${chapter.theoryPaper?.questionCount ?? 0} 题 · ${chapter.theoryPaper?.totalScore ?? 0} 分 · 通过线 ${chapter.theoryPaper?.passRate ?? 0}%`
                      : '尚未配置课后练习'}
                  </small>
                </div>
                <StatusPill tone={chapter.theoryPaper?.isPublished ? 'success' : configured ? 'warning' : 'neutral'}>
                  {chapter.theoryPaper?.isPublished ? '已发放' : configured ? '草稿' : '未配置'}
                </StatusPill>
                {chapter.id ? (
                  <Link
                    aria-label={`配置 ${chapter.title || '章节'} 的课后练习`}
                    to={`/training/courses/${courseId}/chapters/${chapter.id}/theory-edit`}
                  >
                    {configured ? <Pencil size={16} /> : <BookOpenCheck size={16} />}
                    {configured ? '配置' : '创建'}
                  </Link>
                ) : null}
              </article>
            )
          })}
        </div>
      ) : (
        <DataState description="请先创建课程章节，再配置章节课后练习。" title="暂无课程章节" />
      )}
    </section>
  )
}
