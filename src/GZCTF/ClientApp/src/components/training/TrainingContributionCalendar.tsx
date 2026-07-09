import clsx from 'clsx'
import { memo, useMemo } from 'react'
import type { TrainingActivityPointModel } from '@Utils/TrainingApi'
import { buildTrainingContributionModel, formatDateKey } from './trainingActivity'

interface TrainingContributionCalendarProps {
  activity?: TrainingActivityPointModel[]
  compact?: boolean
  days?: number
  className?: string
}

const describeDay = (day: ReturnType<typeof buildTrainingContributionModel>['weeks'][number]['days'][number]) => {
  const point = day.activity
  const date = formatDateKey(day.date)
  if (!day.inRange) return `${date} 不在统计范围`
  if (!point || day.level === 0) return `${date} 无学习记录`

  const parts = [
    point.checkedIn ? '已签到' : '未签到',
    `${point.studyActions} 次学习动作`,
    `${point.completedChapters} 个章节完成`,
    `${point.acceptedChallenges} 次实验通过`,
  ]

  return `${date}：${parts.join('，')}`
}

const formatCompactDate = (date: Date) => `${date.getMonth() + 1}/${date.getDate()}`
const COMPACT_WEEKDAYS = ['周日', '周一', '周二', '周三', '周四', '周五', '周六']

export const TrainingContributionCalendar = memo(function TrainingContributionCalendar({
  activity = [],
  compact = false,
  days,
  className,
}: TrainingContributionCalendarProps) {
  const model = useMemo(
    () => buildTrainingContributionModel(activity, { days: days ?? (compact ? 42 : 371), compact }),
    [activity, compact, days]
  )
  const todayKey = useMemo(() => formatDateKey(new Date()), [])

  if (compact) {
    const compactDays = model.weeks
      .flatMap((week) => week.days)
      .filter((day) => day.inRange)
      .slice(-14)
      .reverse()

    return (
      <div
        className={clsx('yy-training-contribution', 'is-compact', className)}
        aria-label="近期学习活跃度"
      >
        <div className="yy-training-contribution-rail" role="list">
          {compactDays.map((day) => {
            const point = day.activity
            const isToday = day.key === todayKey
            return (
              <span
                key={day.key}
                className={clsx('yy-training-contribution-day', isToday && 'is-today', point?.checkedIn && 'is-checked')}
                title={describeDay(day)}
                aria-label={describeDay(day)}
                role="listitem"
              >
                <i className={`is-level-${day.level}`} />
                <b>{formatCompactDate(day.date)}</b>
                <em>{COMPACT_WEEKDAYS[day.date.getDay()]}</em>
                <strong>{point?.checkedIn ? '已签' : day.level > 0 ? '学习' : '--'}</strong>
              </span>
            )
          })}
        </div>
      </div>
    )
  }

  return (
    <div
      className={clsx('yy-training-contribution', className)}
      aria-label="近一年学习活跃度"
    >
      <div className="yy-training-contribution-scroll">
        <div
          className="yy-training-contribution-months"
          style={{ gridTemplateColumns: `repeat(${model.weeks.length}, var(--yy-training-day-size))` }}
          aria-hidden="true"
        >
          {model.monthLabels.map((month) => (
            <span key={month.key} style={{ gridColumnStart: month.weekIndex + 1 }}>
              {month.label}
            </span>
          ))}
        </div>

        <div className="yy-training-contribution-body">
          <div className="yy-training-contribution-weekdays" aria-hidden="true">
            {model.weekdayLabels.map((weekday) => (
              <span key={weekday.index} style={{ gridRowStart: weekday.index + 1 }}>
                {weekday.label}
              </span>
            ))}
          </div>

          <div
            className="yy-training-contribution-grid"
            style={{ gridTemplateColumns: `repeat(${model.weeks.length}, var(--yy-training-day-size))` }}
          >
            {model.weeks.flatMap((week) =>
              week.days.map((day) => {
                const isToday = day.key === todayKey
                return (
                  <span
                    key={day.key}
                    className={clsx(`is-level-${day.level}`, !day.inRange && 'is-outside', isToday && 'is-today')}
                    title={describeDay(day)}
                    aria-label={describeDay(day)}
                  />
                )
              })
            )}
          </div>
        </div>
      </div>

      {!compact ? (
        <div className="yy-training-contribution-footer">
          <span>{model.totalActiveDays} 天有学习记录</span>
          <div className="yy-training-contribution-legend" aria-hidden="true">
            <em>Less</em>
            {[0, 1, 2, 3, 4].map((level) => (
              <i key={level} className={`is-level-${level}`} />
            ))}
            <em>More</em>
          </div>
        </div>
      ) : null}
    </div>
  )
})
