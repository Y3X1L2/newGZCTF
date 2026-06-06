import {
  ActionIcon,
  Alert,
  Badge,
  Button,
  Card,
  Checkbox,
  FileButton,
  Group,
  Modal,
  MultiSelect,
  NumberInput,
  Radio,
  ScrollArea,
  Select,
  SimpleGrid,
  Stack,
  Table,
  Text,
  Textarea,
  TextInput,
  Title,
} from '@mantine/core'
import { showNotification } from '@mantine/notifications'
import {
  mdiCheck,
  mdiClose,
  mdiContentSaveOutline,
  mdiDeleteOutline,
  mdiFileUploadOutline,
  mdiPlus,
  mdiSendCheckOutline,
} from '@mdi/js'
import { Icon } from '@mdi/react'
import { FC, useEffect, useMemo, useState } from 'react'
import { useParams } from 'react-router'
import { WithGameEditTab } from '@Components/admin/WithGameEditTab'
import { Empty } from '@Components/Empty'
import { showErrorMsg } from '@Utils/Shared'
import {
  theoryAdminApi,
  TheoryPaperDetailModel,
  TheoryPaperQuestionEditModel,
  TheoryQuestionBankItemModel,
  TheoryQuestionType,
} from '../../../../Api/TheoryApi'

const questionTypeOptions = [
  { value: TheoryQuestionType.SingleChoice, label: '单选题' },
  { value: TheoryQuestionType.MultipleChoice, label: '多选题' },
  { value: TheoryQuestionType.TrueFalse, label: '判断题' },
]

const DEFAULT_BANK_NAME = 'Default'

const normalizeBankName = (bankName?: string | null) => bankName?.trim() || DEFAULT_BANK_NAME

const defaultQuestion = (order: number): TheoryPaperQuestionEditModel => ({
  type: TheoryQuestionType.SingleChoice,
  bankName: DEFAULT_BANK_NAME,
  title: '',
  content: '',
  options: ['选项 A', '选项 B'],
  answerIndexes: [0],
  score: 1,
  order,
})

const normalizeQuestion = (question: Partial<TheoryPaperQuestionEditModel>, index: number): TheoryPaperQuestionEditModel => {
  const type = question.type ?? TheoryQuestionType.SingleChoice
  const options = type === TheoryQuestionType.TrueFalse ? ['正确', '错误'] : question.options?.length ? question.options : ['选项 A', '选项 B']

  return {
    id: question.id,
    sourceQuestionId: question.sourceQuestionId ?? null,
    type,
    bankName: normalizeBankName(question.bankName),
    title: question.title ?? '',
    content: question.content ?? '',
    options,
    answerIndexes: question.answerIndexes?.length ? question.answerIndexes : [0],
    score: Number(question.score || 1),
    order: Number(question.order || index + 1),
  }
}

const getAnswerLabel = (question: TheoryPaperQuestionEditModel) =>
  question.answerIndexes.map((idx) => question.options[idx] ?? `#${idx}`).join(' / ')

