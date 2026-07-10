import {
  Box,
  Button,
  Group,
  Progress,
  Stack,
  Text,
  ThemeIcon,
  Title,
} from '@mantine/core'
import {
  mdiBookOpenPageVariantOutline,
  mdiChartLine,
  mdiCheckCircleOutline,
  mdiClockOutline,
  mdiFire,
  mdiSchoolOutline,
} from '@mdi/js'
import { Icon } from '@mdi/react'
import clsx from 'clsx'
import React, { memo, ReactNode, useMemo } from 'react'
import { Link } from 'react-router'
import { YinyuGradientText } from '@Components/yinyu/YinyuReactBits'
import { YinyuPanel } from '@Components/yinyu/YinyuUI'
import {
  TrainingActivityPointModel,
  TrainingCourseEnrollmentStatus,
  TrainingCourseModel,
  TrainingCourseProgressStatus,
  TrainingCourseStatus,
  TrainingPersonalOverviewModel,
} from '@Utils/TrainingApi'
import { TrainingContributionCalendar } from '@Components/training/TrainingContributionCalendar'

type TrainingTone = 'brand' | 'ongoing' | 'coming' | 'ended' | 'danger' | 'silver'

export const trainingCourseStatus = (course: TrainingCourseModel) => {
  if (course.status === TrainingCourseStatus.Draft) return { label: '草稿', tone: 'silver' as const }
  if (course.status === TrainingCourseStatus.Archived) return { label: '已归档', tone: 'ended' as const }
  if (course.canLearn) return { label: '可学习', tone: 'ongoing' as const }
  if (course.enrollmentStatus === TrainingCourseEnrollmentStatus.Pending) return { label: '待审核', tone: 'coming' as const }
  if (course.enrollmentStatus === TrainingCourseEnrollmentStatus.Rejected) return { label: '未通过', tone: 'danger' as const }
  return { label: '可报名', tone: 'brand' as const }
}

export const trainingCourseProgress = (course: TrainingCourseModel) => {
  const total = course.totalChapterCount || course.chapterCount || 0
  if (total <= 0) return course.progressStatus === TrainingCourseProgressStatus.Completed ? 100 : 0
  return Math.max(0, Math.min(100, Math.round((course.completedChapterCount / total) * 100)))
}

export const trainingTeacherNames = (course: TrainingCourseModel) =>
  course.teachers.map((teacher) => teacher.realName || teacher.userName).filter(Boolean).join(' / ') || '未设置老师'

export const trainingTags = (course: TrainingCourseModel, fallback = '培训') =>
  course.tags.length ? course.tags : [fallback]

export const TrainingStatusText = memo<{
  children: ReactNode
  tone?: TrainingTone
  className?: string
  vertical?: boolean
}>(({ children, tone = 'brand', className, vertical = false }) => (
  <YinyuGradientText tone={tone} className={clsx('yy-training-status-text', vertical && 'is-vertical', className)}>
    {children}
  </YinyuGradientText>
))

export const TrainingTagLine = memo<{ tags: string[]; max?: number; className?: string }>(function TrainingTagLine({
  tags,
  max = 4,
  className,
}) {
  const visibleTags = useMemo(() => tags.slice(0, max), [max, tags])

  return (
    <span className={clsx('yy-training-tag-line', className)}>
      {visibleTags.map((tag, index) => (
        <React.Fragment key={`${tag}-${index}`}>
          {index > 0 ? <span className="yy-training-tag-separator">/</span> : null}
          <span>{tag}</span>
        </React.Fragment>
      ))}
    </span>
  )
})

export const TrainingMetricTile = memo<{
  icon: string
  label: string
  value: ReactNode
  hint?: ReactNode
  tone?: TrainingTone
}>(({ icon, label, value, hint, tone = 'brand' }) => (
  <article className="yy-training-metric-tile">
    <ThemeIcon variant="light" radius="lg" size="lg" className={`yy-training-metric-icon is-${tone}`}>
      <Icon path={icon} size={0.86} />
    </ThemeIcon>
    <Stack gap={2} miw={0}>
      <Text size="xs" fw={800} className="yy-training-muted">
        {label}
      </Text>
      <Text fw={950} className="yy-training-metric-value">
        {value}
      </Text>
      {hint ? (
        <Text size="xs" lineClamp={1} className="yy-training-muted">
          {hint}
        </Text>
      ) : null}
    </Stack>
  </article>
))

