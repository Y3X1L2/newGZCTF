import { ArrowLeft, Check, Clock3 } from 'lucide-react'
import { Link } from 'react-router'
import { TrainingCourseChapterModel, TrainingCourseProgressStatus } from '@Api'
import styles from './TrainingChapterPage.module.css'
import { chapterDepth } from './trainingChapterDomain'

interface ChapterTreeProps {
  courseId: number
  courseTitle?: string | null
  currentChapterId: number
  chapters: TrainingCourseChapterModel[]
}

export function ChapterTree({ courseId, courseTitle, currentChapterId, chapters }: ChapterTreeProps) {
  return (
    <aside className={styles.chapterTree}>
      <Link className={styles.courseBack} to={`/training/courses/${courseId}`}>
        <ArrowLeft size={16} />
        <span>{courseTitle || '返回课程'}</span>
      </Link>
      <div className={styles.treeHeading}>
        <span>CHAPTERS</span>
        <h2>课程目录</h2>
      </div>
      <nav aria-label="课程章节" className={styles.chapterList}>
        {chapters.map((item, index) => {
          const itemId = item.id ?? 0
          const active = itemId === currentChapterId
          const completed = item.progressStatus === TrainingCourseProgressStatus.Completed || Boolean(item.completedAt)
          const depth = chapterDepth(item, chapters)
          return (
            <Link
              className={`${active ? styles.chapterLinkActive : styles.chapterLink} ${depth === 1 ? styles.chapterDepthOne : depth === 2 ? styles.chapterDepthTwo : ''}`}
              key={itemId}
              to={`/training/courses/${courseId}/chapters/${itemId}`}
            >
              <span>{completed ? <Check size={14} /> : String(index + 1).padStart(2, '0')}</span>
              <strong>{item.title || `章节 ${index + 1}`}</strong>
            </Link>
          )
        })}
      </nav>
    </aside>
  )
}

interface ChapterOutlineProps {
  items: Array<{ id: string; label: string; level: 2 | 3 }>
  completed: boolean
  readPercent: number
}

export function ChapterOutline({ items, completed, readPercent }: ChapterOutlineProps) {
  return (
    <aside className={styles.outlineRail}>
      <span>ON THIS PAGE</span>
      <h2>文章目录</h2>
      <nav aria-label="文章目录">
        {items.map((item) => (
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
          <strong>{completed ? 100 : readPercent}%</strong>
          阅读进度
        </span>
      </div>
    </aside>
  )
}
