import {
  Box,
  Button,
  ActionIcon,
  Group,
  Modal,
  SimpleGrid,
  Stack,
  Switch,
  Text,
  TextInput,
  Textarea,
  Title,
} from '@mantine/core'
import { modals } from '@mantine/modals'
import { showNotification } from '@mantine/notifications'
import {
  mdiBookOpenPageVariantOutline,
  mdiArchiveOutline,
  mdiChartTimelineVariant,
  mdiPlus,
  mdiSchoolOutline,
  mdiShieldSearch,
  mdiTrashCanOutline,
} from '@mdi/js'
import { Icon } from '@mdi/react'
import React, { FC, useCallback, useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useNavigate } from 'react-router'
import useSWR from 'swr'
import { WithNavBar } from '@Components/WithNavbar'
import { DeferredGrid } from '@Components/foundation'
import { RequireRole } from '@Components/WithRole'
import {
  TrainingCheckInCard,
  TrainingCourseCard,
  TrainingEmptyState,
  TrainingProgressSummary,
  TrainingStatusText,
  trainingCourseProgress,
  trainingTags,
} from '@Components/training/TrainingCourseUI'
import { YinyuGameBendsBackground } from '@Components/yinyu/YinyuReactBits'
import { useUser } from '@Hooks/useUser'
import { Role } from '@Api'
import { showErrorMsg } from '@Utils/Shared'
import {
  TrainingCourseEditModel,
  TrainingCourseEnrollmentPolicy,
  TrainingCourseModel,
  TrainingCourseStatus,
  TrainingPersonalOverviewModel,
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

const courseMatches = (course: TrainingCourseModel, keywords: RegExp) =>
  trainingTags(course).some((tag) => keywords.test(tag)) || keywords.test(course.title)

const courseRecentTime = (course: TrainingCourseModel) =>
  course.lastStudiedAt ?? (course.progressStatus ? course.updatedAt : 0)

const trainingCoursesKey = 'training:home:courses'
const trainingOverviewKey = 'training:home:overview'

const fetchTrainingCourses = async () => {
  const res = await trainingCourseApi.courses()
  return res.data.filter((course) => course.status !== TrainingCourseStatus.Archived)
}

const fetchTrainingOverview = async () => {
  const res = await trainingCourseApi.overview()
  return res.data
}

const Training: FC = () => {
  const [createOpened, setCreateOpened] = useState(false)
  const [draft, setDraft] = useState<TrainingCourseEditModel>(emptyCourseDraft())
  const [saving, setSaving] = useState(false)
  const [checking, setChecking] = useState(false)
  const { user } = useUser()
  const { t } = useTranslation()
  const navigate = useNavigate()
  const canCreate = RequireRole(Role.Teacher, user?.role)
  const {
    data: courses = [],
    error: coursesError,
    mutate: mutateCourses,
  } = useSWR<TrainingCourseModel[]>(trainingCoursesKey, fetchTrainingCourses)
  const {
    data: overview = null,
    error: overviewError,
    mutate: mutateOverview,
  } = useSWR<TrainingPersonalOverviewModel | null>(trainingOverviewKey, fetchTrainingOverview)

  useEffect(() => {
    if (coursesError) showErrorMsg(coursesError, t)
  }, [coursesError, t])

  useEffect(() => {
    if (overviewError) showErrorMsg(overviewError, t)
  }, [overviewError, t])

  const reload = useCallback(async () => {
    await Promise.all([mutateCourses(), mutateOverview()])
  }, [mutateCourses, mutateOverview])

  const teachingCourses = useMemo(() => courses.filter((course) => course.canEdit), [courses])
  const learningCourses = useMemo(
    () =>
      courses
        .filter((course) => course.canLearn || course.progressStatus)
        .sort((a, b) => courseRecentTime(b) - courseRecentTime(a) || b.updatedAt - a.updatedAt),
    [courses]
  )
  const recent = useMemo(
    () => (canCreate ? teachingCourses : learningCourses).slice(0, 3),
    [canCreate, learningCourses, teachingCourses]
  )
  const ctfCourses = useMemo(
    () => courses.filter((course) => courseMatches(course, /ctf|web|misc|crypto|pwn|reverse|渗透|攻防/i)),
    [courses]
  )
  const theoryCourses = useMemo(
    () => courses.filter((course) => courseMatches(course, /理论|theory|考试|测验|基础/i)),
    [courses]
  )
  const todoCourses = useMemo(
    () => learningCourses.filter((course) => trainingCourseProgress(course) < 100).slice(0, 5),
    [learningCourses]
  )

  const submitCreate = useCallback(async () => {
    if (!draft.title.trim()) return
    setSaving(true)
    try {
      const res = await trainingCourseAdminApi.createCourse({
        ...draft,
        title: draft.title.trim(),
        tags: draft.tags.length ? draft.tags : ['培训'],
      })
      showNotification({ color: 'green', title: '课程已创建', message: draft.title.trim() })
      setCreateOpened(false)
      setDraft(emptyCourseDraft())
      await reload()
      navigate(`/training/courses/${res.data.id}`)
    } catch (e) {
      showErrorMsg(e, t)
    } finally {
      setSaving(false)
    }
  }, [draft, navigate, reload, t])

  const checkIn = useCallback(async () => {
    setChecking(true)
    try {
      const res = await trainingCourseApi.checkIn()
      await mutateOverview(res.data, { revalidate: false })
      showNotification({ color: 'green', title: '签到完成', message: '今天的学习记录已写入概览。' })
    } catch (e) {
      showErrorMsg(e, t)
    } finally {
      setChecking(false)
    }
  }, [mutateOverview, t])

  const archiveCourse = useCallback(async (course: TrainingCourseModel) => {
    try {
      await trainingCourseAdminApi.archive(course.id)
      showNotification({ color: 'orange', title: '课程已归档', message: course.title })
      await reload()
    } catch (e) {
      showErrorMsg(e, t)
    }
  }, [reload, t])

  const deleteCourse = useCallback(async (course: TrainingCourseModel) => {
    modals.openConfirmModal({
      title: '删除课程',
      children: (
        <Text size="sm">
          确认删除「{course.title}」？课程内章节、报名、学习记录和课程专属题目会被删除，环境模板只会解绑。
        </Text>
      ),
      labels: { confirm: '删除', cancel: '取消' },
      confirmProps: { color: 'red' },
      onConfirm: async () => {
        try {
          await trainingCourseAdminApi.deleteCourse(course.id)
          showNotification({ color: 'green', title: '课程已删除', message: course.title })
          await reload()
        } catch (e) {
          showErrorMsg(e, t)
        }
      },
    })
  }, [reload, t])

  const courseCardAction = useCallback((course: TrainingCourseModel) => {
    if (!canCreate) return null
    const actions = []

    if (course.canEdit && course.status === TrainingCourseStatus.Draft) {
      actions.push(
        <ActionIcon
          key="archive"
          variant="light"
          color="orange"
          aria-label="归档课程"
          title="归档课程"
          onClick={(event) => {
            event.preventDefault()
            event.stopPropagation()
            void archiveCourse(course)
          }}
        >
          <Icon path={mdiArchiveOutline} size={0.78} />
        </ActionIcon>
      )
    }

    if (course.canDelete) {
      actions.push(
        <ActionIcon
          key="delete"
          variant="light"
          color="red"
          aria-label="删除课程"
          title="删除课程"
          onClick={(event) => {
            event.preventDefault()
            event.stopPropagation()
            void deleteCourse(course)
          }}
        >
          <Icon path={mdiTrashCanOutline} size={0.78} />
        </ActionIcon>
      )
    }

    return actions.length ? <Group gap={6}>{actions}</Group> : null
  }, [archiveCourse, canCreate, deleteCourse])

  const showcase = canCreate ? teachingCourses : learningCourses

  return (
    <WithNavBar width="var(--container)" minWidth={0}>
      <Box className="yy-training-page yy-course-home">
        <YinyuGameBendsBackground className="yy-training-bg" />
        <section className="yy-training-shell">
          <aside className="yy-training-sidebar">
            <Stack gap="md">
              <Stack gap={4}>
                <Text className="yy-section-kicker">Training</Text>
                <Title order={2}>学习导航</Title>
              </Stack>
              <nav className="yy-training-nav-list" aria-label="培训导航">
                <a href="#continue">
                  <Icon path={mdiBookOpenPageVariantOutline} size={0.9} />
                  <span>{canCreate ? '教学入口' : '继续学习'}</span>
                  <b>{recent.length}</b>
                </a>
                <a href="#ctf">
                  <Icon path={mdiShieldSearch} size={0.9} />
                  <span>CTF 培训</span>
                  <b>{ctfCourses.length}</b>
                </a>
                <a href="#theory">
                  <Icon path={mdiSchoolOutline} size={0.9} />
                  <span>理论培训</span>
                  <b>{theoryCourses.length}</b>
                </a>
                <a href="#all">
                  <Icon path={mdiChartTimelineVariant} size={0.9} />
                  <span>全部课程</span>
                  <b>{courses.length}</b>
                </a>
              </nav>
              <TrainingCheckInCard overview={overview} checking={checking} onCheckIn={checkIn} />
            </Stack>
          </aside>

          <main className="yy-training-main">
            <Group justify="space-between" align="flex-end" className="yy-training-hero-head">
              <Stack gap={6}>
                <Title order={1}>培训课程</Title>
              </Stack>
              {canCreate ? (
                <Button leftSection={<Icon path={mdiPlus} size={0.85} />} onClick={() => setCreateOpened(true)}>
                  创建课程
                </Button>
              ) : null}
            </Group>

            <TrainingProgressSummary courses={courses} overview={overview} canCreate={canCreate} />

            <section id="continue" className="yy-training-section">
              <Group justify="space-between" mb="md" align="flex-end">
                <Stack gap={0}>
                  <Title order={3}>{canCreate ? '教学入口' : '继续学习'}</Title>
                </Stack>
                <TrainingStatusText tone="ongoing">{recent.length}/{Math.min(3, showcase.length || 3)}</TrainingStatusText>
              </Group>
              {recent.length ? (
                <DeferredGrid cols={{ base: 1, md: 2, lg: 3 }} spacing="md" className="yy-training-course-grid">
                  {recent.map((course) => (
                    <TrainingCourseCard
                      key={course.id}
                      course={course}
                      featured
                      actionLabel={canCreate ? '管理课程' : '继续学习'}
                      extraAction={courseCardAction(course)}
                    />
                  ))}
                </DeferredGrid>
              ) : (
                <TrainingEmptyState
                  title={canCreate ? '还没有授课课程' : '还没有开始学习'}
                />
              )}
            </section>

            <section id="ctf" className="yy-training-section">
              <Group justify="space-between" mb="md">
                <Stack gap={0}>
                  <Title order={3}>{canCreate ? '我教授的课程' : '今日待完成'}</Title>
                </Stack>
              </Group>
              <DeferredGrid cols={{ base: 1, md: 2, lg: 3 }} spacing="md" className="yy-training-course-grid">
                {(canCreate ? teachingCourses : todoCourses).slice(0, 6).map((course) => (
                  <TrainingCourseCard
                    key={course.id}
                    course={course}
                    compact
                    actionLabel={canCreate ? '编辑课程' : '继续'}
                    extraAction={courseCardAction(course)}
                  />
                ))}
              </DeferredGrid>
              {(canCreate ? teachingCourses : todoCourses).length === 0 ? (
                <TrainingEmptyState
                  title={canCreate ? '暂无授课课程' : '暂无待完成课程'}
                />
              ) : null}
            </section>

            <section id="all" className="yy-training-section">
              <Group justify="space-between" mb="md">
                <Stack gap={0}>
                  <Title order={3}>全部课程</Title>
                </Stack>
              </Group>
              <DeferredGrid cols={{ base: 1, md: 2, lg: 3 }} spacing="md" className="yy-training-course-grid">
                {courses.map((course) => (
                  <TrainingCourseCard key={course.id} course={course} extraAction={courseCardAction(course)} />
                ))}
              </DeferredGrid>
              {courses.length === 0 ? (
                <TrainingEmptyState title="暂无课程" />
              ) : null}
            </section>
          </main>
        </section>
      </Box>

      <Modal opened={createOpened} onClose={() => setCreateOpened(false)} title="创建课程" centered size="lg">
        <Stack>
          <TextInput
            label="课程名称"
            value={draft.title}
            onChange={(event) => setDraft({ ...draft, title: event.currentTarget.value })}
          />
          <TextInput
            label="课程标签"
            value={draft.tags.join('；')}
            onChange={(event) =>
              setDraft({
                ...draft,
                tags: event.currentTarget.value
                  .split(/[；;,]/)
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
            label="课程介绍 Markdown"
            minRows={6}
            value={draft.description}
            onChange={(event) => setDraft({ ...draft, description: event.currentTarget.value })}
          />
          <Switch
            label="课程报名审核"
            checked={draft.enrollmentPolicy === TrainingCourseEnrollmentPolicy.TeacherApproval}
            onChange={(event) =>
              setDraft({
                ...draft,
                enrollmentPolicy: event.currentTarget.checked
                  ? TrainingCourseEnrollmentPolicy.TeacherApproval
                  : TrainingCourseEnrollmentPolicy.AutoApprove,
              })
            }
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
