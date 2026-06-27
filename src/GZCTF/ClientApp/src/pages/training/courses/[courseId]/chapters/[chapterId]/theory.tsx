import {
  Alert,
  Badge,
  Button,
  Checkbox,
  Grid,
  Group,
  Modal,
  Progress,
  Radio,
  SimpleGrid,
  Stack,
  Text,
  Title,
  Tooltip,
  UnstyledButton,
} from '@mantine/core'
import { showNotification } from '@mantine/notifications'
import { mdiArrowLeft, mdiArrowLeftBold, mdiArrowRightBold, mdiCheck, mdiContentSaveOutline, mdiSendCheckOutline } from '@mdi/js'
import { Icon } from '@mdi/react'
import { FC, useEffect, useMemo, useState } from 'react'
import { Link, useParams } from 'react-router'
import { WithNavBar } from '@Components/WithNavbar'
import {
  theoryQuestionTypeLabel,
  theoryQuestionTypeShort,
} from '@Components/training/CourseTheoryQuestionTools'
import { YinyuGameBendsBackground } from '@Components/yinyu/YinyuReactBits'
import { YinyuHeartbeatIcon, YinyuModalBody, YinyuPanel, YinyuStatePage } from '@Components/yinyu/YinyuUI'
import { showErrorMsg } from '@Utils/Shared'
import {
  TrainingCourseChapterTheoryPlayerPaperModel,
  TrainingCourseChapterTheoryPlayerQuestionModel,
  trainingCourseApi,
} from '@Utils/TrainingApi'
import { TheoryAnswerModel, TheoryAnswerSheetStatus, TheoryQuestionType } from '../../../../../../Api/TheoryApi'

const optionLabel = (index: number) => String.fromCharCode(65 + index)

const formatIndexes = (indexes: number[], options: string[]) =>
  indexes.length
    ? indexes
        .map((index) => {
          const prefix = optionLabel(index)
          const content = options[index] ?? ''
          return content ? `${prefix}. ${content}` : prefix
        })
        .join('；')
    : '未作答'

const isAnswerCorrect = (selected: number[], correctIndexes?: number[] | null) =>
  correctIndexes !== undefined &&
  correctIndexes !== null &&
  [...selected].sort((a, b) => a - b).join(',') === [...correctIndexes].sort((a, b) => a - b).join(',')

const TheoryQuestionCard: FC<{
  question: TrainingCourseChapterTheoryPlayerQuestionModel
  selected: number[]
  disabled: boolean
  submitted: boolean
  onChange: (selected: number[]) => void
}> = ({ question, selected, disabled, submitted, onChange }) => {
  const isMultiple = question.type === TheoryQuestionType.MultipleChoice
  const correctIndexes = question.answerIndexes ?? []
  const revealAnswer = submitted && question.answerIndexes !== undefined && question.answerIndexes !== null
  const isCorrect = revealAnswer && isAnswerCorrect(selected, correctIndexes)

  return (
    <YinyuPanel p="md" className="yy-theory-question-card">
      <Stack gap="sm">
        <Group justify="space-between" align="flex-start">
          <Stack gap={6}>
            <Group gap="xs">
              <Badge variant="light">{theoryQuestionTypeLabel(question.type)}</Badge>
              <Badge variant="light" color="violet">
                {question.score} 分
              </Badge>
              {revealAnswer ? (
                <Badge color={isCorrect ? 'teal' : 'red'} variant="light">
                  {isCorrect ? '正确' : '错误'}
                </Badge>
              ) : null}
            </Group>
            <Title order={3}>{question.title}</Title>
          </Stack>
        </Group>

        {question.content ? (
          <Text className="yy-readable-text" size="sm">
            {question.content}
          </Text>
        ) : null}

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

        {revealAnswer ? (
          <YinyuPanel p="sm" className="yy-theory-review-panel">
            <Stack gap={6}>
              <Text size="sm" fw={700}>
                答题复盘
              </Text>
              <Text size="sm" className="yy-readable-text">
                我的答案：{formatIndexes(selected, question.options)}
              </Text>
              <Text size="sm" className="yy-readable-text">
                正确答案：{formatIndexes(correctIndexes, question.options)}
              </Text>
              <Text size="sm" className="yy-readable-text">
                题目解析：解析暂未配置，请结合题干与标准答案复盘。
              </Text>
            </Stack>
          </YinyuPanel>
        ) : null}
      </Stack>
    </YinyuPanel>
  )
}

