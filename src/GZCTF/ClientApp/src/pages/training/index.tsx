import {
  Badge,
  Box,
  Button,
  Group,
  Modal,
  Progress,
  SimpleGrid,
  Stack,
  Text,
  TextInput,
  Textarea,
  Title,
} from '@mantine/core'
import { showNotification } from '@mantine/notifications'
import { mdiArrowRight, mdiBookOpenPageVariantOutline, mdiPlus } from '@mdi/js'
import { Icon } from '@mdi/react'
import React, { FC, useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useNavigate } from 'react-router'
import { WithNavBar } from '@Components/WithNavbar'
import { RequireRole } from '@Components/WithRole'
import { YinyuPanel, YinyuStatusPill } from '@Components/yinyu/YinyuUI'
import { useUser } from '@Hooks/useUser'
import { Role } from '@Api'
import { showErrorMsg } from '@Utils/Shared'
import {
  TrainingCourseEditModel,
  TrainingCourseEnrollmentPolicy,
  TrainingCourseEnrollmentStatus,
  TrainingCourseModel,
  TrainingCourseProgressStatus,
  TrainingCourseStatus,
  trainingCourseAdminApi,
  trainingCourseApi,
} from '@Utils/TrainingApi'

const emptyCourseDraft = (): TrainingCourseEditModel => ({
  title: '',
  slug: '',
  summary: '',
  description: '',
  coverFileHash: null,
  tags: [],
  enrollmentPolicy: TrainingCourseEnrollmentPolicy.TeacherApproval,
})

const courseStatusLabel = (course: TrainingCourseModel) => {
  if (course.status === TrainingCourseStatus.Draft) return '草稿'
  if (course.status === TrainingCourseStatus.Archived) return '已归档'
  if (course.canLearn) return '可学习'
  if (course.enrollmentStatus === TrainingCourseEnrollmentStatus.Pending) return '待审核'
  if (course.enrollmentStatus === TrainingCourseEnrollmentStatus.Rejected) return '未通过'
  return '可报名'
}

const progressValue = (course: TrainingCourseModel) => {
  if (!course.totalChapterCount) return course.progressStatus === TrainingCourseProgressStatus.Completed ? 100 : 0
  return Math.round((course.completedChapterCount / course.totalChapterCount) * 100)
}

const CourseCard: FC<{ course: TrainingCourseModel; compact?: boolean }> = ({ course, compact = false }) => (
  <Link className="yy-course-link" to={`/training/courses/${course.id}`}>
    <YinyuPanel className="yy-course-card" p="md">
      <Box
        className="yy-course-card-cover"
        style={course.coverUrl ? { backgroundImage: `url(${course.coverUrl})` } : undefined}
      >
        <Badge className="yy-gradient-status">{courseStatusLabel(course)}</Badge>
      </Box>
      <Stack gap={compact ? 6 : 'xs'} className="yy-course-card-body">
        <Group gap={6}>
          {(course.tags.length ? course.tags.slice(0, compact ? 2 : 4) : ['课程']).map((tag) => (
            <Badge key={tag} variant="light" color="teal">
              {tag}
            </Badge>
          ))}
        </Group>
        <Title order={compact ? 4 : 3}>{course.title}</Title>
        <Text size="sm" c="dimmed" lineClamp={compact ? 2 : 3}>
          {course.summary || '暂无课程摘要'}
        </Text>
        <Group justify="space-between" mt="xs">
          <Text size="xs" c="dimmed">
            {course.teachers.map((t) => t.realName || t.userName).join(' / ') || '未设置老师'}
          </Text>
          <Text size="xs" fw={900}>
            {course.completedChapterCount}/{course.totalChapterCount || course.chapterCount}
          </Text>
        </Group>
        <Progress value={progressValue(course)} radius="xl" color="teal" />
      </Stack>
    </YinyuPanel>
  </Link>
)

