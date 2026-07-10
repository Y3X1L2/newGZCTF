import {
  ActionIcon,
  Badge,
  Button,
  Checkbox,
  Grid,
  Group,
  NumberInput,
  ScrollArea,
  Select,
  SimpleGrid,
  Stack,
  Switch,
  Table,
  Text,
  TextInput,
  Textarea,
  Title,
  Tooltip,
} from '@mantine/core'
import { showNotification } from '@mantine/notifications'
import {
  mdiArrowLeft,
  mdiContentSaveOutline,
  mdiDeleteOutline,
  mdiDiceMultipleOutline,
  mdiMagnify,
  mdiPlus,
} from '@mdi/js'
import { Icon } from '@mdi/react'
import { FC, useEffect, useMemo, useState } from 'react'
import { Link, useParams } from 'react-router'
import { WithNavBar } from '@Components/WithNavbar'
import {
  DEFAULT_THEORY_BANK_NAME,
  getTheoryAnswerLabel,
  theoryQuestionTypeFilterOptions,
  theoryQuestionTypeLabel,
} from '@Components/training/CourseTheoryQuestionTools'
import { YinyuGameBendsBackground } from '@Components/yinyu/YinyuReactBits'
import { YinyuPanel } from '@Components/yinyu/YinyuUI'
import { showErrorMsg } from '@Utils/Shared'
import {
  TrainingCourseChapterModel,
  TrainingCourseChapterTheoryPaperDetailModel,
  TrainingCourseTheoryPaperQuestionEditModel,
  TrainingCourseTheoryQuestionModel,
  TrainingCourseModel,
  trainingCourseAdminApi,
  trainingCourseApi,
} from '@Utils/TrainingApi'
import { TheoryQuestionType } from '../../../../../../Api/TheoryApi'

const normalizeOrder = (questions: TrainingCourseTheoryPaperQuestionEditModel[]) =>
  questions.map((question, index) => ({ ...question, order: index + 1 }))

const toPaperQuestion = (
  question: TrainingCourseTheoryQuestionModel,
  score: number,
  order: number
): TrainingCourseTheoryPaperQuestionEditModel => ({
  sourceQuestionId: question.id,
  type: question.type,
  bankName: question.bankName,
  title: question.title,
  content: question.content,
  options: question.options,
  answerIndexes: question.answerIndexes,
  score,
  order,
})