const PaperQuestionEditor: FC<{
  question: TheoryPaperQuestionEditModel
  index: number
  disabled: boolean
  onChange: (question: TheoryPaperQuestionEditModel) => void
  onDelete: () => void
  onSaveToBank: () => void
}> = ({ question, index, disabled, onChange, onDelete, onSaveToBank }) => {
  const isTrueFalse = question.type === TheoryQuestionType.TrueFalse
  const isMultiple = question.type === TheoryQuestionType.MultipleChoice

  const setType = (type: TheoryQuestionType) => {
    const options = type === TheoryQuestionType.TrueFalse ? ['正确', '错误'] : question.options.length >= 2 ? question.options : ['选项 A', '选项 B']
    onChange({
      ...question,
      type,
      options,
      answerIndexes: question.answerIndexes.filter((i) => i < options.length).slice(0, type === TheoryQuestionType.MultipleChoice ? undefined : 1),
    })
  }

  const setOption = (optionIndex: number, value: string) => {
    const options = [...question.options]
    options[optionIndex] = value
    onChange({ ...question, options })
  }

  const removeOption = (optionIndex: number) => {
    const options = question.options.filter((_, idx) => idx !== optionIndex)
    const answerIndexes = question.answerIndexes
      .filter((idx) => idx !== optionIndex)
      .map((idx) => (idx > optionIndex ? idx - 1 : idx))
    onChange({ ...question, options, answerIndexes: answerIndexes.length ? answerIndexes : [0] })
  }

  return (
    <Card withBorder radius="sm">
      <Stack gap="sm">
        <Group justify="space-between" align="flex-start">
          <Group gap="xs">
            <Badge variant="light">#{index + 1}</Badge>
            <Badge color="teal" variant="light">
              {question.score} 分
            </Badge>
            {question.sourceQuestionId && <Badge variant="outline">题库 #{question.sourceQuestionId}</Badge>}
          </Group>
          <Group gap="xs">
            <Button size="xs" variant="light" disabled={disabled || !question.title.trim()} onClick={onSaveToBank}>
              同步题库
            </Button>
            <ActionIcon color="red" variant="subtle" disabled={disabled} onClick={onDelete}>
              <Icon path={mdiDeleteOutline} size={0.85} />
            </ActionIcon>
          </Group>
        </Group>

        <SimpleGrid cols={{ base: 1, md: 3 }}>
          <Select
            label="题型"
            data={questionTypeOptions}
            value={question.type}
            disabled={disabled}
            onChange={(value) => value && setType(value as TheoryQuestionType)}
          />
          <NumberInput
            label="分值"
            min={1}
            value={question.score}
            disabled={disabled}
            onChange={(value) => onChange({ ...question, score: Number(value || 1) })}
          />
          <NumberInput
            label="排序"
            min={1}
            value={question.order}
            disabled={disabled}
            onChange={(value) => onChange({ ...question, order: Number(value || index + 1) })}
          />
        </SimpleGrid>

        <TextInput
          label="题干"
          required
          value={question.title}
          disabled={disabled}
          onChange={(event) => onChange({ ...question, title: event.currentTarget.value })}
        />
        <Textarea
          label="补充说明"
          minRows={2}
          value={question.content}
          disabled={disabled}
          onChange={(event) => onChange({ ...question, content: event.currentTarget.value })}
        />

        {isMultiple ? (
          <Checkbox.Group
            label="正确答案"
            value={question.answerIndexes.map(String)}
            onChange={(values) => onChange({ ...question, answerIndexes: values.map(Number).sort((a, b) => a - b) })}
          >
            <Stack gap="xs" mt="xs">
              {question.options.map((option, optionIndex) => (
                <Group key={optionIndex} wrap="nowrap" align="center">
                  <Checkbox value={String(optionIndex)} disabled={disabled} />
                  <TextInput
                    value={option}
                    disabled={disabled || isTrueFalse}
                    onChange={(event) => setOption(optionIndex, event.currentTarget.value)}
                    style={{ flex: 1 }}
                  />
                  {!isTrueFalse && (
                    <ActionIcon
                      color="red"
                      variant="subtle"
                      disabled={disabled || question.options.length <= 2}
                      onClick={() => removeOption(optionIndex)}
                    >
                      <Icon path={mdiClose} size={0.8} />
                    </ActionIcon>
                  )}
                </Group>
              ))}
            </Stack>
          </Checkbox.Group>
        ) : (
          <Radio.Group
            label="正确答案"
            value={String(question.answerIndexes[0] ?? 0)}
            onChange={(value) => onChange({ ...question, answerIndexes: [Number(value)] })}
          >
            <Stack gap="xs" mt="xs">
              {question.options.map((option, optionIndex) => (
                <Group key={optionIndex} wrap="nowrap" align="center">
                  <Radio value={String(optionIndex)} disabled={disabled} />
                  <TextInput
                    value={option}
                    disabled={disabled || isTrueFalse}
                    onChange={(event) => setOption(optionIndex, event.currentTarget.value)}
                    style={{ flex: 1 }}
                  />
                  {!isTrueFalse && (
                    <ActionIcon
                      color="red"
                      variant="subtle"
                      disabled={disabled || question.options.length <= 2}
                      onClick={() => removeOption(optionIndex)}
                    >
                      <Icon path={mdiClose} size={0.8} />
                    </ActionIcon>
                  )}
                </Group>
              ))}
            </Stack>
          </Radio.Group>
        )}

        {!isTrueFalse && (
          <Button
            size="xs"
            variant="default"
            leftSection={<Icon path={mdiPlus} size={0.75} />}
            disabled={disabled}
            onClick={() => onChange({ ...question, options: [...question.options, `选项 ${question.options.length + 1}`] })}
          >
            添加选项
          </Button>
        )}
      </Stack>
    </Card>
  )
}

const TheoryPaper: FC = () => {
  const { id } = useParams()
  const numId = parseInt(id ?? '-1')

  const [paper, setPaper] = useState<TheoryPaperDetailModel>()
  const [questions, setQuestions] = useState<TheoryQuestionBankItemModel[]>([])
  const [disabled, setDisabled] = useState(false)
  const [bankOpened, setBankOpened] = useState(false)
  const [jsonOpened, setJsonOpened] = useState(false)
  const [jsonText, setJsonText] = useState('')
  const [bankType, setBankType] = useState<TheoryQuestionType>(TheoryQuestionType.SingleChoice)
  const [selectedBankNames, setSelectedBankNames] = useState<string[]>([])
  const [selectedBankQuestionIds, setSelectedBankQuestionIds] = useState<string[]>([])
  const [bankKeyword, setBankKeyword] = useState('')
  const [bankScore, setBankScore] = useState(1)
  const [randomCount, setRandomCount] = useState(1)

  const totalScore = useMemo(() => paper?.questions.reduce((sum, q) => sum + Number(q.score || 0), 0) ?? 0, [paper])
  const currentSourceIds = useMemo(
    () => new Set((paper?.questions ?? []).map((q) => q.sourceQuestionId).filter((id): id is number => typeof id === 'number')),
    [paper]
  )
  const bankStats = useMemo(() => {
    const stats = new Map<string, number>()
    questions
      .filter((question) => question.type === bankType)
      .forEach((question) => {
        const bankName = normalizeBankName(question.bankName)
        stats.set(bankName, (stats.get(bankName) ?? 0) + 1)
      })

    return stats
  }, [questions, bankType])
  const bankNameOptions = useMemo(
    () =>
      [...bankStats.entries()]
        .sort(([a], [b]) => a.localeCompare(b))
        .map(([bankName, count]) => ({ value: bankName, label: `${bankName} (${count})` })),
    [bankStats]
  )
  const filteredBankQuestions = useMemo(() => {
    const keyword = bankKeyword.trim().toLowerCase()

    return questions
      .filter((question) => question.type === bankType)
      .filter((question) => selectedBankNames.length === 0 || selectedBankNames.includes(normalizeBankName(question.bankName)))
      .filter((question) => {
        if (!keyword) return true
        return `${question.title} ${question.content}`.toLowerCase().includes(keyword)
      })
  }, [questions, bankType, selectedBankNames, bankKeyword])
  const availableBankQuestions = useMemo(
    () => filteredBankQuestions.filter((question) => !currentSourceIds.has(question.id)),
    [filteredBankQuestions, currentSourceIds]
  )
  const selectedBankQuestions = useMemo(
    () => filteredBankQuestions.filter((question) => selectedBankQuestionIds.includes(String(question.id))),
    [filteredBankQuestions, selectedBankQuestionIds]
  )

  const fetchPaper = async () => {
    if (numId < 0) return
    try {
      const res = await theoryAdminApi.getPaper(numId)
      setPaper({
        ...res.data,
        questions: (res.data.questions ?? []).map(normalizeQuestion),
      })
    } catch (err) {
      showErrorMsg(err, (key) => key)
    }
  }

  const fetchQuestions = async () => {
    try {
      const res = await theoryAdminApi.getQuestions()
      setQuestions(res.data ?? [])
    } catch (err) {
      showErrorMsg(err, (key) => key)
    }
  }

  useEffect(() => {
    fetchPaper()
    fetchQuestions()
  }, [numId])

  const setQuestion = (index: number, question: TheoryPaperQuestionEditModel) => {
    if (!paper) return
    setPaper({
      ...paper,
      questions: paper.questions.map((item, idx) => (idx === index ? question : item)),
    })
  }

  const addQuestion = (question?: TheoryPaperQuestionEditModel) => {
    if (!paper) return
    setPaper({
      ...paper,
      questions: [...paper.questions, question ?? defaultQuestion(paper.questions.length + 1)],
    })
  }

  const deleteQuestion = (index: number) => {
    if (!paper) return
    setPaper({
      ...paper,
      questions: paper.questions.filter((_, idx) => idx !== index).map((q, idx) => ({ ...q, order: idx + 1 })),
    })
  }

  const bankQuestionToPaperQuestion = (
    item: TheoryQuestionBankItemModel,
    order: number
  ): TheoryPaperQuestionEditModel => ({
    sourceQuestionId: item.id,
    type: item.type,
    bankName: normalizeBankName(item.bankName),
    title: item.title,
    content: item.content,
    options: [...item.options],
    answerIndexes: [...item.answerIndexes],
    score: Number(bankScore || 1),
    order,
  })

  const addFromBank = (items: TheoryQuestionBankItemModel[]) => {
    if (!paper || items.length === 0) return

    const existingSourceIds = new Set(
      paper.questions.map((question) => question.sourceQuestionId).filter((sourceId): sourceId is number => typeof sourceId === 'number')
    )
    const uniqueItems = items.filter((item) => !existingSourceIds.has(item.id))
    if (uniqueItems.length === 0) {
      showNotification({ color: 'yellow', message: '选中的题目已经在试卷中' })
      return
    }

    setPaper({
      ...paper,
      questions: [
        ...paper.questions,
        ...uniqueItems.map((item, index) => bankQuestionToPaperQuestion(item, paper.questions.length + index + 1)),
      ],
    })
    setSelectedBankQuestionIds((ids) => ids.filter((id) => !uniqueItems.some((item) => String(item.id) === id)))
    showNotification({ color: 'teal', message: `已添加 ${uniqueItems.length} 道题目`, icon: <Icon path={mdiCheck} size={1} /> })
  }

  const addRandomFromBank = () => {
    const count = Math.min(Number(randomCount || 0), availableBankQuestions.length)
    if (count <= 0) {
      showNotification({ color: 'yellow', message: '当前筛选条件下没有可添加的题目' })
      return
    }

    const selected = [...availableBankQuestions].sort(() => Math.random() - 0.5).slice(0, count)
    addFromBank(selected)
  }

  const saveToBank = async (question: TheoryPaperQuestionEditModel, index: number) => {
    setDisabled(true)
    try {
      const payload = {
        type: question.type,
        bankName: normalizeBankName(questions.find((item) => item.id === question.sourceQuestionId)?.bankName ?? question.bankName),
        title: question.title,
        content: question.content,
        options: question.options,
        answerIndexes: question.answerIndexes,
      }
      const res = question.sourceQuestionId
        ? await theoryAdminApi.updateQuestion(question.sourceQuestionId, payload)
        : await theoryAdminApi.createQuestion(payload)
      setQuestion(index, { ...question, sourceQuestionId: res.data.id })
      fetchQuestions()
      showNotification({ color: 'teal', message: '题库已更新', icon: <Icon path={mdiCheck} size={1} /> })
    } catch (err) {
      showErrorMsg(err, (key) => key)
    } finally {
      setDisabled(false)
    }
  }

  const importJson = (text: string) => {
    if (!paper) return
    const parsed = JSON.parse(text)
    const importedQuestions = Array.isArray(parsed) ? parsed : parsed.questions
    if (!Array.isArray(importedQuestions)) throw new Error('JSON 中没有 questions 数组')

    setPaper({
      ...paper,
      title: parsed.title ?? paper.title,
      description: parsed.description ?? paper.description,
      questions: importedQuestions.map(normalizeQuestion),
    })
    setJsonOpened(false)
    setJsonText('')
  }

  const onJsonFile = async (file: File | null) => {
    if (!file) return
    try {
      importJson(await file.text())
    } catch (err) {
      showErrorMsg(err, (key) => key)
    }
  }

  const savePaper = async () => {
    if (!paper) return
    setDisabled(true)
    try {
      const res = await theoryAdminApi.savePaper(numId, {
        title: paper.title,
        description: paper.description,
        questions: paper.questions,
      })
      setPaper({ ...res.data, questions: res.data.questions.map(normalizeQuestion) })
      showNotification({ color: 'teal', message: '试卷已保存', icon: <Icon path={mdiCheck} size={1} /> })
    } catch (err) {
      showErrorMsg(err, (key) => key)
    } finally {
      setDisabled(false)
    }
  }

  const publishPaper = async () => {
    setDisabled(true)
    try {
      const res = await theoryAdminApi.publishPaper(numId)
      setPaper({ ...res.data, questions: res.data.questions.map(normalizeQuestion) })
      showNotification({ color: 'teal', message: '试卷已发放', icon: <Icon path={mdiCheck} size={1} /> })
    } catch (err) {
      showErrorMsg(err, (key) => key)
    } finally {
      setDisabled(false)
    }
  }

  return (
    <WithGameEditTab
      isLoading={!paper || disabled}
      contentPos="right"
      head={
        <>
          <Button leftSection={<Icon path={mdiFileUploadOutline} size={1} />} variant="outline" onClick={() => setJsonOpened(true)}>
            JSON 导入
          </Button>
          <FileButton onChange={onJsonFile} accept="application/json,.json">
            {(props) => (
              <Button {...props} variant="outline" leftSection={<Icon path={mdiFileUploadOutline} size={1} />}>
                选择文件
              </Button>
            )}
          </FileButton>
          <Button leftSection={<Icon path={mdiContentSaveOutline} size={1} />} disabled={disabled} onClick={savePaper}>
            保存试卷
          </Button>
          <Button leftSection={<Icon path={mdiSendCheckOutline} size={1} />} disabled={disabled || !paper?.questions.length} onClick={publishPaper}>
            发放试卷
          </Button>
        </>
      }
    >
      <Stack gap="md">
        {paper?.isPublished && (
          <Alert color="teal" icon={<Icon path={mdiCheck} />}>
            试卷已发放，选手可以进入理论考试页面作答。已有提交后将不能再编辑试卷。
          </Alert>
        )}

        <SimpleGrid cols={{ base: 1, md: 3 }}>
          <TextInput
            label="试卷名称"
            value={paper?.title ?? ''}
            disabled={disabled}
            onChange={(event) => paper && setPaper({ ...paper, title: event.currentTarget.value })}
          />
          <NumberInput label="题目数量" value={paper?.questions.length ?? 0} disabled />
          <NumberInput label="总分" value={totalScore} disabled />
        </SimpleGrid>
        <Textarea
          label="试卷说明"
          minRows={2}
          value={paper?.description ?? ''}
          disabled={disabled}
          onChange={(event) => paper && setPaper({ ...paper, description: event.currentTarget.value })}
        />

        <Group justify="space-between">
          <Title order={4}>题目配置</Title>
          <Group>
            <Button variant="default" leftSection={<Icon path={mdiPlus} size={1} />} onClick={() => setBankOpened(true)}>
              从题库添加
            </Button>
            <Button leftSection={<Icon path={mdiPlus} size={1} />} onClick={() => addQuestion()}>
              新增题目
            </Button>
          </Group>
        </Group>

        {paper?.questions.length ? (
          <Stack gap="md">
            {paper.questions.map((question, index) => (
              <PaperQuestionEditor
                key={`${question.id ?? 'new'}-${index}`}
                question={question}
                index={index}
                disabled={disabled}
                onChange={(item) => setQuestion(index, item)}
                onDelete={() => deleteQuestion(index)}
                onSaveToBank={() => saveToBank(question, index)}
              />
            ))}
          </Stack>
        ) : (
          <Empty description="暂无题目，可以从题库选择题目，也可以导入 JSON 试卷。" />
        )}
      </Stack>

      <Modal opened={bankOpened} onClose={() => setBankOpened(false)} title="共享题库" size="90%">
        <Stack gap="md">
          <SimpleGrid cols={{ base: 1, md: 4 }}>
            <Select
              label="题型"
              data={questionTypeOptions}
              value={bankType}
              onChange={(value) => {
                if (!value) return
                setBankType(value as TheoryQuestionType)
                setSelectedBankNames([])
                setSelectedBankQuestionIds([])
              }}
            />
            <MultiSelect
              label="题库"
              data={bankNameOptions}
              value={selectedBankNames}
              placeholder="默认包含全部题库"
              searchable
              clearable
              onChange={(value) => {
                setSelectedBankNames(value)
                setSelectedBankQuestionIds([])
              }}
            />
            <NumberInput
              label="统一分值"
              min={1}
              value={bankScore}
              onChange={(value) => setBankScore(Number(value || 1))}
            />
            <TextInput
              label="搜索题干"
              value={bankKeyword}
              placeholder="按题干或说明过滤"
              onChange={(event) => setBankKeyword(event.currentTarget.value)}
            />
          </SimpleGrid>

          <Group justify="space-between" align="flex-end">
            <Group gap="xs">
              <Badge variant="light">符合条件 {filteredBankQuestions.length}</Badge>
              <Badge color="teal" variant="light">可添加 {availableBankQuestions.length}</Badge>
              <Badge color="gray" variant="light">已选择 {selectedBankQuestions.length}</Badge>
            </Group>
            <Group>
              <NumberInput
                label="随机抽取数量"
                min={1}
                max={availableBankQuestions.length || 1}
                value={randomCount}
                w={140}
                onChange={(value) => setRandomCount(Number(value || 1))}
              />
              <Button variant="outline" disabled={!availableBankQuestions.length} onClick={addRandomFromBank}>
                随机添加
              </Button>
              <Button disabled={!selectedBankQuestions.length} onClick={() => addFromBank(selectedBankQuestions)}>
                添加选中
              </Button>
            </Group>
          </Group>

          <ScrollArea h={430}>
            <Table striped highlightOnHover>
              <Table.Thead>
                <Table.Tr>
                  <Table.Th w={48}>
                    <Checkbox
                      disabled={!availableBankQuestions.length}
                      checked={
                        availableBankQuestions.length > 0 &&
                        availableBankQuestions.every((question) => selectedBankQuestionIds.includes(String(question.id)))
                      }
                      indeterminate={
                        availableBankQuestions.some((question) => selectedBankQuestionIds.includes(String(question.id))) &&
                        !availableBankQuestions.every((question) => selectedBankQuestionIds.includes(String(question.id)))
                      }
                      onChange={(event) => {
                        setSelectedBankQuestionIds(
                          event.currentTarget.checked ? availableBankQuestions.map((question) => String(question.id)) : []
                        )
                      }}
                    />
                  </Table.Th>
                  <Table.Th>题干</Table.Th>
                  <Table.Th>题库</Table.Th>
                  <Table.Th>答案</Table.Th>
                  <Table.Th>状态</Table.Th>
                </Table.Tr>
              </Table.Thead>
              <Table.Tbody>
                {filteredBankQuestions.map((item) => {
                  const added = currentSourceIds.has(item.id)

                  return (
                    <Table.Tr key={item.id}>
                      <Table.Td>
                        <Checkbox
                          disabled={added}
                          checked={selectedBankQuestionIds.includes(String(item.id))}
                          onChange={(event) =>
                            setSelectedBankQuestionIds((ids) =>
                              event.currentTarget.checked
                                ? [...ids, String(item.id)]
                                : ids.filter((id) => id !== String(item.id))
                            )
                          }
                        />
                      </Table.Td>
                      <Table.Td>
                        <Text fw={600}>{item.title}</Text>
                        {item.content && (
                          <Text size="xs" c="dimmed" lineClamp={1}>
                            {item.content}
                          </Text>
                        )}
                      </Table.Td>
                      <Table.Td>{normalizeBankName(item.bankName)}</Table.Td>
                      <Table.Td>{getAnswerLabel({ ...item, score: 1, order: 1 })}</Table.Td>
                      <Table.Td>
                        {added ? (
                          <Badge color="gray" variant="light">
                            已添加
                          </Badge>
                        ) : (
                          <Button size="xs" variant="subtle" onClick={() => addFromBank([item])}>
                            添加
                          </Button>
                        )}
                      </Table.Td>
                    </Table.Tr>
                  )
                })}
              </Table.Tbody>
            </Table>
          </ScrollArea>
        </Stack>
      </Modal>

      <Modal opened={jsonOpened} onClose={() => setJsonOpened(false)} title="JSON 导入" size="lg">
        <Stack>
          <Textarea
            minRows={14}
            value={jsonText}
            onChange={(event) => setJsonText(event.currentTarget.value)}
            placeholder='{"title":"理论考试","questions":[{"type":"SingleChoice","title":"...","options":["A","B"],"answerIndexes":[0],"score":5}]}'
          />
          <Button
            disabled={!jsonText.trim()}
            onClick={() => {
              try {
                importJson(jsonText)
              } catch (err) {
                showErrorMsg(err, (key) => key)
              }
            }}
          >
            导入
          </Button>
        </Stack>
      </Modal>
    </WithGameEditTab>
  )
}

export default TheoryPaper
