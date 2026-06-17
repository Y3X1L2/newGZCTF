import {
  Badge,
  Button,
  Group,
  SimpleGrid,
  Stack,
  Text,
  TextInput,
  Title,
} from '@mantine/core'
import { showNotification } from '@mantine/notifications'
import { mdiArrowLeft, mdiCheck, mdiConsoleNetworkOutline, mdiOpenInNew, mdiSend } from '@mdi/js'
import { Icon } from '@mdi/react'
import React, { FC, useEffect, useMemo, useState } from 'react'
import { Link, useParams } from 'react-router'
import { InstanceEntry } from '@Components/InstanceEntry'
import { Markdown } from '@Components/MarkdownRenderer'
import { WithNavBar } from '@Components/WithNavbar'
import { YinyuPanel, YinyuStatusPill } from '@Components/yinyu/YinyuUI'
import { AnswerResult, ChallengeType, ClientFlagContext, ContainerInfoModel, EnvironmentType } from '@Api'
import { showErrorMsg } from '@Utils/Shared'
import { useTranslation } from 'react-i18next'
import {
  TrainingCourseChallengeModel,
  TrainingCourseChapterModel,
  TrainingCourseModel,
  TrainingCourseVideoProvider,
  trainingCourseApi,
} from '@Utils/TrainingApi'

