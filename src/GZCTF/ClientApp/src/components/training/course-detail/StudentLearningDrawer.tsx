import { Accordion, Badge, Box, Button, Drawer, Group, Progress, ScrollArea, Stack, Table, Text, Title } from '@mantine/core'
import { mdiAccountMultipleOutline } from '@mdi/js'
import { Icon } from '@mdi/react'
import { ResponsiveTable } from '@Components/foundation'
import { YinyuPanel } from '@Components/yinyu/YinyuUI'
import { TrainingCourseStudentLearningDetailModel } from '@Utils/TrainingApi'
import {
  enrollmentStatusInfo,
  formatTime,
  optionIndexesText,
  percentOf,
  theoryScoreText,
} from './courseDetailModel'

type Props = {
  opened: boolean
  loading: boolean
  detail: TrainingCourseStudentLearningDetailModel | null
  onClose: () => void
}

export function StudentLearningDrawer({ opened, loading, detail, onClose }: Props) {
  return (
    <Drawer
      opened={opened}
      onClose={onClose}
      title="学员学习详情"
      position="right"
      size="min(72rem, calc(100vw - 1.5rem))"
      padding="lg"
      scrollAreaComponent={ScrollArea.Autosize}
      classNames={{
        content: 'yy-training-student-detail-drawer-content',
        header: 'yy-training-student-detail-drawer-header',
        title: 'yy-training-student-detail-drawer-title',
        body: 'yy-training-student-detail-drawer-body',
      }}
    >
      {loading ? (
        <Text c="dimmed">正在加载学习详情...</Text>
      ) : detail ? (
        <Stack gap="md">
          <YinyuPanel p="md">
            <Stack gap="sm">
              <Group justify="space-between" align="flex-start">
                <Stack gap={2}>
                  <Title order={3}>{detail.realName || detail.userName}</Title>
                  <Text size="sm" c="dimmed">
                    {detail.stdNumber || detail.userName}
                  </Text>
                </Stack>
                <Badge color={enrollmentStatusInfo(detail.enrollmentStatus).color} variant="light">
                  {enrollmentStatusInfo(detail.enrollmentStatus).label}
                </Badge>
              </Group>
              <Box className="yy-training-course-progress">
                <Group justify="space-between" mb={5}>
                  <Text size="xs" c="dimmed" fw={800}>
                    课程进度
                  </Text>
                  <Text size="xs" fw={950}>
                    {detail.completedChapterCount}/{detail.totalChapterCount}
                  </Text>
                </Group>
                <Progress
                  value={percentOf(detail.completedChapterCount, detail.totalChapterCount)}
                  radius="xl"
                  size="sm"
                  color="teal"
                />
              </Box>
              <Group gap="xs">
                <Badge variant="light">
                  实验 {detail.challengeSolvedCount}/{detail.challengeTotalCount}
                </Badge>
                <Badge variant="light">
                  理论 {detail.theorySubmittedCount}/{detail.theoryTotalCount}
                </Badge>
                <Badge variant="light">
                  理论得分 {detail.theoryScore}/{detail.theoryMaxScore}
                </Badge>
                <Badge variant="light">最后学习 {formatTime(detail.lastActivityAt)}</Badge>
              </Group>
            </Stack>
          </YinyuPanel>

          <Accordion multiple variant="separated">
            {detail.chapters.map((chapter) => (
              <Accordion.Item key={chapter.chapterId} value={String(chapter.chapterId)}>
                <Accordion.Control>
                  <Group justify="space-between" pr="md">
                    <Stack gap={2}>
                      <Text fw={900}>{chapter.title}</Text>
                      <Text size="xs" c="dimmed">
                        {chapter.summary || '暂无章节摘要'}
                      </Text>
                    </Stack>
                    <Badge color={chapter.completedAt ? 'teal' : 'gray'} variant="light">
                      {chapter.completedAt ? '已完成' : '未完成'}
                    </Badge>
                  </Group>
                </Accordion.Control>
                <Accordion.Panel>
                  <Stack gap="md">
                    {chapter.theory ? (
                      <YinyuPanel p="md">
                        <Stack gap="sm">
                          <Group justify="space-between">
                            <Stack gap={2}>
                              <Text fw={900}>{chapter.theory.title}</Text>
                              <Text size="xs" c="dimmed">
                                {chapter.theory.questionCount} 题 / {chapter.theory.totalScore} 分 / 得分{' '}
                                {theoryScoreText(chapter.theory.score, chapter.theory.totalScore)} / 及格线{' '}
                                {chapter.theory.passRate}%
                              </Text>
                            </Stack>
                            <Badge
                              color={chapter.theory.passed ? 'teal' : chapter.theory.status ? 'yellow' : 'gray'}
                              variant="light"
                            >
                              {chapter.theory.passed
                                ? `已通过 ${theoryScoreText(chapter.theory.score, chapter.theory.totalScore)}`
                                : chapter.theory.status
                                  ? theoryScoreText(chapter.theory.score, chapter.theory.totalScore)
                                  : '未提交'}
                            </Badge>
                          </Group>
                          {chapter.theory.answers.length > 0 ? (
                            <ResponsiveTable minWidth={760} label={`${chapter.title} 理论作答详情`}>
                              <Table verticalSpacing="sm">
                                <Table.Thead>
                                  <Table.Tr>
                                    <Table.Th>题目</Table.Th>
                                    <Table.Th>学生答案</Table.Th>
                                    <Table.Th>正确答案</Table.Th>
                                    <Table.Th>得分</Table.Th>
                                  </Table.Tr>
                                </Table.Thead>
                                <Table.Tbody>
                                  {chapter.theory.answers.map((answer) => (
                                    <Table.Tr key={answer.questionId}>
                                      <Table.Td>
                                        <Text fw={800} lineClamp={2}>
                                          {answer.title}
                                        </Text>
                                        <Text size="xs" c="dimmed">
                                          {answer.type}
                                        </Text>
                                      </Table.Td>
                                      <Table.Td>
                                        <Text size="sm" c={answer.isCorrect ? 'teal' : 'red'}>
                                          {optionIndexesText(answer.options, answer.selectedIndexes)}
                                        </Text>
                                      </Table.Td>
                                      <Table.Td>
                                        <Text size="sm">{optionIndexesText(answer.options, answer.answerIndexes)}</Text>
                                      </Table.Td>
                                      <Table.Td>
                                        {answer.score}/{answer.maxScore}
                                      </Table.Td>
                                    </Table.Tr>
                                  ))}
                                </Table.Tbody>
                              </Table>
                            </ResponsiveTable>
                          ) : (
                            <Text size="sm" c="dimmed">
                              学员尚未提交该测试。
                            </Text>
                          )}
                        </Stack>
                      </YinyuPanel>
                    ) : null}

                    {chapter.challenges.length > 0 ? (
                      <YinyuPanel p="md">
                        <Stack gap="sm">
                          <Group gap="xs">
                            <Icon path={mdiAccountMultipleOutline} size={0.9} />
                            <Text fw={900}>章节实验</Text>
                          </Group>
                          <ResponsiveTable minWidth={820} label={`${chapter.title} 实验完成详情`}>
                            <Table verticalSpacing="sm">
                              <Table.Thead>
                                <Table.Tr>
                                  <Table.Th>题目</Table.Th>
                                  <Table.Th>状态</Table.Th>
                                  <Table.Th>提交</Table.Th>
                                  <Table.Th>实例入口</Table.Th>
                                  <Table.Th>最后提交</Table.Th>
                                </Table.Tr>
                              </Table.Thead>
                              <Table.Tbody>
                                {chapter.challenges.map((challenge) => (
                                  <Table.Tr key={challenge.exerciseChallengeId}>
                                    <Table.Td>
                                      <Text fw={800}>{challenge.displayTitle || challenge.title}</Text>
                                      <Text size="xs" c="dimmed">
                                        {challenge.category} / {challenge.type}
                                      </Text>
                                    </Table.Td>
                                    <Table.Td>
                                      <Badge color={challenge.solved ? 'teal' : 'gray'} variant="light">
                                        {challenge.solved ? '已完成' : '未完成'}
                                      </Badge>
                                    </Table.Td>
                                    <Table.Td>
                                      {challenge.acceptedSubmissionCount}/{challenge.submissionCount}
                                    </Table.Td>
                                    <Table.Td>
                                      {challenge.instanceEntry &&
                                      (challenge.instanceEntry.startsWith('http') || challenge.instanceEntry.includes(':')) ? (
                                        <Button
                                          component="a"
                                          href={
                                            challenge.instanceEntry.startsWith('http')
                                              ? challenge.instanceEntry
                                              : `http://${challenge.instanceEntry}`
                                          }
                                          target="_blank"
                                          rel="noopener noreferrer"
                                          size="xs"
                                          variant="light"
                                        >
                                          打开
                                        </Button>
                                      ) : challenge.instanceEntry ? (
                                        <Text size="xs" ff="monospace" c="dimmed">
                                          {challenge.instanceEntry}
                                        </Text>
                                      ) : (
                                        <Text size="sm" c="dimmed">
                                          -
                                        </Text>
                                      )}
                                    </Table.Td>
                                    <Table.Td>
                                      <Text size="sm">{challenge.lastStatus ?? '-'}</Text>
                                      <Text size="xs" c="dimmed">
                                        {formatTime(challenge.lastSubmittedAt)}
                                      </Text>
                                    </Table.Td>
                                  </Table.Tr>
                                ))}
                              </Table.Tbody>
                            </Table>
                          </ResponsiveTable>
                        </Stack>
                      </YinyuPanel>
                    ) : (
                      <Text size="sm" c="dimmed">
                        本章节未配置实验题。
                      </Text>
                    )}
                  </Stack>
                </Accordion.Panel>
              </Accordion.Item>
            ))}
          </Accordion>
        </Stack>
      ) : (
        <Text c="dimmed">请选择学员查看详情。</Text>
      )}
    </Drawer>
  )
}
