import {
  Alert,
  Badge,
  Button,
  Card,
  Checkbox,
  Group,
  Modal,
  Radio,
  Stack,
  Text,
  Title,
} from '@mantine/core'
import { showNotification } from '@mantine/notifications'
import { mdiCheck, mdiContentSaveOutline, mdiSendCheckOutline } from '@mdi/js'
import { Icon } from '@mdi/react'
import { FC, useEffect, useMemo, useState } from 'react'
import { useParams } from 'react-router'
import { Empty } from '@Components/Empty'
import { WithGameTab } from '@Components/WithGameTab'
import { WithNavBar } from '@Components/WithNavbar'
import { showErrorMsg } from '@Utils/Shared'
import {
  theoryPlayerApi,
  TheoryAnswerModel,
  TheoryAnswerSheetStatus,
  TheoryPlayerPaperModel,
  TheoryPlayerQuestionModel,
  TheoryQuestionType,
} from '../../../Api/TheoryApi'

const questionTypeLabel = (type: TheoryQuestionType) =>
  type === TheoryQuestionType.MultipleChoice ? '多选题' : type === TheoryQuestionType.TrueFalse ? '判断题' : '单选题'

const TheoryQuestionCard: FC<{
  question: TheoryPlayerQuestionModel
  selected: number[]
  disabled: boolean
  onChange: (selected: number[]) => void
}> = ({ question, selected, disabled, onChange }) => {
  const isMultiple = question.type === TheoryQuestionType.MultipleChoice

  return (
    <Card withBorder radius="sm">
      <Stack gap="sm">
        <Group justify="space-between" align="flex-start">
          <Stack gap={4}>
            <Group gap="xs">
              <Badge variant="light">{questionTypeLabel(question.type)}</Badge>
              <Badge color="teal" variant="light">
                {question.score} 分
              </Badge>
            </Group>
            <Title order={4}>{question.title}</Title>
          </Stack>
        </Group>
        {question.content && <Text c="dimmed">{question.content}</Text>}

        {isMultiple ? (
          <Checkbox.Group value={selected.map(String)} onChange={(values) => onChange(values.map(Number).sort((a, b) => a - b))}>
            <Stack gap="xs">
              {question.options.map((option, index) => (
                <Checkbox key={index} disabled={disabled} value={String(index)} label={option} />
              ))}
            </Stack>
          </Checkbox.Group>
        ) : (
          <Radio.Group value={selected[0] !== undefined ? String(selected[0]) : null} onChange={(value) => onChange([Number(value)])}>
            <Stack gap="xs">
              {question.options.map((option, index) => (
                <Radio key={index} disabled={disabled} value={String(index)} label={option} />
              ))}
            </Stack>
          </Radio.Group>
        )}
      </Stack>
    </Card>
  )
}

