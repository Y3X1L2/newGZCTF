import {
  Alert,
  Badge,
  Button,
  Checkbox,
  Grid,
  Group,
  Modal,
  Radio,
  SimpleGrid,
  Stack,
  Text,
  Title,
  Tooltip,
  UnstyledButton,
} from '@mantine/core'
import { showNotification } from '@mantine/notifications'
import { mdiArrowLeftBold, mdiArrowRightBold, mdiCheck, mdiContentSaveOutline, mdiSendCheckOutline } from '@mdi/js'
import { Icon } from '@mdi/react'
import { CSSProperties, FC, useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react'
import { useParams } from 'react-router'
import { WithGameTab } from '@Components/WithGameTab'
import { WithNavBar } from '@Components/WithNavbar'
import { YinyuGradientText, YinyuTextType } from '@Components/yinyu/YinyuReactBits'
import { YinyuHeartbeatIcon, YinyuModalBody, YinyuPanel, YinyuStatePage } from '@Components/yinyu/YinyuUI'
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

const questionTypeShort = (type: TheoryQuestionType) =>
  type === TheoryQuestionType.MultipleChoice ? '多' : type === TheoryQuestionType.TrueFalse ? '判' : '单'

const questionTypeTone = (type: TheoryQuestionType) =>
  type === TheoryQuestionType.MultipleChoice ? 'multiple' : type === TheoryQuestionType.TrueFalse ? 'judge' : 'single'

const TheoryQuestionCard: FC<{
  question: TheoryPlayerQuestionModel
  selected: number[]
  disabled: boolean
  onChange: (selected: number[]) => void
}> = ({ question, selected, disabled, onChange }) => {
  const isMultiple = question.type === TheoryQuestionType.MultipleChoice
  const titleBoxRef = useRef<HTMLDivElement>(null)
  const titleMeasureRef = useRef<HTMLSpanElement>(null)
  const [titleScale, setTitleScale] = useState(1)

  useLayoutEffect(() => {
    const updateTitleScale = () => {
      const box = titleBoxRef.current
      const measure = titleMeasureRef.current
      if (!box || !measure) return

      const maxWidth = Math.max(160, box.clientWidth * 0.75)
      const naturalWidth = measure.scrollWidth
      let next = 1

      while (naturalWidth * next > maxWidth && next > 0.62) {
        next -= 0.035
      }

      setTitleScale(Number(next.toFixed(3)))
    }

    updateTitleScale()
    const observer = new ResizeObserver(updateTitleScale)
    if (titleBoxRef.current) observer.observe(titleBoxRef.current)
    return () => observer.disconnect()
  }, [question.id, question.title])

  return (
    <YinyuPanel p="md" className="yy-theory-question-card">
      <Stack gap="sm">
        <Group justify="space-between" align="flex-start">
          <Stack gap={6}>
            <Group gap="xs">
              <YinyuGradientText tone={questionTypeTone(question.type)} className="yy-theory-meta-gradient">
                {questionTypeLabel(question.type)}
              </YinyuGradientText>
              <YinyuGradientText tone="score" className="yy-theory-meta-gradient">
                {question.score} 分
              </YinyuGradientText>
            </Group>
            <div className="yy-theory-title-fit-box" ref={titleBoxRef}>
              <span ref={titleMeasureRef} className="yy-theory-title-measure" aria-hidden="true">
                {question.title}
              </span>
              <YinyuTextType
                key={`${question.id}-${question.title}`}
                as="h4"
                text={question.title}
                loop={false}
                typingSpeed={20}
                showCursor
                cursorCharacter="_"
                cursorClassName="yy-theory-type-cursor"
                className="yy-theory-question-title-type"
                style={{ '--yy-theory-title-scale': titleScale } as CSSProperties}
              />
            </div>
          </Stack>
        </Group>

        {question.content && (
          <Text className="yy-readable-text" size="sm">
            {question.content}
          </Text>
        )}

        {isMultiple ? (
          <Checkbox.Group
            value={selected.map(String)}
            onChange={(values) => onChange(values.map(Number).sort((a, b) => a - b))}
          >
            <Stack gap="xs" className="yy-theory-option-list">
              {question.options.map((option, index) => (
                <Checkbox key={index} disabled={disabled} value={String(index)} label={option} />
              ))}
            </Stack>
          </Checkbox.Group>
        ) : (
          <Radio.Group
            value={selected[0] !== undefined ? String(selected[0]) : null}
            onChange={(value) => onChange(value === null ? [] : [Number(value)])}
          >
            <Stack gap="xs" className="yy-theory-option-list">
              {question.options.map((option, index) => (
                <Radio key={index} disabled={disabled} value={String(index)} label={option} />
              ))}
            </Stack>
          </Radio.Group>
        )}
      </Stack>
    </YinyuPanel>
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
  const [currentIndex, setCurrentIndex] = useState(0)

  const submitted = paper?.status === TheoryAnswerSheetStatus.Submitted
  const questions = useMemo(() => [...(paper?.questions ?? [])].sort((a, b) => a.order - b.order), [paper?.questions])
  const answeredCount = useMemo(
    () => questions.filter((question) => (answers[question.id]?.length ?? 0) > 0).length,
    [answers, questions]
  )
  const unansweredCount = questions.length - answeredCount

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
      setErrorText('理论试卷尚未发布，或当前账号暂无访问权限。')
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    loadPaper()
  }, [numId])

  useEffect(() => {
    if (!questions.length) return
    setCurrentIndex((index) => Math.min(Math.max(index, 0), questions.length - 1))
  }, [questions.length])

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

  const currentQuestion = questions[currentIndex]
  const goToQuestion = (index: number) => setCurrentIndex(Math.min(Math.max(index, 0), questions.length - 1))
  const currentAnswered = currentQuestion ? (answers[currentQuestion.id]?.length ?? 0) > 0 : false

  return (
    <WithNavBar minWidth={0} width="min(100%, calc(100vw - 7.25rem))">
      <WithGameTab>
        <Stack gap="md" className="yy-theory-page">
          {loading && !paper ? (
            <YinyuStatePage tone="neutral" p="xl" className="yy-theory-loading">
              <Stack gap="xs">
                <Badge variant="light">Theory</Badge>
                <Title order={2}>理论赛加载中</Title>
                <Text className="yy-readable-text">正在读取试卷、答题状态与队伍权限。</Text>
              </Stack>
            </YinyuStatePage>
          ) : null}

          {errorText && !paper && (
            <YinyuStatePage tone="neutral" p="xl" className="yy-theory-loading">
              <Stack gap="xs">
                <Badge variant="light">Theory</Badge>
                <Title order={2}>理论赛暂不可用</Title>
                <Text className="yy-readable-text">{errorText}</Text>
              </Stack>
            </YinyuStatePage>
          )}

          {paper && (
            <>
              <YinyuPanel p="md" className="yy-theory-header">
                <Group justify="space-between" align="center" wrap="wrap">
                  <Stack gap={4}>
                    <Title order={3}>{paper.title}</Title>
                    {paper.description && <Text className="yy-readable-text">{paper.description}</Text>}
                    <Group gap="xs">
                      <YinyuGradientText tone="signal" className="yy-theory-stat-gradient">
                        {answeredCount} / {questions.length} 已作答
                      </YinyuGradientText>
                      <YinyuGradientText tone="score" className="yy-theory-stat-gradient">
                        总分 {paper.totalScore}
                      </YinyuGradientText>
                      {submitted && (
                        <YinyuGradientText tone="ongoing" className="yy-theory-stat-gradient">
                          得分 {paper.score ?? 0} / {paper.totalScore}
                        </YinyuGradientText>
                      )}
                    </Group>
                  </Stack>
                  <Group className="yy-theory-actions">
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
                      leftSection={
                        loading ? <YinyuHeartbeatIcon label="submitting theory answer" /> : <Icon path={mdiSendCheckOutline} size={1} />
                      }
                      onClick={() => setConfirmOpened(true)}
                    >
                      {loading ? '正在提交' : '提交答卷'}
                    </Button>
                  </Group>
                </Group>
              </YinyuPanel>

              <Modal opened={confirmOpened && !submitted} onClose={() => setConfirmOpened(false)} title="确认提交答卷" centered>
                <YinyuModalBody p="md">
                  <Stack gap="md">
                    <Text size="sm" className="yy-readable-text">
                      提交后不再允许修改，系统会立即判分并计入理论赛排行。当前还有 {unansweredCount} 题未作答。
                    </Text>
                    <Group justify="flex-end">
                      <Button variant="default" disabled={loading} onClick={() => setConfirmOpened(false)}>
                        取消
                      </Button>
                      <Button
                        color="teal"
                        disabled={loading}
                        leftSection={loading ? <YinyuHeartbeatIcon label="confirm theory submit" /> : undefined}
                        onClick={submit}
                      >
                        {loading ? '正在提交' : '确认提交'}
                      </Button>
                    </Group>
                  </Stack>
                </YinyuModalBody>
              </Modal>

              {submitted && (
                <Alert color="teal" icon={<Icon path={mdiCheck} />}>
                  答卷已提交，当前成绩已经计入理论赛排行榜。
                </Alert>
              )}

              <Grid align="flex-start" className="yy-theory-workspace">
                <Grid.Col span={{ base: 12, lg: 9 }}>
                  {currentQuestion && (
                    <Stack gap="md" className="yy-theory-main-column">
                      <YinyuPanel p="md" className="yy-theory-nav-panel">
                        <Group justify="space-between" align="center" wrap="wrap">
                          <Stack gap={2}>
                            <Text size="sm" className="yy-readable-text">
                              第 {currentIndex + 1} 题 / 共 {questions.length} 题
                            </Text>
                            <Group gap="xs">
                              <YinyuGradientText tone={currentAnswered ? 'ongoing' : 'ended'} className="yy-theory-stat-gradient">
                                {currentAnswered ? '已作答' : '未作答'}
                              </YinyuGradientText>
                              <YinyuGradientText tone="silver" className="yy-theory-stat-gradient">
                                剩余 {questions.length - currentIndex - 1} 题
                              </YinyuGradientText>
                            </Group>
                          </Stack>
                          <Group>
                            <Button
                              variant="default"
                              disabled={currentIndex <= 0}
                              leftSection={<Icon path={mdiArrowLeftBold} size={1} />}
                              onClick={() => goToQuestion(currentIndex - 1)}
                            >
                              上一题
                            </Button>
                            <Button
                              variant="default"
                              disabled={currentIndex >= questions.length - 1}
                              rightSection={<Icon path={mdiArrowRightBold} size={1} />}
                              onClick={() => goToQuestion(currentIndex + 1)}
                            >
                              下一题
                            </Button>
                          </Group>
                        </Group>
                      </YinyuPanel>

                      <TheoryQuestionCard
                        key={currentQuestion.id}
                        question={currentQuestion}
                        selected={answers[currentQuestion.id] ?? []}
                        disabled={submitted || loading}
                        onChange={(selected) =>
                          setAnswers((current) => ({ ...current, [currentQuestion.id]: selected }))
                        }
                      />
                    </Stack>
                  )}
                </Grid.Col>
                <Grid.Col span={{ base: 12, lg: 3 }}>
                  <YinyuPanel p="md" className="yy-theory-side-panel">
                    <Stack gap="sm">
                      <Group justify="space-between">
                        <Text fw={700}>题目索引</Text>
                        <Badge variant="light">
                          {answeredCount} / {questions.length}
                        </Badge>
                      </Group>
                      <SimpleGrid className="yy-theory-index-grid" cols={{ base: 4, sm: 6, lg: 4 }} spacing="xs">
                        {questions.map((question, index) => {
                          const answered = (answers[question.id]?.length ?? 0) > 0
                          const active = index === currentIndex

                          return (
                            <Tooltip key={question.id} label={`${index + 1}. ${questionTypeLabel(question.type)}`}>
                              <UnstyledButton
                                className="yy-theory-node"
                                data-active={active || undefined}
                                data-answered={answered || undefined}
                                onClick={() => goToQuestion(index)}
                              >
                                <span className="yy-theory-node-number">{index + 1}</span>
                                <span className="yy-theory-node-type">{questionTypeShort(question.type)}</span>
                              </UnstyledButton>
                            </Tooltip>
                          )
                        })}
                      </SimpleGrid>
                    </Stack>
                  </YinyuPanel>
                </Grid.Col>
              </Grid>
            </>
          )}
        </Stack>
      </WithGameTab>
    </WithNavBar>
  )
}

export default TheoryPage
