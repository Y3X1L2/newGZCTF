import {
  TrainingCourseEnrollmentStatus,
  TrainingCourseModel,
  TrainingCourseProgressStatus,
  TrainingCourseStatus,
} from '@Api'

export type TrainingScope = 'all' | 'learning' | 'teaching' | 'available' | 'draft'

export const trainingScopes: Array<{ id: TrainingScope; label: string }> = [
  { id: 'all', label: '全部课程' },
  { id: 'learning', label: '正在学习' },
  { id: 'teaching', label: '授课管理' },
  { id: 'available', label: '可报名' },
  { id: 'draft', label: '草稿课程' },
]

export function courseProgress(course: TrainingCourseModel) {
  const total = course.totalChapterCount ?? course.chapterCount ?? 0
  const completed = Math.min(course.completedChapterCount ?? 0, total)
  return {
    completed,
    total,
    percent: total > 0 ? Math.round((completed / total) * 100) : 0,
  }
}

export function courseTeacherNames(course: TrainingCourseModel) {
  const names = (course.teachers ?? [])
    .map((teacher) => teacher.realName?.trim() || teacher.userName?.trim())
    .filter((name): name is string => Boolean(name))
  return names.length ? names.join('、') : '暂未指定教师'
}

export function courseStatusLabel(course: TrainingCourseModel) {
  if (course.canEdit) {
    if (course.status === TrainingCourseStatus.Draft) return '草稿课程'
    if (course.status === TrainingCourseStatus.Archived) return '已归档'
    return '授课管理'
  }
  if (course.enrollmentStatus === TrainingCourseEnrollmentStatus.Pending) return '等待审核'
  if (course.enrollmentStatus === TrainingCourseEnrollmentStatus.Rejected) return '报名未通过'
  if (course.enrollmentStatus === TrainingCourseEnrollmentStatus.Approved) {
    if (course.progressStatus === TrainingCourseProgressStatus.Completed) return '已完成'
    return '正在学习'
  }
  if (course.canLearn) return '可以学习'
  return '可以报名'
}

export function courseStatusTone(course: TrainingCourseModel): 'success' | 'info' | 'warning' | 'neutral' {
  if (course.status === TrainingCourseStatus.Archived) return 'neutral'
  if (
    course.status === TrainingCourseStatus.Draft ||
    course.enrollmentStatus === TrainingCourseEnrollmentStatus.Pending
  )
    return 'warning'
  if (course.canLearn || course.enrollmentStatus === TrainingCourseEnrollmentStatus.Approved) return 'success'
  return 'info'
}

export function matchesScope(course: TrainingCourseModel, scope: TrainingScope) {
  if (scope === 'learning') {
    return Boolean(
      course.canLearn &&
      !course.canEdit &&
      course.progressStatus !== TrainingCourseProgressStatus.Completed &&
      ((course.completedChapterCount ?? 0) > 0 || course.lastStudiedAt)
    )
  }
  if (scope === 'teaching') return Boolean(course.canEdit)
  if (scope === 'available') {
    return Boolean(
      !course.canEdit &&
      !course.canLearn &&
      course.status === TrainingCourseStatus.Published &&
      course.enrollmentStatus !== TrainingCourseEnrollmentStatus.Pending
    )
  }
  if (scope === 'draft') return course.status === TrainingCourseStatus.Draft
  return true
}

const trainingDateFormatter = new Intl.DateTimeFormat('zh-CN', {
  month: '2-digit',
  day: '2-digit',
  hour: '2-digit',
  minute: '2-digit',
  hour12: false,
})

export function formatTrainingDate(value?: number | null) {
  if (!value) return '暂无记录'
  return trainingDateFormatter.format(value)
}

export function formatFileSize(value?: number | null) {
  if (!value || value <= 0) return '外部资源'
  const units = ['B', 'KB', 'MB', 'GB']
  let size = value
  let index = 0
  while (size >= 1024 && index < units.length - 1) {
    size /= 1024
    index += 1
  }
  return `${size >= 10 || index === 0 ? size.toFixed(0) : size.toFixed(1)} ${units[index]}`
}
