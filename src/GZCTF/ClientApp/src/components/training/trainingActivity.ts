import type { TrainingActivityPointModel } from '@Utils/TrainingApi'

export interface TrainingContributionDay {
  key: string
  date: Date
  inRange: boolean
  activity?: TrainingActivityPointModel
  level: number
}

export interface TrainingContributionWeek {
  key: string
  days: TrainingContributionDay[]
}

export interface TrainingContributionMonthLabel {
  key: string
  label: string
  weekIndex: number
}

export interface TrainingContributionModel {
  weeks: TrainingContributionWeek[]
  monthLabels: TrainingContributionMonthLabel[]
  weekdayLabels: { index: number; label: string }[]
  totalActiveDays: number
  maxLevel: number
}

const MS_PER_DAY = 24 * 60 * 60 * 1000
const MONTH_LABELS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec']
const WEEKDAY_LABELS = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat']

const startOfLocalDay = (date: Date) => new Date(date.getFullYear(), date.getMonth(), date.getDate())

export const formatDateKey = (date: Date) => {
  const year = date.getFullYear()
  const month = String(date.getMonth() + 1).padStart(2, '0')
  const day = String(date.getDate()).padStart(2, '0')
  return `${year}-${month}-${day}`
}

export const parseDateKey = (value: string) => {
  const [year, month, day] = value.slice(0, 10).split('-').map(Number)
  if (!year || !month || !day) return startOfLocalDay(new Date(value))
  return new Date(year, month - 1, day)
}

export const addDays = (date: Date, days: number) => {
  const next = new Date(date)
  next.setDate(next.getDate() + days)
  return next
}

export const activityLevel = (point?: TrainingActivityPointModel) => {
  if (!point) return 0
  const score =
    (point.checkedIn ? 1 : 0) +
    Math.min(point.studyActions, 4) +
    point.completedChapters * 2 +
    point.acceptedChallenges * 3

  if (score <= 0) return 0
  if (score <= 1) return 1
  if (score <= 3) return 2
  if (score <= 6) return 3
  return 4
}

export const buildTrainingContributionModel = (
  activity: TrainingActivityPointModel[] = [],
  options?: { today?: Date; days?: number; compact?: boolean }
): TrainingContributionModel => {
  const today = startOfLocalDay(options?.today ?? new Date())
  const days = Math.max(1, Math.min(options?.days ?? 371, 371))
  const rangeStart = addDays(today, -(days - 1))
  const calendarStart = addDays(rangeStart, -rangeStart.getDay())
  const activityMap = new Map(activity.map((point) => [point.date.slice(0, 10), point]))
  const totalCalendarDays = Math.ceil((today.getTime() - calendarStart.getTime() + MS_PER_DAY) / MS_PER_DAY)
  const totalWeeks = Math.max(1, Math.ceil(totalCalendarDays / 7))

  const weeks: TrainingContributionWeek[] = []
  let maxLevel = 0
  let totalActiveDays = 0

  for (let weekIndex = 0; weekIndex < totalWeeks; weekIndex += 1) {
    const weekDays: TrainingContributionDay[] = []
    for (let weekday = 0; weekday < 7; weekday += 1) {
      const date = addDays(calendarStart, weekIndex * 7 + weekday)
      const key = formatDateKey(date)
      const inRange = date >= rangeStart && date <= today
      const dayActivity = inRange ? activityMap.get(key) : undefined
      const level = inRange ? activityLevel(dayActivity) : 0
      if (level > 0) totalActiveDays += 1
      maxLevel = Math.max(maxLevel, level)
      weekDays.push({ key, date, inRange, activity: dayActivity, level })
    }
    weeks.push({ key: formatDateKey(weekDays[0].date), days: weekDays })
  }

  const monthLabels: TrainingContributionMonthLabel[] = []
  let previousMonth = -1
  weeks.forEach((week, weekIndex) => {
    const firstInRange = week.days.find((day) => day.inRange)
    if (!firstInRange) return
    const month = firstInRange.date.getMonth()
    if (month === previousMonth) return
    previousMonth = month
    monthLabels.push({
      key: `${firstInRange.date.getFullYear()}-${month}`,
      label: MONTH_LABELS[month],
      weekIndex,
    })
  })

  return {
    weeks,
    monthLabels,
    weekdayLabels: options?.compact
      ? [
          { index: 1, label: 'Mon' },
          { index: 3, label: 'Wed' },
          { index: 5, label: 'Fri' },
        ]
      : WEEKDAY_LABELS.map((label, index) => ({ index, label })).filter((item) => item.index % 2 === 1),
    totalActiveDays,
    maxLevel,
  }
}

