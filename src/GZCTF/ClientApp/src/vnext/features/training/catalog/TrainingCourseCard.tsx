import { BookOpen, GraduationCap, UserRound } from 'lucide-react'
import { Link } from 'react-router'
import { TrainingCourseModel } from '@Api'
import { GeometricPoster, StatusPill } from '../../../shared/Primitives'
import { courseProgress, courseStatusLabel, courseStatusTone, courseTeacherNames } from '../training'
import styles from './TrainingCourseCard.module.css'

function tagTone(index: number) {
  return index % 3 === 1 ? 'info' : index % 3 === 2 ? 'warning' : 'neutral'
}

export function TrainingCourseCard({ course, compact = false }: { course: TrainingCourseModel; compact?: boolean }) {
  const progress = courseProgress(course)
  const id = course.id ?? 0
  const title = course.title?.trim() || `课程 ${id}`

  return (
    <Link className={`${styles.card} ${compact ? styles.cardCompact : ''}`} to={`/training/courses/${id}`}>
      <div className={styles.poster}>
        <GeometricPoster alt={`${title}课程海报`} src={course.coverUrl} tone={course.canEdit ? 'orange' : 'blue'} />
        <span className={styles.status}>
          <StatusPill tone={courseStatusTone(course)}>{courseStatusLabel(course)}</StatusPill>
        </span>
      </div>
      <div className={styles.body}>
        <div className={styles.tags}>
          {(course.tags ?? []).slice(0, 3).map((tag, index) => (
            <StatusPill key={`${tag}-${index}`} tone={tagTone(index)}>
              {tag}
            </StatusPill>
          ))}
        </div>
        <h3>{title}</h3>
        <p>{course.summary?.trim() || '课程简介尚未填写。'}</p>
        <div className={styles.meta}>
          <span>
            <UserRound size={14} />
            {courseTeacherNames(course)}
          </span>
          <span>
            <BookOpen size={14} />
            {course.chapterCount ?? course.totalChapterCount ?? 0} 章
          </span>
        </div>
        <div className={styles.progress}>
          <div>
            <span>
              <GraduationCap size={14} />
              学习进度
            </span>
            <strong>{progress.percent}%</strong>
          </div>
          <progress aria-label={`${title}学习进度`} max={100} value={progress.percent} />
        </div>
      </div>
    </Link>
  )
}
