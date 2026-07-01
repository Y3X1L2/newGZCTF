import {
  Button,
  Box,
  Group,
  Stack,
  Text,
  TextInput,
  Title,
} from '@mantine/core'
import { showNotification } from '@mantine/notifications'
import { mdiArrowLeft, mdiCheck, mdiConsoleNetworkOutline, mdiDownloadOutline, mdiOpenInNew, mdiSend } from '@mdi/js'
import { Icon } from '@mdi/react'
import React, { FC, useEffect, useMemo, useState } from 'react'
import { Link, useParams } from 'react-router'
import { InstanceEntry } from '@Components/InstanceEntry'
import { Markdown } from '@Components/MarkdownRenderer'
import { WithNavBar } from '@Components/WithNavbar'
import { TrainingStatusText, TrainingTagLine } from '@Components/training/TrainingCourseUI'
import { YinyuGameBendsBackground } from '@Components/yinyu/YinyuReactBits'
import { YinyuPanel } from '@Components/yinyu/YinyuUI'
import { AnswerResult, ChallengeType, ClientFlagContext, ContainerInfoModel, EnvironmentType } from '@Api'
import { showErrorMsg } from '@Utils/Shared'
import { useTranslation } from 'react-i18next'
import {
  TrainingCourseChallengeModel,
  TrainingCourseChallengeDetailModel,
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
  const [challengeDetails, setChallengeDetails] = useState<Record<number, TrainingCourseChallengeDetailModel>>({})
  const [containers, setContainers] = useState<Record<number, ContainerInfoModel>>({})
  const [answers, setAnswers] = useState<Record<number, string>>({})
  const [completing, setCompleting] = useState(false)
  const [completionLocked, setCompletionLocked] = useState(false)
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
      setCompletionLocked(Boolean(chapterRes.data.completedAt))

      const detailEntries = await Promise.all(
        chapterRes.data.challenges.map(async (challenge) => {
          try {
            const detail = await trainingCourseApi.challenge(courseNum, challenge.exerciseChallengeId, chapterNum)
            return [challenge.exerciseChallengeId, detail.data] as const
          } catch {
            return null
          }
        })
      )

      const details = Object.fromEntries(
        detailEntries.filter((entry): entry is readonly [number, TrainingCourseChallengeDetailModel] => !!entry)
      )
      setChallengeDetails(details)

      setContainers(
        Object.fromEntries(
          Object.entries(details)
            .map(([challengeId, detail]) => [Number(challengeId), clientContextToContainerInfo(detail.context)] as const)
            .filter((entry): entry is readonly [number, ContainerInfoModel] => !!entry[1].entry)
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
      setCompleting(true)
      const res = await trainingCourseApi.completeChapter(courseNum, chapterNum)
      setChapter(res.data)
      setCompletionLocked(true)
      showNotification({ color: 'green', message: '章节已完成' })
      await load()
    } catch (e) {
      showErrorMsg(e, t)
    } finally {
      setCompleting(false)
    }
  }

  useEffect(() => {
    setCompletionLocked(false)
    void load()
  }, [courseId, chapterId])

  if (!course || !chapter) {
    return (
      <WithNavBar isLoading width="100%" minWidth={0}>
        <></>
      </WithNavBar>
    )
  }

  return (
    <WithNavBar width="100%" minWidth={0}>
      <Box className="yy-training-page yy-course-chapter-page">
        <YinyuGameBendsBackground className="yy-training-bg" />
        <YinyuPanel p="md" className="yy-course-chapter-side">
          <Stack gap="sm">
            <Button component={Link} to={`/training/courses/${course.id}`} variant="subtle" leftSection={<Icon path={mdiArrowLeft} size={0.85} />}>
              {course.title}
            </Button>
            {orderedChapters.map((item, index) => (
              <Link
                key={item.id}
                to={`/training/courses/${course.id}/chapters/${item.id}`}
                className={`yy-training-chapter-link ${item.id === chapter.id ? 'is-active' : ''}`}
              >
                <span>{index + 1}</span>
                <strong>{item.title}</strong>
                <em>{item.completedAt ? '已完成' : item.id === chapter.id ? '学习中' : '章节'}</em>
              </Link>
            ))}
          </Stack>
        </YinyuPanel>

        <Stack gap="md" className="yy-course-chapter-main">
          <YinyuPanel p="xl" className="yy-training-chapter-hero">
            <Stack gap="xs">
              <Group gap="md">
                <TrainingStatusText tone={chapter.completedAt ? 'ongoing' : 'brand'}>
                  {chapter.completedAt ? '已完成' : '学习中'}
                </TrainingStatusText>
                <TrainingTagLine tags={[course.title]} max={1} />
              </Group>
              <Title order={1}>{chapter.title}</Title>
              <Text c="dimmed">{chapter.summary || '本章节暂未填写摘要，请按正文内容完成学习任务。'}</Text>
            </Stack>
          </YinyuPanel>

          {chapter.videoProvider !== TrainingCourseVideoProvider.None ? (
            <YinyuPanel p="md" className="yy-training-video-panel">
              {chapter.videoProvider === TrainingCourseVideoProvider.ExternalUrl && chapter.videoUrl ? (
                <Button
                  component="a"
                  href={chapter.videoUrl}
                  target="_blank"
                  rel="noopener noreferrer"
                  rightSection={<Icon path={mdiOpenInNew} size={0.85} />}
                >
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
            <YinyuPanel p="lg" className="yy-training-lab-section">
              <Group justify="space-between" align="flex-end" mb="md">
                <Stack gap={2}>
                  <Group gap="xs">
                    <Icon path={mdiConsoleNetworkOutline} size={1} />
                    <Title order={3}>章节实验</Title>
                  </Group>
                  <Text size="sm" c="dimmed">
                    实验题直接嵌入章节末尾。创建容器后在当前页面复制入口、提交 Flag，正确后会同步章节进度。
                  </Text>
                </Stack>
                <TrainingStatusText tone="ongoing">{chapter.challenges.filter((item) => item.solved).length}/{chapter.challenges.length}</TrainingStatusText>
              </Group>
              <Stack gap="sm">
                {chapter.challenges.map((challenge) => {
                  const container = containers[challenge.exerciseChallengeId]
                  const detail = challengeDetails[challenge.exerciseChallengeId]
                  const attachmentUrl = detail?.context?.url
                  return (
                    <YinyuPanel key={challenge.exerciseChallengeId} p="md" className="yy-course-lab-card yy-training-lab-card">
                      <Stack gap="sm">
                        <Group justify="space-between" align="flex-start" gap="md">
                          <Stack gap={4} miw={0}>
                            <TrainingTagLine
                              tags={[challenge.category, challenge.type, challenge.isRequired ? '必做' : '选做']}
                              max={3}
                            />
                            <Title order={4}>{challenge.displayTitle || challenge.title}</Title>
                            {attachmentUrl ? (
                              <Button
                                component="a"
                                href={attachmentUrl}
                                target="_blank"
                                rel="noopener noreferrer"
                                variant="light"
                                size="xs"
                                w="fit-content"
                                leftSection={<Icon path={mdiDownloadOutline} size={0.75} />}
                              >
                                下载附件
                              </Button>
                            ) : challenge.hasAttachment ? (
                              <Text size="xs" c="dimmed">
                                附件正在加载或暂不可用
                              </Text>
                            ) : null}
                          </Stack>
                          <TrainingStatusText tone={challenge.solved ? 'ongoing' : 'silver'}>
                            {challenge.solved ? '已完成' : '待完成'}
                          </TrainingStatusText>
                        </Group>
                        {isContainerChallenge(challenge) ? (
                          <InstanceEntry
                            label={`${challenge.displayTitle || challenge.title} @ ${course.title}`}
                            context={containerInfoToClientContext(container)}
                            onCreate={() => createContainer(challenge.exerciseChallengeId)}
                            onExtend={() => extendContainer(challenge.exerciseChallengeId)}
                            onDestroy={() => destroyContainer(challenge.exerciseChallengeId)}
                          />
                        ) : null}
                        <Group>
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
                      </Stack>
                    </YinyuPanel>
                  )
                })}
              </Stack>
            </YinyuPanel>
          ) : null}

          {chapter.theoryPaper ? (
            <YinyuPanel p="lg">
              <Group justify="space-between" align="center">
                <Stack gap={4}>
                  <Group gap="xs">
                    <TrainingStatusText tone={chapter.theoryPaper.passed ? 'ongoing' : 'brand'}>
                      {chapter.theoryPaper.passed ? '已通过' : chapter.theoryPaper.status ? '已开始' : '课后测试'}
                    </TrainingStatusText>
                    <TrainingTagLine
                      tags={[
                        `${chapter.theoryPaper.questionCount} 题`,
                        `${chapter.theoryPaper.totalScore} 分`,
                        `及格线 ${chapter.theoryPaper.passRate}%`,
                      ]}
                      max={3}
                    />
                  </Group>
                  <Title order={3}>{chapter.theoryPaper.title}</Title>
                  <Text c="dimmed" size="sm">
                    {chapter.theoryPaper.score !== null && chapter.theoryPaper.score !== undefined
                      ? `当前得分：${chapter.theoryPaper.score}/${chapter.theoryPaper.totalScore}`
                      : '完成课后测试后，章节进度会自动刷新。'}
                  </Text>
                </Stack>
                {chapter.theoryPaper.isPublished ? (
                  <Button component={Link} to={`/training/courses/${course.id}/chapters/${chapter.id}/theory`}>
                    进入测试
                  </Button>
                ) : course.canEdit ? (
                  <Button
                    component={Link}
                    to={`/training/courses/${course.id}/chapters/${chapter.id}/theory-edit`}
                    variant="light"
                  >
                    配置测试
                  </Button>
                ) : null}
              </Group>
            </YinyuPanel>
          ) : null}

          <YinyuPanel p="lg">
            <Group justify="space-between">
              <Stack gap={2}>
                <Title order={3}>章节完成</Title>
                <Text c="dimmed" size="sm">
                  {chapter.challenges.length ? '完成本章节必做实验后会自动标记。普通阅读章节也可以手动标记完成。' : '普通章节可手动标记完成。'}
                </Text>
              </Stack>
              <Button
                leftSection={<Icon path={mdiCheck} size={0.85} />}
                onClick={complete}
                loading={completing}
                disabled={completionLocked || !!chapter.completedAt}
              >
                {completionLocked || chapter.completedAt ? '已完成' : '标记完成'}
              </Button>
            </Group>
          </YinyuPanel>
        </Stack>

        <YinyuPanel p="md" className="yy-course-toc yy-course-learning-status">
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
      </Box>
    </WithNavBar>
  )
}

export default ChapterDetail