const TheoryPage: FC = () => {
  const { id } = useParams()
  const numId = parseInt(id ?? '-1')

  const [paper, setPaper] = useState<TheoryPlayerPaperModel>()
  const [answers, setAnswers] = useState<Record<number, number[]>>({})
  const [loading, setLoading] = useState(false)
  const [errorText, setErrorText] = useState<string>()
  const [confirmOpened, setConfirmOpened] = useState(false)

  const submitted = paper?.status === TheoryAnswerSheetStatus.Submitted
  const answeredCount = useMemo(
    () => paper?.questions.filter((q) => (answers[q.id]?.length ?? 0) > 0).length ?? 0,
    [answers, paper]
  )

  const toAnswerModel = (): TheoryAnswerModel[] =>
    Object.entries(answers).map(([paperQuestionId, selectedIndexes]) => ({
      paperQuestionId: Number(paperQuestionId),
      selectedIndexes,
    }))

  const loadPaper = async () => {
    if (numId < 0) return
    setLoading(true)
    setErrorText(undefined)
    try {
      const res = await theoryPlayerApi.getPaper(numId)
      setPaper(res.data)
      setAnswers(
        Object.fromEntries((res.data.answers ?? []).map((answer) => [answer.paperQuestionId, answer.selectedIndexes]))
      )
    } catch (err) {
      setErrorText('理论试卷尚未发放或当前账号暂无访问权限。')
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    loadPaper()
  }, [numId])

  const saveDraft = async () => {
    if (!paper || submitted) return
    setLoading(true)
    try {
      const res = await theoryPlayerApi.saveDraft(numId, { answers: toAnswerModel() })
      setPaper(res.data)
      showNotification({ color: 'teal', message: '草稿已保存', icon: <Icon path={mdiCheck} size={1} /> })
    } catch (err) {
      showErrorMsg(err, (key) => key)
    } finally {
      setLoading(false)
    }
  }

  const submit = async () => {
    if (!paper || submitted) return
    setConfirmOpened(false)
    setLoading(true)
    try {
      const res = await theoryPlayerApi.submit(numId, { answers: toAnswerModel() })
      setPaper(res.data)
      showNotification({ color: 'teal', message: '答卷已提交', icon: <Icon path={mdiCheck} size={1} /> })
    } catch (err) {
      showErrorMsg(err, (key) => key)
    } finally {
      setLoading(false)
    }
  }

  const confirmSubmit = () => setConfirmOpened(true)

  return (
    <WithNavBar minWidth={0} isLoading={loading} withFooter>
      <WithGameTab>
        <Stack gap="md">
          {errorText && <Empty description={errorText} />}
          {paper && (
            <>
              <Card withBorder radius="sm">
                <Group justify="space-between" align="flex-start">
                  <Stack gap={4}>
                    <Title order={3}>{paper.title}</Title>
                    {paper.description && <Text c="dimmed">{paper.description}</Text>}
                    <Group gap="xs">
                      <Badge variant="light">
                        {answeredCount} / {paper.questions.length} 已作答
                      </Badge>
                      <Badge color="teal" variant="light">
                        总分 {paper.totalScore}
                      </Badge>
                      {submitted && (
                        <Badge color="green" variant="light">
                          得分 {paper.score} / {paper.totalScore}
                        </Badge>
                      )}
                    </Group>
                  </Stack>
                  <Group>
                    <Button
                      variant="outline"
                      disabled={submitted || loading}
                      leftSection={<Icon path={mdiContentSaveOutline} size={1} />}
                      onClick={saveDraft}
                    >
                      保存草稿
                    </Button>
                    <Button
                      disabled={submitted || loading}
                      leftSection={<Icon path={mdiSendCheckOutline} size={1} />}
                      onClick={confirmSubmit}
                    >
                      提交答卷
                    </Button>
                  </Group>
                </Group>
              </Card>

              <Modal
                opened={confirmOpened && !submitted}
                onClose={() => setConfirmOpened(false)}
                title="确认提交答卷"
                centered
              >
                <Stack gap="md">
                  <Text size="sm">提交后不可修改，系统会立即判分并计入理论排行榜。</Text>
                  <Group justify="flex-end">
                    <Button variant="default" disabled={loading} onClick={() => setConfirmOpened(false)}>
                      取消
                    </Button>
                    <Button color="teal" loading={loading} onClick={submit}>
                      确认提交
                    </Button>
                  </Group>
                </Stack>
              </Modal>

              {submitted && (
                <Alert color="teal" icon={<Icon path={mdiCheck} />}>
                  答卷已提交，当前成绩已计入理论排行榜。
                </Alert>
              )}

              <Stack gap="md">
                {paper.questions.map((question) => (
                  <TheoryQuestionCard
                    key={question.id}
                    question={question}
                    selected={answers[question.id] ?? []}
                    disabled={submitted || loading}
                    onChange={(selected) => setAnswers({ ...answers, [question.id]: selected })}
                  />
                ))}
              </Stack>
            </>
          )}
        </Stack>
      </WithGameTab>
    </WithNavBar>
  )
}

export default TheoryPage
