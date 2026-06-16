import { Badge, Button, Checkbox, Group, Radio, SimpleGrid, Stack, Text, Title } from '@mantine/core'
import { showNotification } from '@mantine/notifications'
import { mdiArrowLeft, mdiCheck, mdiRefresh, mdiSendCheckOutline } from '@mdi/js'
import { Icon } from '@mdi/react'
import React, { FC, useEffect, useMemo, useState } from 'react'
import { Link, useParams } from 'react-router'
import { WithNavBar } from '@Components/WithNavbar'
import { YinyuGradientText } from '@Components/yinyu/YinyuReactBits'
import { YinyuPanel } from '@Components/yinyu/YinyuUI'
import { showErrorMsg } from '@Utils/Shared'
import { TheoryTrainingSessionModel, trainingApi } from '@Utils/TrainingApi'

const isMultiple = (type: string) => type === 'MultipleChoice'

const TrainingTheorySession: FC = () => {
  const { moduleId } = useParams()
  const id = Number(moduleId)
  const [session, setSession] = useState<TheoryTrainingSessionModel | null>(null)
  const [answers, setAnswers] = useState<Record<number, number[]>>({})
  const [loading, setLoading] = useState(false)

  const submitted = session?.status === 'Submitted'
  const questions = useMemo(() => [...(session?.questions ?? [])].sort((a, b) => a.order - b.order), [session])
  const answeredCount = questions.filter((q) => (answers[q.id]?.length ?? 0) > 0).length

  const load = async () => {
    if (!id) return
    setLoading(true)
    try {
      const res = await trainingApi.theorySession(id)
      setSession(res.data)
      setAnswers(Object.fromEntries(res.data.questions.map((q) => [q.id, q.selectedIndexes ?? []])))
    } catch (e) {
      showErrorMsg(e, (key) => key)
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    void load()
  }, [id])

  const regenerate = async () => {
    setLoading(true)
    try {
      const res = await trainingApi.regenerateTheorySession(id)
      setSession(res.data)
      setAnswers(Object.fromEntries(res.data.questions.map((q) => [q.id, []])))
      showNotification({ color: 'teal', message: '训练试卷已重新生成', icon: <Icon path={mdiCheck} size={1} /> })
    } catch (e) {
      showErrorMsg(e, (key) => key)
    } finally {
      setLoading(false)
    }
  }

  const submit = async () => {
    if (!session) return
    setLoading(true)
    try {
      const res = await trainingApi.submitTheorySession(session.id, {
        answers: Object.entries(answers).map(([questionId, selectedIndexes]) => ({
          questionId: Number(questionId),
          selectedIndexes,
        })),
      })
      setSession(res.data)
      setAnswers(Object.fromEntries(res.data.questions.map((q) => [q.id, q.selectedIndexes ?? []])))
      showNotification({ color: 'teal', message: '理论培训答卷已提交', icon: <Icon path={mdiCheck} size={1} /> })
    } catch (e) {
      showErrorMsg(e, (key) => key)
    } finally {
      setLoading(false)
    }
  }

  return (
    <WithNavBar width="var(--container)">
      <Stack gap="md">
        <Group justify="space-between">
          <Button component={Link} to="/training" variant="subtle" leftSection={<Icon path={mdiArrowLeft} size={0.86} />}>
            返回培训
          </Button>
          <Badge className="yy-gradient-status">理论培训</Badge>
        </Group>

        <YinyuPanel className="panel-card" p="md">
          <Group justify="space-between" align="start">
            <Stack gap={4}>
              <Title order={2}>理论训练试卷</Title>
              <Text c="dimmed">本试卷由老师配置的理论培训计划生成，提交后会更新你的培训进度。</Text>
              <Group gap="xs">
                <YinyuGradientText tone="signal" className="yy-theory-stat-gradient">
                  已答 {answeredCount}/{questions.length}
                </YinyuGradientText>
                {submitted && (
                  <YinyuGradientText tone={session?.passed ? 'ongoing' : 'danger'} className="yy-theory-stat-gradient">
                    得分 {session?.score ?? 0}/{session?.maxScore ?? 0} / 正确率 {session?.correctRate ?? 0}%
                  </YinyuGradientText>
                )}
              </Group>
            </Stack>
            <Group>
              <Button
                variant="light"
                disabled={loading}
                leftSection={<Icon path={mdiRefresh} size={0.86} />}
                onClick={regenerate}
              >
                重新生成
              </Button>
              <Button
                disabled={loading || submitted}
                leftSection={<Icon path={mdiSendCheckOutline} size={0.86} />}
                onClick={submit}
              >
                提交答卷
              </Button>
            </Group>
          </Group>
        </YinyuPanel>

        <SimpleGrid cols={{ base: 1, lg: 2 }} spacing="md">
          {questions.map((question, index) => (
            <YinyuPanel key={question.id} className="panel-card yy-theory-question-card" p="md">
              <Stack gap="sm">
                <Group justify="space-between" align="start">
                  <Stack gap={4}>
                    <Text fw={900}>
                      {index + 1}. {question.title}
                    </Text>
                    {question.content && (
                      <Text size="sm" c="dimmed">
                        {question.content}
                      </Text>
                    )}
                  </Stack>
                  <Badge className="yy-gradient-status">{question.score} 分</Badge>
                </Group>
                {isMultiple(question.type) ? (
                  <Checkbox.Group
                    value={(answers[question.id] ?? []).map(String)}
                    onChange={(values) =>
                      setAnswers((current) => ({
                        ...current,
                        [question.id]: values.map(Number).sort((a, b) => a - b),
                      }))
                    }
                  >
                    <Stack gap="xs">
                      {question.options.map((option, optionIndex) => (
                        <Checkbox key={optionIndex} disabled={submitted} value={String(optionIndex)} label={option} />
                      ))}
                    </Stack>
                  </Checkbox.Group>
                ) : (
                  <Radio.Group
                    value={(answers[question.id] ?? [])[0] !== undefined ? String((answers[question.id] ?? [])[0]) : null}
                    onChange={(value) =>
                      setAnswers((current) => ({
                        ...current,
                        [question.id]: value === null ? [] : [Number(value)],
                      }))
                    }
                  >
                    <Stack gap="xs">
                      {question.options.map((option, optionIndex) => (
                        <Radio key={optionIndex} disabled={submitted} value={String(optionIndex)} label={option} />
                      ))}
                    </Stack>
                  </Radio.Group>
                )}
                {submitted && question.isCorrect !== null && (
                  <Stack gap={4}>
                    <YinyuGradientText tone={question.isCorrect ? 'ongoing' : 'danger'} className="yy-theory-stat-gradient">
                      {question.isCorrect ? '回答正确' : '回答错误'}
                    </YinyuGradientText>
                    {question.answerIndexes && question.answerIndexes.length > 0 && (
                      <Text size="xs" c="dimmed">
                        正确答案：{question.answerIndexes.map((item) => question.options[item] ?? `选项 ${item + 1}`).join('、')}
                      </Text>
                    )}
                  </Stack>
                )}
              </Stack>
            </YinyuPanel>
          ))}
        </SimpleGrid>
      </Stack>
    </WithNavBar>
  )
}

export default TrainingTheorySession
