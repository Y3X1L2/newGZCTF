import {
  Box,
  Button,
  Group,
  Modal,
  SimpleGrid,
  Stack,
  Text,
  TextInput,
  Textarea,
  Title,
} from '@mantine/core'
import { showNotification } from '@mantine/notifications'
import {
  mdiBookOpenPageVariantOutline,
  mdiChartTimelineVariant,
  mdiPlus,
  mdiSchoolOutline,
  mdiShieldSearch,
} from '@mdi/js'
import { Icon } from '@mdi/react'
import React, { FC, useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useNavigate } from 'react-router'
import { WithNavBar } from '@Components/WithNavbar'
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

const Training: FC = () => {
  const [courses, setCourses] = useState<TrainingCourseModel[]>([])
  const [overview, setOverview] = useState<TrainingPersonalOverviewModel | null>(null)
  const [createOpened, setCreateOpened] = useState(false)
  const [draft, setDraft] = useState<TrainingCourseEditModel>(emptyCourseDraft())
  const [saving, setSaving] = useState(false)
  const [checking, setChecking] = useState(false)
  const { user } = useUser()
  const { t } = useTranslation()
  const navigate = useNavigate()
  const canCreate = RequireRole(Role.Teacher, user?.role)

  const teachingCourses = useMemo(() => courses.filter((course) => course.canEdit), [courses])
  const learningCourses = useMemo(
    () =>
      courses
        .filter((course) => course.canLearn || course.progressStatus)
        .sort((a, b) => (b.updatedAt ?? 0) - (a.updatedAt ?? 0)),
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

  const load = async () => {
    try {
      const [courseRes, overviewRes] = await Promise.all([
        trainingCourseApi.courses(),
        trainingCourseApi.overview(),
      ])
      setCourses(courseRes.data)
      setOverview(overviewRes.data)
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
        title: draft.title.trim(),
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

  const checkIn = async () => {
    setChecking(true)
    try {
      const res = await trainingCourseApi.checkIn()
      setOverview(res.data)
      showNotification({ color: 'green', title: '签到完成', message: '今天的学习记录已写入概览。' })
    } catch (e) {
      showErrorMsg(e, t)
    } finally {
      setChecking(false)
    }
  }

  useEffect(() => {
    void load()
  }, [])

  const showcase = canCreate ? teachingCourses : learningCourses

  return (
    <WithNavBar width="min(100%, calc(100vw - 7.25rem))" minWidth={0}>
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
                <Text className="yy-training-readable" maw="62rem">
                  按课程路径学习知识点，在章节末尾直接完成容器实验和 Flag 提交。老师可以从同一入口维护课程、章节、资源、实验与报名审核。
                </Text>
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
                  <Text size="sm" className="yy-training-readable">
                    {canCreate ? '快速进入你负责的课程建设、章节维护和授课内容。' : '优先展示你最近学习或已经加入的课程。'}
                  </Text>
                </Stack>
                <TrainingStatusText tone="ongoing">{recent.length}/{Math.min(3, showcase.length || 3)}</TrainingStatusText>
              </Group>
              {recent.length ? (
                <SimpleGrid cols={{ base: 1, md: 2, lg: 3 }} spacing="md">
                  {recent.map((course) => (
                    <TrainingCourseCard key={course.id} course={course} featured actionLabel={canCreate ? '管理课程' : '继续学习'} />
                  ))}
                </SimpleGrid>
              ) : (
                <TrainingEmptyState
                  title={canCreate ? '还没有授课课程' : '还没有开始学习'}
                  description={
                    canCreate
                      ? '创建第一门课程后，可以继续配置章节、资源、实验和报名审核。'
                      : '报名通过或被老师分配课程后，会在这里显示继续学习入口。'
                  }
                />
              )}
            </section>

            <section id="ctf" className="yy-training-section">
              <Group justify="space-between" mb="md">
                <Stack gap={0}>
                  <Title order={3}>{canCreate ? '我教授的课程' : '今日待完成'}</Title>
                  <Text size="sm" className="yy-training-readable">
                    {canCreate ? '用于课程建设、章节维护和学员管理。' : '根据已加入课程的完成进度生成。'}
                  </Text>
                </Stack>
              </Group>
              <SimpleGrid cols={{ base: 1, md: 2, lg: 3 }} spacing="md" className="yy-training-course-grid">
                {(canCreate ? teachingCourses : todoCourses).slice(0, 6).map((course) => (
                  <TrainingCourseCard key={course.id} course={course} compact actionLabel={canCreate ? '编辑课程' : '继续'} />
                ))}
              </SimpleGrid>
              {(canCreate ? teachingCourses : todoCourses).length === 0 ? (
                <TrainingEmptyState
                  title={canCreate ? '暂无授课课程' : '暂无待完成课程'}
                  description={
                    canCreate
                      ? '可以从右上角创建课程，或让管理员将你加入课程教师列表。'
                      : '完成进度会在加入课程后自动显示。'
                  }
                />
              ) : null}
            </section>

            <section id="all" className="yy-training-section">
              <Group justify="space-between" mb="md">
                <Stack gap={0}>
                  <Title order={3}>全部课程</Title>
                  <Text size="sm" className="yy-training-readable">
                    共 {courses.length} 门课程，包含已加入、待审核、可报名和教师可管理课程。
                  </Text>
                </Stack>
              </Group>
              <SimpleGrid cols={{ base: 1, md: 2, lg: 3 }} spacing="md" className="yy-training-course-grid">
                {courses.map((course) => (
                  <TrainingCourseCard key={course.id} course={course} />
                ))}
              </SimpleGrid>
              {courses.length === 0 ? (
                <TrainingEmptyState title="暂无课程" description="老师创建并发布课程后，学生会在这里看到可学习或可报名的内容。" />
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