const ChapterTheoryPage: FC = () => {
  const { courseId, chapterId } = useParams()
  const courseNum = Number(courseId)
  const chapterNum = Number(chapterId)
  const [paper, setPaper] = useState<TrainingCourseChapterTheoryPlayerPaperModel>()
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
  const questionById = useMemo(() => new Map(questions.map((question) => [question.id, question])), [questions])
  const reviewItems = useMemo(() => {
    if (!submitted) return []

    return questions.map((question, index) => {
      const selected = answers[question.id] ?? []
      const correct = question.answerIndexes ?? []
      const isCorrect = isAnswerCorrect(selected, question.answerIndexes)

      return {
        question,
        index,
        selected,
        correct,
        isCorrect,
      }
    })
  }, [answers, questions, submitted])
  const wrongReviewItems = reviewItems.filter((item) => !item.isCorrect)
  const correctCount = reviewItems.length - wrongReviewItems.length
  const reviewRate = reviewItems.length ? Math.round((correctCount / reviewItems.length) * 100) : 0

  const toAnswerModel = (): TheoryAnswerModel[] =>
    Object.entries(answers).map(([paperQuestionId, selectedIndexes]) => ({
      paperQuestionId: Number(paperQuestionId),
      selectedIndexes,
    }))

  const loadPaper = async () => {
    if (!Number.isFinite(courseNum) || !Number.isFinite(chapterNum)) return
    setLoading(true)
    setErrorText(undefined)
    try {
      const res = await trainingCourseApi.chapterTheory(courseNum, chapterNum)
      setPaper(res.data)
      setAnswers(
        Object.fromEntries((res.data.answers ?? []).map((answer) => [answer.paperQuestionId, answer.selectedIndexes]))
      )
    } catch {
      setErrorText('课后测试尚未发放，或当前账号暂时没有访问权限。')
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    void loadPaper()
  }, [courseId, chapterId])

  useEffect(() => {
    if (!questions.length) return
    setCurrentIndex((index) => Math.min(Math.max(index, 0), questions.length - 1))
  }, [questions.length])

  const saveDraft = async () => {
    if (!paper || submitted) return
    setLoading(true)
    try {
      const res = await trainingCourseApi.saveChapterTheoryDraft(courseNum, chapterNum, { answers: toAnswerModel() })
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
      const res = await trainingCourseApi.submitChapterTheory(courseNum, chapterNum, { answers: toAnswerModel() })
      setPaper(res.data)
      showNotification({ color: res.data.passed ? 'green' : 'yellow', message: '答卷已提交', icon: <Icon path={mdiCheck} size={1} /> })
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
      <Stack gap="md" className="yy-training-page yy-theory-page">
        <YinyuGameBendsBackground className="yy-training-bg" />
        <Button
          component={Link}
          to={`/training/courses/${courseNum}/chapters/${chapterNum}`}
          variant="subtle"
          leftSection={<Icon path={mdiArrowLeft} size={0.85} />}
          w="fit-content"
        >
          返回章节
        </Button>

        {loading && !paper ? (
          <YinyuStatePage tone="neutral" p="xl" className="yy-theory-loading">
            <Stack gap="xs">
              <Badge variant="light">Theory</Badge>
              <Title order={2}>课后测试加载中</Title>
              <Text className="yy-readable-text">正在读取测试题目与答题状态。</Text>
            </Stack>
          </YinyuStatePage>
        ) : null}

        {errorText && !paper ? (
          <YinyuStatePage tone="neutral" p="xl" className="yy-theory-loading">
            <Stack gap="xs">
              <Badge variant="light">Theory</Badge>
              <Title order={2}>课后测试暂不可用</Title>
              <Text className="yy-readable-text">{errorText}</Text>
            </Stack>
          </YinyuStatePage>
        ) : null}

        {paper ? (
          <>
            <YinyuPanel p="md" className="yy-theory-header">
              <Group justify="space-between" align="center" wrap="wrap">
                <Stack gap={4}>
                  <Title order={3}>{paper.title}</Title>
                  {paper.description ? <Text className="yy-readable-text">{paper.description}</Text> : null}
                  <Group gap="xs">
                    <Badge variant="light">{answeredCount} / {questions.length} 已作答</Badge>
                    <Badge variant="light" color="violet">总分 {paper.totalScore}</Badge>
                    <Badge variant="light" color="teal">及格线 {paper.passRate}%</Badge>
                    {submitted ? (
                      <Badge color={paper.passed ? 'green' : 'yellow'}>
                        得分 {paper.score ?? 0} / {paper.totalScore} · {paper.passed ? '已通过' : '未通过'}
                      </Badge>
                    ) : null}
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
                    提交后不再允许修改。当前还有 {unansweredCount} 题未作答。
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

            {submitted ? (
              <Alert color={paper.passed ? 'teal' : 'yellow'} icon={<Icon path={mdiCheck} />}>
                答卷已提交，当前成绩：{paper.score ?? 0} / {paper.totalScore}。
              </Alert>
            ) : null}

            {submitted ? (
              <YinyuPanel p="md" className="yy-theory-review-summary">
                <Stack gap="md">
                  <Group justify="space-between" align="flex-start" wrap="wrap">
                    <Stack gap={4}>
                      <Badge color={paper.passed ? 'teal' : 'yellow'} variant="light">
                        {paper.passed ? '已通过' : '未通过'}
                      </Badge>
                      <Title order={3}>答卷复盘</Title>
                      <Text size="sm" className="yy-readable-text">
                        本次答卷得分 {paper.score ?? 0}/{paper.totalScore}，正确 {correctCount}/{reviewItems.length} 题，正确率 {reviewRate}%。
                      </Text>
                    </Stack>
                    <Stack gap={4} miw={220}>
                      <Group justify="space-between">
                        <Text size="sm" fw={800}>
                          正确率
                        </Text>
                        <Text size="sm" className="yy-readable-text">
                          {reviewRate}%
                        </Text>
                      </Group>
                      <Progress value={reviewRate} color={paper.passed ? 'teal' : 'yellow'} />
                    </Stack>
                  </Group>

                  {wrongReviewItems.length > 0 ? (
                    <Stack gap="xs">
                      <Text fw={800}>错题列表</Text>
                      {wrongReviewItems.map((item) => (
                        <UnstyledButton
                          key={item.question.id}
                          className="yy-theory-review-row"
                          onClick={() => goToQuestion(item.index)}
                        >
                          <span>第 {item.index + 1} 题</span>
                          <strong>{item.question.title}</strong>
                          <em>我的答案：{formatIndexes(item.selected, item.question.options)}</em>
                          <em>正确答案：{formatIndexes(item.correct, item.question.options)}</em>
                        </UnstyledButton>
                      ))}
                    </Stack>
                  ) : (
                    <Text size="sm" className="yy-readable-text">
                      本次没有错题，可以继续学习下一章节。
                    </Text>
                  )}
                </Stack>
              </YinyuPanel>
            ) : null}

            <Grid align="flex-start" className="yy-theory-workspace">
              <Grid.Col span={{ base: 12, lg: 9 }}>
                {currentQuestion ? (
                  <Stack gap="md" className="yy-theory-main-column">
                    <YinyuPanel p="md" className="yy-theory-nav-panel">
                      <Group justify="space-between" align="center" wrap="wrap">
                        <Stack gap={2}>
                          <Text size="sm" className="yy-readable-text">
                            第 {currentIndex + 1} 题 / 共 {questions.length} 题
                          </Text>
                          <Group gap="xs">
                            <Badge color={currentAnswered ? 'teal' : 'gray'}>{currentAnswered ? '已作答' : '未作答'}</Badge>
                            <Badge variant="light">剩余 {questions.length - currentIndex - 1} 题</Badge>
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
                      submitted={submitted}
                      onChange={(selected) => setAnswers((current) => ({ ...current, [currentQuestion.id]: selected }))}
                    />
                  </Stack>
                ) : null}
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
                        const reviewQuestion = questionById.get(question.id)
                        const correct =
                          submitted && reviewQuestion ? isAnswerCorrect(answers[question.id] ?? [], reviewQuestion.answerIndexes) : false

                        return (
                          <Tooltip key={question.id} label={`${index + 1}. ${theoryQuestionTypeLabel(question.type)}`}>
                            <UnstyledButton
                              className="yy-theory-node"
                              data-active={active || undefined}
                              data-answered={answered || undefined}
                              data-correct={correct || undefined}
                              data-wrong={submitted && !correct ? true : undefined}
                              onClick={() => goToQuestion(index)}
                            >
                              <span className="yy-theory-node-number">{index + 1}</span>
                              <span className="yy-theory-node-type">{theoryQuestionTypeShort(question.type)}</span>
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
        ) : null}
      </Stack>
    </WithNavBar>
  )
}

export default ChapterTheoryPage
