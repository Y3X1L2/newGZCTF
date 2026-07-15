import { TrainingActivityPointModel } from '@Api'
import styles from './TrainingActivityCalendar.module.css'

function activityValue(point: TrainingActivityPointModel) {
  return (
    (point.studyActions ?? 0) +
    (point.completedChapters ?? 0) * 2 +
    (point.acceptedChallenges ?? 0) * 2 +
    (point.checkedIn ? 1 : 0)
  )
}

function activityLevel(point: TrainingActivityPointModel) {
  const value = activityValue(point)
  if (value <= 0) return 0
  if (value <= 2) return 1
  if (value <= 5) return 2
  if (value <= 9) return 3
  return 4
}

function label(point: TrainingActivityPointModel) {
  const actions = point.studyActions ?? 0
  const chapters = point.completedChapters ?? 0
  const challenges = point.acceptedChallenges ?? 0
  return `${point.date ?? '未知日期'}：学习 ${actions} 次，完成章节 ${chapters} 个，完成实验 ${challenges} 个${point.checkedIn ? '，已签到' : ''}`
}

export function TrainingActivityCalendar({
  activity,
  days = 91,
}: {
  activity: TrainingActivityPointModel[]
  days?: number
}) {
  const requested = activity.slice(-days)
  const remainder = requested.length % 7
  const recent = requested.length >= 7 && remainder ? requested.slice(remainder) : requested
  const periodLabel = days <= 31 ? '最近一个月' : days <= 93 ? '最近三个月' : days <= 124 ? '最近四个月' : '最近半年'

  return (
    <div className={styles.calendarViewport}>
      <div aria-label={`${periodLabel}学习活跃度`} className={styles.calendar} role="img">
        {recent.map((point, index) => (
          <span
            aria-label={label(point)}
            className={styles[`level${activityLevel(point)}`]}
            key={`${point.date ?? 'day'}-${index}`}
            title={label(point)}
          />
        ))}
      </div>
    </div>
  )
}