const headingsFromMarkdown = (source: string) =>
  source
    .split('\n')
    .map((line) => /^(#{1,4})\s+(.+)$/.exec(line.trim()))
    .filter(Boolean)
    .map((match) => ({
      level: match![1].length,
      title: match![2].replace(/[#*_`]/g, '').trim(),
    }))
    .slice(0, 18)

const isContainerChallenge = (challenge: TrainingCourseChallengeModel) =>
  (challenge.type === ChallengeType.StaticContainer || challenge.type === ChallengeType.DynamicContainer) &&
  challenge.environment !== EnvironmentType.WindowsVM

const containerInfoToClientContext = (container?: ContainerInfoModel): ClientFlagContext => ({
  instanceEntry: container?.entry || null,
  closeTime: container?.expectStopAt ?? null,
})

const clientContextToContainerInfo = (context: ClientFlagContext): ContainerInfoModel => ({
  entry: context.instanceEntry ?? '',
  expectStopAt: context.closeTime ?? undefined,
  startedAt: undefined,
  status: undefined,
})

const ChapterDetail: FC = () => {
  const { courseId, chapterId } = useParams()
  const courseNum = Number(courseId)
  const chapterNum = Number(chapterId)
  const [course, setCourse] = useState<TrainingCourseModel | null>(null)
  const [chapter, setChapter] = useState<TrainingCourseChapterModel | null>(null)
  const [containers, setContainers] = useState<Record<number, ContainerInfoModel>>({})
  const [answers, setAnswers] = useState<Record<number, string>>({})
  const { t } = useTranslation()

  const orderedChapters = useMemo(
    () => [...(course?.chapters ?? [])].sort((a, b) => a.order - b.order || a.id - b.id),
    [course?.chapters]
  )
  const toc = useMemo(() => headingsFromMarkdown(chapter?.content ?? ''), [chapter?.content])

  const load = async () => {
    if (!Number.isFinite(courseNum) || !Number.isFinite(chapterNum)) return
    try {
      const [courseRes, chapterRes] = await Promise.all([
        trainingCourseApi.course(courseNum),
        trainingCourseApi.chapter(courseNum, chapterNum),
      ])
      setCourse(courseRes.data)
      setChapter(chapterRes.data)

      const challengeEntries = await Promise.all(
        chapterRes.data.challenges.filter(isContainerChallenge).map(async (challenge) => {
          try {
            const detail = await trainingCourseApi.challenge(courseNum, challenge.exerciseChallengeId, chapterNum)
            return [challenge.exerciseChallengeId, clientContextToContainerInfo(detail.data.context)] as const
          } catch {
            return null
          }
        })
      )

      setContainers(
        Object.fromEntries(
          challengeEntries.filter(
            (entry): entry is readonly [number, ContainerInfoModel] => !!entry && !!entry[1].entry
          )
        )
      )
    } catch (e) {
      showErrorMsg(e, t)
    }
  }

  const createContainer = async (challengeId: number) => {
    try {
      const res = await trainingCourseApi.createContainer(courseNum, challengeId, chapterNum)
      setContainers((current) => ({ ...current, [challengeId]: res.data }))
      showNotification({ color: 'teal', message: '实例已创建' })
    } catch (e) {
      showErrorMsg(e, t)
    }
  }

  const extendContainer = async (challengeId: number) => {
    try {
      const res = await trainingCourseApi.extendContainer(courseNum, challengeId, chapterNum)
      setContainers((current) => ({ ...current, [challengeId]: res.data }))
      showNotification({ color: 'teal', message: '实例时间已延长' })
    } catch (e) {
      showErrorMsg(e, t)
    }
  }

  const destroyContainer = async (challengeId: number) => {
    try {
      await trainingCourseApi.destroyContainer(courseNum, challengeId, chapterNum)
      setContainers((current) => {
        const next = { ...current }
        delete next[challengeId]
        return next
      })
      showNotification({ color: 'teal', message: '实例已销毁' })
    } catch (e) {
      showErrorMsg(e, t)
    }
  }

  const submitFlag = async (challengeId: number) => {
    const flag = answers[challengeId]?.trim()
    if (!flag) return
    try {
      const res = await trainingCourseApi.submitFlag(courseNum, challengeId, { flag }, chapterNum)
      showNotification({
        color: res.data.status === AnswerResult.Accepted ? 'green' : 'red',
        message: res.data.status === AnswerResult.Accepted ? 'Flag 正确' : 'Flag 错误',
      })
      await load()
    } catch (e) {
      showErrorMsg(e, t)
    }
  }

  const complete = async () => {
    try {
      await trainingCourseApi.completeChapter(courseNum, chapterNum)
      showNotification({ color: 'green', message: '章节已完成' })
      await load()
    } catch (e) {
      showErrorMsg(e, t)
    }
  }

  useEffect(() => {
    void load()
  }, [courseId, chapterId])

  if (!course || !chapter) {
    return (
      <WithNavBar isLoading width="min(118rem, calc(100vw - 4rem))">
        <></>
      </WithNavBar>
    )
  }

  return (
    <WithNavBar width="min(118rem, calc(100vw - 4rem))">
      <SimpleGrid cols={{ base: 1, lg: 12 }} spacing="md" className="yy-course-chapter-page">
        <YinyuPanel p="md" className="yy-course-chapter-side">
          <Stack gap="sm">
            <Button component={Link} to={`/training/courses/${course.id}`} variant="subtle" leftSection={<Icon path={mdiArrowLeft} size={0.85} />}>
              {course.title}
            </Button>
            {orderedChapters.map((item) => (
              <Button
                key={item.id}
                component={Link}
                to={`/training/courses/${course.id}/chapters/${item.id}`}
                variant={item.id === chapter.id ? 'light' : 'subtle'}
                justify="flex-start"
                fullWidth
              >
                {item.title}
              </Button>
            ))}
          </Stack>
        </YinyuPanel>

        <Stack gap="md" className="yy-course-chapter-main">
          <YinyuPanel p="xl">
            <Stack gap="xs">
              <Group gap="xs">
                <YinyuStatusPill tone={chapter.completedAt ? 'success' : 'neutral'}>
                  {chapter.completedAt ? '已完成' : '学习中'}
                </YinyuStatusPill>
                <Badge variant="light" color="teal">
                  {course.title}
                </Badge>
              </Group>
              <Title order={1}>{chapter.title}</Title>
              <Text c="dimmed">{chapter.summary}</Text>
            </Stack>
          </YinyuPanel>

          {chapter.videoProvider !== TrainingCourseVideoProvider.None ? (
            <YinyuPanel p="md">
              {chapter.videoProvider === TrainingCourseVideoProvider.ExternalUrl && chapter.videoUrl ? (
                <Button component="a" href={chapter.videoUrl} target="_blank" rightSection={<Icon path={mdiOpenInNew} size={0.85} />}>
                  打开视频
                </Button>
              ) : chapter.videoFileUrl ? (
                <video className="yy-course-video" controls src={chapter.videoFileUrl} />
              ) : (
                <Text c="dimmed">视频资源未就绪</Text>
              )}
            </YinyuPanel>
          ) : null}

          <YinyuPanel p="xl" className="yy-course-markdown">
            <Markdown source={chapter.content || '暂无章节内容。'} />
          </YinyuPanel>

          {chapter.challenges.length ? (
            <YinyuPanel p="lg">
              <Group gap="xs" mb="md">
                <Icon path={mdiConsoleNetworkOutline} size={1} />
                <Title order={3}>实验题目</Title>
              </Group>
              <Stack gap="sm">
                {chapter.challenges.map((challenge) => {
                  const container = containers[challenge.exerciseChallengeId]
                  return (
                    <YinyuPanel key={challenge.exerciseChallengeId} p="md" className="yy-course-lab-card">
                      <Stack gap="sm">
                        <Stack gap={4}>
                          <Group gap="xs">
                            <Badge color={challenge.solved ? 'green' : 'teal'}>{challenge.solved ? '已完成' : challenge.category}</Badge>
                            <Badge variant="light">{challenge.type}</Badge>
                          </Group>
                          <Title order={4}>{challenge.displayTitle || challenge.title}</Title>
                        </Stack>
                        {isContainerChallenge(challenge) ? (
                          <InstanceEntry
                            label={`${challenge.displayTitle || challenge.title} @ ${course.title}`}
                            context={containerInfoToClientContext(container)}
                            onCreate={() => createContainer(challenge.exerciseChallengeId)}
                            onExtend={() => extendContainer(challenge.exerciseChallengeId)}
                            onDestroy={() => destroyContainer(challenge.exerciseChallengeId)}
                          />
                        ) : null}
                      </Stack>
                      <Group mt="sm">
                        <TextInput
                          placeholder="flag{...}"
                          value={answers[challenge.exerciseChallengeId] ?? ''}
                          onChange={(event) => {
                            const value = event.currentTarget.value
                            setAnswers((current) => ({
                              ...current,
                              [challenge.exerciseChallengeId]: value,
                            }))
                          }}
                          style={{ flex: 1 }}
                        />
                        <Button rightSection={<Icon path={mdiSend} size={0.82} />} onClick={() => submitFlag(challenge.exerciseChallengeId)}>
                          提交
                        </Button>
                      </Group>
                    </YinyuPanel>
                  )
                })}
              </Stack>
            </YinyuPanel>
          ) : null}

          <YinyuPanel p="lg">
            <Group justify="space-between">
              <Stack gap={2}>
                <Title order={3}>章节完成</Title>
                <Text c="dimmed" size="sm">
                  {chapter.challenges.length ? '完成本章节关联题目后会自动标记。' : '普通章节可手动标记完成。'}
                </Text>
              </Stack>
              <Button leftSection={<Icon path={mdiCheck} size={0.85} />} onClick={complete}>
                标记完成
              </Button>
            </Group>
          </YinyuPanel>
        </Stack>

        <YinyuPanel p="md" className="yy-course-toc">
          <Title order={4}>目录</Title>
          <Stack gap={4} mt="sm">
            {toc.length ? (
              toc.map((item, index) => (
                <Text key={`${item.title}-${index}`} size="sm" pl={(item.level - 1) * 10} c="dimmed">
                  {item.title}
                </Text>
              ))
            ) : (
              <Text size="sm" c="dimmed">
                暂无标题
              </Text>
            )}
          </Stack>
        </YinyuPanel>
      </SimpleGrid>
    </WithNavBar>
  )
}

export default ChapterDetail