const ChapterTheoryEditPage: FC = () => {
  const { courseId, chapterId } = useParams()
  const courseNum = Number(courseId)
  const chapterNum = Number(chapterId)
  const [course, setCourse] = useState<TrainingCourseModel | null>(null)
  const [chapter, setChapter] = useState<TrainingCourseChapterModel | null>(null)
  const [paper, setPaper] = useState<TrainingCourseChapterTheoryPaperDetailModel | null>(null)
  const [questions, setQuestions] = useState<TrainingCourseTheoryQuestionModel[]>([])
  const [selectedIds, setSelectedIds] = useState<number[]>([])
  const [keyword, setKeyword] = useState('')
  const [typeFilter, setTypeFilter] = useState<string>('All')
  const [bankFilter, setBankFilter] = useState<string>('All')
  const [uniformScore, setUniformScore] = useState(5)
  const [randomCount, setRandomCount] = useState(5)
  const [passRateInput, setPassRateInput] = useState<string | number>(60)
  const [saving, setSaving] = useState(false)

  const bankNames = useMemo(() => {
    const names = [...new Set(questions.map((question) => question.bankName || DEFAULT_THEORY_BANK_NAME))].sort()
    return ['All', ...names]
  }, [questions])

  const filteredQuestions = useMemo(
    () =>
      questions.filter((question) => {
        if (typeFilter !== 'All' && question.type !== typeFilter) return false
        if (bankFilter !== 'All' && (question.bankName || DEFAULT_THEORY_BANK_NAME) !== bankFilter) return false
        const key = keyword.trim().toLowerCase()
        if (!key) return true
        return [question.title, question.content, question.bankName]
          .filter(Boolean)
          .some((value) => String(value).toLowerCase().includes(key))
      }),
    [bankFilter, keyword, questions, typeFilter]
  )

  const totalScore = useMemo(() => (paper?.questions ?? []).reduce((sum, question) => sum + question.score, 0), [paper])

  const load = async () => {
    if (!Number.isFinite(courseNum) || !Number.isFinite(chapterNum)) return
    try {
      const [courseRes, chapterRes, paperRes, questionRes] = await Promise.all([
        trainingCourseApi.course(courseNum),
        trainingCourseApi.chapter(courseNum, chapterNum),
        trainingCourseAdminApi.chapterTheoryPaper(courseNum, chapterNum),
        trainingCourseAdminApi.theoryQuestions(courseNum, { count: 5000 }),
      ])
      setCourse(courseRes.data)
      setChapter(chapterRes.data)
      setPaper(paperRes.data)
      setPassRateInput(paperRes.data.passRate)
      setQuestions(questionRes.data)
    } catch (err) {
      showErrorMsg(err, (key) => key)
    }
  }

  useEffect(() => {
    void load()
  }, [courseId, chapterId])

  const setPaperQuestions = (next: TrainingCourseTheoryPaperQuestionEditModel[]) => {
    if (!paper) return
    setPaper({ ...paper, questions: normalizeOrder(next), totalScore: next.reduce((sum, item) => sum + item.score, 0) })
  }

  const addQuestions = (items: TrainingCourseTheoryQuestionModel[]) => {
    if (!paper || items.length === 0) return
    const existing = new Set(paper.questions.map((question) => question.sourceQuestionId).filter(Boolean))
    const additions = items
      .filter((question) => !existing.has(question.id))
      .map((question, index) => toPaperQuestion(question, uniformScore, paper.questions.length + index + 1))
    setPaperQuestions([...paper.questions, ...additions])
  }

  const addRandomQuestions = () => {
    const existing = new Set((paper?.questions ?? []).map((question) => question.sourceQuestionId).filter(Boolean))
    const pool = filteredQuestions.filter((question) => !existing.has(question.id))
    const shuffled = [...pool].sort(() => Math.random() - 0.5).slice(0, Math.max(1, randomCount))
    addQuestions(shuffled)
  }

  const savePaper = async (publish?: boolean) => {
    if (!paper) return
    const passRate = typeof passRateInput === 'number' ? passRateInput : Number(passRateInput)
    if (!Number.isFinite(passRate) || passRate < 1 || passRate > 100) {
      showNotification({ color: 'red', message: '请输入 1-100 之间的及格线。' })
      return
    }

    setSaving(true)
    try {
      const res = await trainingCourseAdminApi.saveChapterTheoryPaper(courseNum, chapterNum, {
        title: paper.title.trim(),
        description: paper.description,
        passRate: Math.round(passRate),
        allowRetake: paper.allowRetake,
        showCorrectAnswerAfterSubmit: paper.showCorrectAnswerAfterSubmit,
        isPublished: publish ?? paper.isPublished,
        questions: normalizeOrder(paper.questions).map((question, index) => ({ ...question, order: index + 1 })),
      })
      setPaper(res.data)
      setPassRateInput(res.data.passRate)
      showNotification({ color: 'teal', message: publish ?? paper.isPublished ? '课后测试已保存并发放。' : '课后测试草稿已保存。' })
    } catch (err) {
      showErrorMsg(err, (key) => key)
    } finally {
      setSaving(false)
    }
  }

  if (!course || !chapter || !paper) {
    return (
      <WithNavBar isLoading width="min(118rem, calc(100vw - 4rem))">
        <></>
      </WithNavBar>
    )
  }

  return (
    <WithNavBar width="min(132rem, calc(100% - 1.5rem))">
      <Stack gap="md" className="yy-training-page yy-training-theory-edit-page">
        <YinyuGameBendsBackground className="yy-training-bg" />
        <Group justify="space-between" align="center">
          <Button
            component={Link}
            to={`/training/courses/${course.id}?tab=homework`}
            variant="subtle"
            leftSection={<Icon path={mdiArrowLeft} size={0.85} />}
          >
            返回课后练习
          </Button>
          <Group gap="xs">
            <Button variant="light" loading={saving} onClick={() => savePaper(false)}>
              保存草稿
            </Button>
            <Button loading={saving} leftSection={<Icon path={mdiContentSaveOutline} size={0.82} />} onClick={() => savePaper(true)}>
              保存并发放
            </Button>
          </Group>
        </Group>

        <YinyuPanel p="lg">
          <Stack gap="xs">
            <Badge variant="light">{course.title}</Badge>
            <Title order={2}>{chapter.title} · 课后测试配置</Title>
          </Stack>
        </YinyuPanel>

        <Grid align="flex-start" className="yy-training-theory-edit-grid">
          <Grid.Col span={{ base: 12, lg: 7 }}>
            <YinyuPanel p="lg">
              <Stack gap="md">
                <SimpleGrid cols={{ base: 1, md: 2 }}>
                  <TextInput
                    label="测试标题"
                    value={paper.title}
                    onChange={(event) => setPaper({ ...paper, title: event.currentTarget.value })}
                  />
                  <NumberInput
                    label="及格线百分比"
                    min={1}
                    max={100}
                    value={passRateInput}
                    onChange={(value) => setPassRateInput(value === '' ? '' : value)}
                  />
                </SimpleGrid>
                <Textarea
                  label="测试说明"
                  autosize
                  minRows={2}
                  value={paper.description}
                  onChange={(event) => setPaper({ ...paper, description: event.currentTarget.value })}
                />
                <Group grow>
                  <Switch
                    label="允许重做"
                    checked={paper.allowRetake}
                    onChange={(event) => setPaper({ ...paper, allowRetake: event.currentTarget.checked })}
                  />
                  <Switch
                    label="提交后显示正确答案"
                    checked={paper.showCorrectAnswerAfterSubmit}
                    onChange={(event) =>
                      setPaper({ ...paper, showCorrectAnswerAfterSubmit: event.currentTarget.checked })
                    }
                  />
                </Group>
                <Group justify="space-between">
                  <Group gap="xs">
                    <Badge variant="light" color={paper.isPublished ? 'green' : 'yellow'}>
                      {paper.isPublished ? '已发放' : '草稿'}
                    </Badge>
                    <Badge variant="light">{paper.questions.length} 题</Badge>
                    <Badge variant="light">{totalScore} 分</Badge>
                  </Group>
                  <Switch
                    label="发放"
                    checked={paper.isPublished}
                    onChange={(event) => setPaper({ ...paper, isPublished: event.currentTarget.checked })}
                  />
                </Group>

                <Table striped highlightOnHover verticalSpacing="sm">
                  <Table.Thead>
                    <Table.Tr>
                      <Table.Th w={56}>序号</Table.Th>
                      <Table.Th>题目</Table.Th>
                      <Table.Th w={120}>分值</Table.Th>
                      <Table.Th w={64}>操作</Table.Th>
                    </Table.Tr>
                  </Table.Thead>
                  <Table.Tbody>
                    {paper.questions.map((question, index) => (
                      <Table.Tr key={`${question.sourceQuestionId ?? question.title}-${index}`}>
                        <Table.Td>{index + 1}</Table.Td>
                        <Table.Td>
                          <Stack gap={2}>
                            <Group gap="xs">
                              <Badge variant="light">{theoryQuestionTypeLabel(question.type)}</Badge>
                              {question.bankName ? <Badge variant="outline">{question.bankName}</Badge> : null}
                            </Group>
                            <Text fw={800} lineClamp={1}>
                              {question.title}
                            </Text>
                          </Stack>
                        </Table.Td>
                        <Table.Td>
                          <NumberInput
                            min={1}
                            value={question.score}
                            onChange={(value) =>
                              setPaperQuestions(
                                paper.questions.map((item, itemIndex) =>
                                  itemIndex === index ? { ...item, score: Number(value) || 1 } : item
                                )
                              )
                            }
                          />
                        </Table.Td>
                        <Table.Td>
                          <ActionIcon
                            color="red"
                            variant="subtle"
                            onClick={() => setPaperQuestions(paper.questions.filter((_, itemIndex) => itemIndex !== index))}
                          >
                            <Icon path={mdiDeleteOutline} size={0.82} />
                          </ActionIcon>
                        </Table.Td>
                      </Table.Tr>
                    ))}
                  </Table.Tbody>
                </Table>
                {paper.questions.length === 0 ? (
                  <Text c="dimmed" ta="center" py="lg">
                    还没有加入题目，请从右侧课程题库添加。
                  </Text>
                ) : null}
              </Stack>
            </YinyuPanel>
          </Grid.Col>

          <Grid.Col span={{ base: 12, lg: 5 }}>
            <YinyuPanel p="lg">
              <Stack gap="md">
                <Group justify="space-between">
                  <Title order={3}>课程题库</Title>
                  <Button
                    size="xs"
                    variant="light"
                    leftSection={<Icon path={mdiPlus} size={0.75} />}
                    onClick={() => addQuestions(questions.filter((question) => selectedIds.includes(question.id)))}
                  >
                    加入选中
                  </Button>
                </Group>
                <SimpleGrid cols={{ base: 1, sm: 2 }} spacing="sm">
                  <TextInput
                    leftSection={<Icon path={mdiMagnify} size={0.75} />}
                    placeholder="搜索题目"
                    value={keyword}
                    onChange={(event) => setKeyword(event.currentTarget.value)}
                  />
                  <Select
                    data={theoryQuestionTypeFilterOptions}
                    value={typeFilter}
                    onChange={(value) => setTypeFilter(value ?? 'All')}
                  />
                  <Select
                    data={bankNames.map((name) => ({ value: name, label: name === 'All' ? '全部题库' : name }))}
                    value={bankFilter}
                    onChange={(value) => setBankFilter(value ?? 'All')}
                    searchable
                  />
                  <NumberInput label="统一分值" min={1} value={uniformScore} onChange={(value) => setUniformScore(Number(value) || 1)} />
                </SimpleGrid>
                <Group grow>
                  <NumberInput
                    label="随机数量"
                    min={1}
                    max={filteredQuestions.length || 1}
                    value={randomCount}
                    onChange={(value) => setRandomCount(Number(value) || 1)}
                  />
                  <Button
                    mt="auto"
                    variant="light"
                    leftSection={<Icon path={mdiDiceMultipleOutline} size={0.82} />}
                    onClick={addRandomQuestions}
                  >
                    随机抽题
                  </Button>
                </Group>
                <ScrollArea.Autosize mah={620}>
                  <Stack gap="xs">
                    {filteredQuestions.map((question) => {
                      const checked = selectedIds.includes(question.id)
                      return (
                        <YinyuPanel key={question.id} p="sm">
                          <Group align="flex-start" wrap="nowrap">
                            <Checkbox
                              checked={checked}
                              onChange={(event) =>
                                setSelectedIds((current) =>
                                  event.currentTarget.checked
                                    ? [...current, question.id]
                                    : current.filter((id) => id !== question.id)
                                )
                              }
                            />
                            <Stack gap={2} style={{ flex: 1 }}>
                              <Group gap="xs">
                                <Badge variant="light">{theoryQuestionTypeLabel(question.type)}</Badge>
                                <Badge variant="outline">{question.bankName || DEFAULT_THEORY_BANK_NAME}</Badge>
                              </Group>
                              <Text fw={800} lineClamp={1}>
                                {question.title}
                              </Text>
                              <Text size="xs" c="dimmed" lineClamp={1}>
                                答案：{getTheoryAnswerLabel(question)}
                              </Text>
                            </Stack>
                            <Tooltip label="加入">
                              <ActionIcon variant="light" onClick={() => addQuestions([question])}>
                                <Icon path={mdiPlus} size={0.82} />
                              </ActionIcon>
                            </Tooltip>
                          </Group>
                        </YinyuPanel>
                      )
                    })}
                    {filteredQuestions.length === 0 ? (
                      <Text c="dimmed" ta="center" py="lg">
                        当前课程题库暂无匹配题目。
                      </Text>
                    ) : null}
                  </Stack>
                </ScrollArea.Autosize>
              </Stack>
            </YinyuPanel>
          </Grid.Col>
        </Grid>
      </Stack>
    </WithNavBar>
  )
}

export default ChapterTheoryEditPage