const Training: FC = () => {
  const [courses, setCourses] = useState<TrainingCourseModel[]>([])
  const [createOpened, setCreateOpened] = useState(false)
  const [draft, setDraft] = useState<TrainingCourseEditModel>(emptyCourseDraft())
  const [saving, setSaving] = useState(false)
  const { user } = useUser()
  const { t } = useTranslation()
  const navigate = useNavigate()
  const canCreate = RequireRole(Role.Teacher, user?.role)

  const highlighted = courses.slice(0, 4)
  const teachingCourses = useMemo(() => courses.filter((course) => course.canEdit), [courses])
  const learningCourses = useMemo(
    () =>
      courses
        .filter((course) => course.canLearn || course.progressStatus)
        .sort((a, b) => (b.updatedAt ?? 0) - (a.updatedAt ?? 0))
        .slice(0, 3),
    [courses]
  )
  const recent = canCreate ? teachingCourses.slice(0, 3) : learningCourses

  const load = async () => {
    try {
      const res = await trainingCourseApi.courses()
      setCourses(res.data)
    } catch (e) {
      showErrorMsg(e, t)
    }
  }

  const submitCreate = async () => {
    if (!draft.title.trim()) return
    setSaving(true)
    try {
      const res = await trainingCourseAdminApi.createCourse({
        ...draft,
        tags: draft.tags.length ? draft.tags : ['培训'],
      })
      showNotification({ color: 'green', title: '课程已创建', message: draft.title.trim() })
      setCreateOpened(false)
      setDraft(emptyCourseDraft())
      navigate(`/training/courses/${res.data.id}`)
    } catch (e) {
      showErrorMsg(e, t)
    } finally {
      setSaving(false)
    }
  }

  useEffect(() => {
    void load()
  }, [])

  return (
    <WithNavBar width="min(118rem, calc(100vw - 4rem))">
      <Stack gap="lg" className="yy-course-home">
        <Group justify="space-between" align="flex-end">
          <Stack gap={4}>
            <Text className="yy-section-kicker">隐域网安</Text>
            <Title order={1}>培训课程</Title>
          </Stack>
          {canCreate ? (
            <Button leftSection={<Icon path={mdiPlus} size={0.85} />} onClick={() => setCreateOpened(true)}>
              创建课程
            </Button>
          ) : null}
        </Group>

        <SimpleGrid cols={{ base: 1, lg: highlighted.length > 1 ? 2 : 1 }} spacing="md">
          {(highlighted.length ? highlighted : courses).slice(0, 2).map((course) => (
            <Link key={course.id} className="yy-course-link" to={`/training/courses/${course.id}`}>
              <YinyuPanel
                className="yy-course-hero"
                p="xl"
                style={course.coverUrl ? { backgroundImage: `url(${course.coverUrl})` } : undefined}
              >
                <Stack gap="sm">
                  <Group gap="xs">
                    <YinyuStatusPill tone={course.canLearn ? 'success' : 'neutral'}>{courseStatusLabel(course)}</YinyuStatusPill>
                    {course.tags.slice(0, 3).map((tag) => (
                      <Badge key={tag} variant="light" color="teal">
                        {tag}
                      </Badge>
                    ))}
                  </Group>
                  <Title order={2}>{course.title}</Title>
                  <Text c="dimmed" lineClamp={2}>
                    {course.summary || '暂无课程摘要'}
                  </Text>
                  <Group justify="space-between">
                    <Text size="sm">{course.teachers.map((teacher) => teacher.realName || teacher.userName).join(' / ')}</Text>
                    <Button variant="light" rightSection={<Icon path={mdiArrowRight} size={0.82} />}>
                      进入课程
                    </Button>
                  </Group>
                </Stack>
              </YinyuPanel>
            </Link>
          ))}
        </SimpleGrid>

        <YinyuPanel p="lg">
          <Group justify="space-between" mb="md">
            <Group gap="xs">
              <Icon path={mdiBookOpenPageVariantOutline} size={1} />
              <Title order={3}>{canCreate ? '我教授的课程' : '最近学习课程'}</Title>
            </Group>
            <Text size="sm" c="dimmed">
              {recent.length}/3
            </Text>
          </Group>
          <SimpleGrid cols={{ base: 1, md: 3 }} spacing="md">
            {recent.length ? (
              recent.map((course) => <CourseCard key={course.id} course={course} compact />)
            ) : (
              <Text c="dimmed">暂无课程</Text>
            )}
          </SimpleGrid>
        </YinyuPanel>

        <YinyuPanel p="lg">
          <Group justify="space-between" mb="md">
            <Title order={3}>全部课程</Title>
            <Text size="sm" c="dimmed">
              {courses.length} 门
            </Text>
          </Group>
          <SimpleGrid cols={{ base: 1, md: 2, xl: 3 }} spacing="md">
            {courses.map((course) => (
              <CourseCard key={course.id} course={course} />
            ))}
          </SimpleGrid>
        </YinyuPanel>
      </Stack>

      <Modal opened={createOpened} onClose={() => setCreateOpened(false)} title="创建课程" centered>
        <Stack>
          <TextInput
            label="课程名称"
            value={draft.title}
            onChange={(event) => setDraft({ ...draft, title: event.currentTarget.value })}
          />
          <TextInput
            label="课程标签"
            value={draft.tags.join('，')}
            onChange={(event) =>
              setDraft({
                ...draft,
                tags: event.currentTarget.value
                  .split(/[，,]/)
                  .map((tag) => tag.trim())
                  .filter(Boolean),
              })
            }
          />
          <Textarea
            label="课程摘要"
            minRows={3}
            value={draft.summary}
            onChange={(event) => setDraft({ ...draft, summary: event.currentTarget.value })}
          />
          <Textarea
            label="课程介绍"
            minRows={5}
            value={draft.description}
            onChange={(event) => setDraft({ ...draft, description: event.currentTarget.value })}
          />
          <Group justify="flex-end">
            <Button variant="subtle" onClick={() => setCreateOpened(false)}>
              取消
            </Button>
            <Button loading={saving} onClick={submitCreate}>
              保存
            </Button>
          </Group>
        </Stack>
      </Modal>
    </WithNavBar>
  )
}

export default Training