export const TrainingCourseCard = memo<{
  course: TrainingCourseModel
  compact?: boolean
  featured?: boolean
  actionLabel?: string
  extraAction?: ReactNode
}>(({ course, compact = false, featured = false, actionLabel, extraAction }) => {
  const status = trainingCourseStatus(course)
  const progress = trainingCourseProgress(course)
  const total = course.totalChapterCount || course.chapterCount || 0
  const tags = trainingTags(course, course.canEdit ? '授课' : '课程')

  return (
    <Link className="yy-course-link" to={`/training/courses/${course.id}`}>
      <YinyuPanel className={clsx('yy-training-course-card', featured && 'is-featured', compact && 'is-compact')} p={0}>
        <div
          className="yy-training-course-cover"
          style={course.coverUrl ? { backgroundImage: `url(${course.coverUrl})` } : undefined}
        >
          <TrainingStatusText tone={status.tone} className="yy-training-course-status-badge">
            {status.label}
          </TrainingStatusText>
        </div>

        <Stack gap={compact ? 8 : 'sm'} className="yy-training-course-content">
          <Group justify="space-between" align="flex-start" gap="sm" wrap="nowrap">
            <Stack gap={4} miw={0}>
              <TrainingTagLine tags={tags} max={compact ? 2 : 4} />
              <Title order={featured ? 2 : compact ? 4 : 3} lineClamp={2}>
                {course.title}
              </Title>
            </Stack>
          </Group>

          <Text size={compact ? 'sm' : 'md'} lineClamp={2} className="yy-training-readable yy-training-course-summary">
            {course.summary || '暂无课程摘要。'}
          </Text>

          <div className="yy-training-course-meta-grid">
            <span>
              <Icon path={mdiSchoolOutline} size={0.72} />
              <b>{trainingTeacherNames(course)}</b>
            </span>
            <span>
              <Icon path={mdiBookOpenPageVariantOutline} size={0.72} />
              <b>{course.resourceCount} 份资源</b>
            </span>
          </div>

          <Box className="yy-training-course-progress">
            <Group justify="space-between" mb={6}>
              <Text size="xs" fw={850} className="yy-training-muted">
                章节进度
              </Text>
              <Text size="xs" fw={950}>
                {course.completedChapterCount}/{total}
              </Text>
            </Group>
            <Progress value={progress} radius="xl" size="sm" color="teal" />
          </Box>

          <Group justify="space-between" align="center" mt="auto" wrap="nowrap">
            <Text size="xs" className="yy-training-muted" lineClamp={1}>
              {course.challenges.length} 个实验 / {course.enrollmentCount} 名学员
            </Text>
            <Group gap={6} wrap="nowrap">
              {extraAction}
              <Button variant={featured ? 'filled' : 'light'} size={compact ? 'xs' : 'sm'}>
                {actionLabel ?? (course.canLearn ? '继续学习' : course.canEdit ? '管理课程' : '查看课程')}
              </Button>
            </Group>
          </Group>
        </Stack>
      </YinyuPanel>
    </Link>
  )
})

export const TrainingEmptyState = memo<{
  title: string
  description?: string
  action?: ReactNode
}>(({ title, description, action }) => (
  <YinyuPanel p="lg" className="yy-training-empty-state">
    <Stack align="center" gap="sm">
      <ThemeIcon size="xl" radius="xl" variant="light">
        <Icon path={mdiBookOpenPageVariantOutline} size={1.05} />
      </ThemeIcon>
      <Stack gap={4} ta="center">
        <Title order={4}>{title}</Title>
        {description ? (
          <Text size="sm" className="yy-training-readable">
            {description}
          </Text>
        ) : null}
      </Stack>
      {action}
    </Stack>
  </YinyuPanel>
))

export const TrainingProgressSummary = memo<{
  courses: TrainingCourseModel[]
  overview?: TrainingPersonalOverviewModel | null
  canCreate?: boolean
}>(({ courses, overview, canCreate = false }) => {
  const courseStats = useMemo(() => {
    let learningCount = 0
    let pendingCount = 0
    let editableCount = 0
    let completedCount = 0

    for (const course of courses) {
      if (course.canLearn || course.progressStatus) learningCount += 1
      if (course.canEdit) editableCount += 1
      if (course.enrollmentStatus === TrainingCourseEnrollmentStatus.Pending) pendingCount += 1
      if (course.progressStatus === TrainingCourseProgressStatus.Completed) completedCount += 1
    }

    return { learningCount, pendingCount, editableCount, completedCount }
  }, [courses])
  const average = overview?.averageProgress ?? 0

  return (
    <div className="yy-training-summary-grid">
      <TrainingMetricTile
        icon={mdiBookOpenPageVariantOutline}
        label={canCreate ? '授课课程' : '已加入课程'}
        value={canCreate ? courseStats.editableCount : (overview?.joinedCourseCount ?? courseStats.learningCount)}
        hint={`${overview?.visibleCourseCount ?? courses.length} 门可见课程`}
        tone="ongoing"
      />
      <TrainingMetricTile icon={mdiChartLine} label="平均进度" value={`${average}%`} hint="按可学习章节统计" tone="brand" />
      <TrainingMetricTile
        icon={mdiCheckCircleOutline}
        label="已完成"
        value={overview?.completedCourseCount ?? courseStats.completedCount}
        hint={`${overview?.completedChapterCount ?? 0}/${overview?.totalChapterCount ?? 0} 章节`}
        tone="ongoing"
      />
      <TrainingMetricTile
        icon={mdiClockOutline}
        label={canCreate ? '待审核报名' : '待审核'}
        value={courseStats.pendingCount}
        hint={canCreate ? '需要老师处理' : '等待老师通过'}
        tone={courseStats.pendingCount ? 'coming' : 'silver'}
      />
    </div>
  )
})

export const TrainingActivityHeatmap = memo<{ activity?: TrainingActivityPointModel[] }>(({ activity = [] }) => (
  <TrainingContributionCalendar activity={activity} compact />
))

export const TrainingOverviewPanel = memo<{
  overview?: TrainingPersonalOverviewModel | null
  todoCourses: TrainingCourseModel[]
}>(({ overview, todoCourses }) => {
  const ctfText = `${overview?.ctfSolvedChallenges ?? 0}/${overview?.ctfTotalChallenges ?? 0}`
  const theoryText = `${overview?.theoryPassedAssessments ?? 0}/${overview?.theoryTotalAssessments ?? 0}`

  return (
    <YinyuPanel p="lg" className="yy-training-insight-card">
      <Stack gap="md">
        <Stack gap={2}>
          <Text className="yy-section-kicker">Overview</Text>
          <Title order={3}>学习概览</Title>
        </Stack>

        <div className="yy-training-overview-rings">
          <div>
            <TrainingStatusText tone="ongoing">{overview?.averageProgress ?? 0}%</TrainingStatusText>
            <span>综合进度</span>
          </div>
          <div>
            <TrainingStatusText tone="brand">{ctfText}</TrainingStatusText>
            <span>CTF 实验</span>
          </div>
          <div>
            <TrainingStatusText tone="coming">{theoryText}</TrainingStatusText>
            <span>理论培训</span>
          </div>
        </div>

        <div className="yy-training-checkin-stats">
          <span>
            <b>{overview?.checkInDays ?? 0}</b>
            <em>累计打卡</em>
          </span>
          <span>
            <b>{overview?.currentCheckInStreak ?? 0}</b>
            <em>连续打卡</em>
          </span>
          <span>
            <b>{overview?.checkedInToday ? '已' : '未'}</b>
            <em>今日状态</em>
          </span>
        </div>

        <TrainingActivityHeatmap activity={overview?.activity ?? []} />

        <Stack gap="xs">
          {todoCourses.length ? (
            todoCourses.slice(0, 4).map((course) => {
              const status = trainingCourseStatus(course)
              return (
                <Link key={course.id} className="yy-training-task-row" to={`/training/courses/${course.id}`}>
                  <span>
                    <TrainingTagLine tags={trainingTags(course)} max={2} />
                    <strong>{course.title}</strong>
                  </span>
                  <TrainingStatusText tone={status.tone}>{trainingCourseProgress(course)}%</TrainingStatusText>
                </Link>
              )
            })
          ) : (
            <Text size="sm" className="yy-training-readable">暂无待完成课程。</Text>
          )}
        </Stack>
      </Stack>
    </YinyuPanel>
  )
})

export const TrainingCheckInCard = memo<{
  overview?: TrainingPersonalOverviewModel | null
  checking?: boolean
  onCheckIn: () => void
}>(({ overview, checking = false, onCheckIn }) => (
  <YinyuPanel p="md" className="yy-training-checkin-card">
    <Stack gap="xs">
      <Group gap="xs" wrap="nowrap">
        <Icon path={mdiFire} size={0.92} />
        <Text fw={950}>平台签到</Text>
      </Group>
      <div className="yy-training-checkin-mini">
        <span>
          <b>{overview?.checkInDays ?? 0}</b>
          <em>累计</em>
        </span>
        <span>
          <b>{overview?.currentCheckInStreak ?? 0}</b>
          <em>连续</em>
        </span>
      </div>
      <Stack gap={6}>
        <Text size="xs" fw={900} className="yy-training-muted">
          学习活跃趋势
        </Text>
        <TrainingContributionCalendar activity={overview?.activity ?? []} compact />
      </Stack>
      <Button variant={overview?.checkedInToday ? 'light' : 'filled'} loading={checking} disabled={overview?.checkedInToday} onClick={onCheckIn}>
        {overview?.checkedInToday ? '今日已签到' : '立即签到'}
      </Button>
    </Stack>
  </YinyuPanel>
))
