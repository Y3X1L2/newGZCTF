import {
  ActionIcon,
  Badge,
  Button,
  Checkbox,
  FileButton,
  Group,
  Modal,
  Radio,
  ScrollArea,
  Select,
  SimpleGrid,
  Stack,
  Table,
  Text,
  Textarea,
  TextInput,
  Tooltip,
} from '@mantine/core'
import { showNotification } from '@mantine/notifications'
import {
  mdiCheck,
  mdiDeleteOutline,
  mdiFileUploadOutline,
  mdiMagnify,
  mdiPencilOutline,
  mdiPlus,
  mdiRefresh,
} from '@mdi/js'
import { Icon } from '@mdi/react'
import { FC, useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { ActionIconWithConfirm } from '@Components/ActionIconWithConfirm'
import { Empty } from '@Components/Empty'
import { AdminPage } from '@Components/admin/AdminPage'
import { YinyuModalBody, YinyuPanel, YinyuTableShell } from '@Components/yinyu/YinyuUI'
import { showErrorMsg } from '@Utils/Shared'
import tableClasses from '@Styles/Table.module.css'
import {
  theoryAdminApi,
  TheoryQuestionBankItemModel,
  TheoryQuestionEditModel,
  TheoryQuestionType,
} from '../../Api/TheoryApi'

const DEFAULT_BANK_NAME = 'Default'

const questionTypeOptions = [
  { value: TheoryQuestionType.SingleChoice, label: '单选题' },
  { value: TheoryQuestionType.MultipleChoice, label: '多选题' },
  { value: TheoryQuestionType.TrueFalse, label: '判断题' },
]

const filterTypeOptions = [{ value: 'All', label: '全部题型' }, ...questionTypeOptions]

const normalizeBankName = (bankName?: string | null) => {
  const value = bankName?.trim()
  return value ? value.slice(0, 128) : DEFAULT_BANK_NAME
}

const questionTypeLabel = (type: TheoryQuestionType) =>
  type === TheoryQuestionType.MultipleChoice ? '多选题' : type === TheoryQuestionType.TrueFalse ? '判断题' : '单选题'

const questionTypeSemantic = (type: TheoryQuestionType) =>
  type === TheoryQuestionType.MultipleChoice ? 'type-multiple' : type === TheoryQuestionType.TrueFalse ? 'type-judge' : 'type-single'

const emptyQuestion = (): TheoryQuestionEditModel => ({
  type: TheoryQuestionType.SingleChoice,
  bankName: DEFAULT_BANK_NAME,
  title: '',
  content: '',
  options: ['选项 A', '选项 B'],
  answerIndexes: [0],
})

const normalizeAnswerIndexes = (indexes: number[], optionCount: number, multiple: boolean) => {
  const values = [
    ...new Set(indexes.map(Number).filter((index) => Number.isInteger(index) && index >= 0 && index < optionCount)),
  ]
  if (values.length === 0) return [0]
  return multiple ? values.sort((a, b) => a - b) : [values[0]]
}

const normalizeDraft = (question: TheoryQuestionEditModel): TheoryQuestionEditModel => {
  const type = question.type ?? TheoryQuestionType.SingleChoice
  const options =
    type === TheoryQuestionType.TrueFalse
      ? ['正确', '错误']
      : question.options.map((option) => option.trim()).filter(Boolean)
  const safeOptions = options.length >= 2 ? options : ['选项 A', '选项 B']

  return {
    type,
    bankName: normalizeBankName(question.bankName),
    title: question.title.trim(),
    content: question.content.trim(),
    options: safeOptions,
    answerIndexes: normalizeAnswerIndexes(
      question.answerIndexes,
      safeOptions.length,
      type === TheoryQuestionType.MultipleChoice
    ),
  }
}

const getAnswerLabel = (question: TheoryQuestionEditModel) =>
  question.answerIndexes
    .map((index) => question.options[index])
    .filter(Boolean)
    .join('、') || '-'

const normalizeType = (value: unknown): TheoryQuestionType => {
  const raw = String(value ?? '').toLowerCase()
  if (raw.includes('multiple') || raw.includes('multi') || raw.includes('多选')) return TheoryQuestionType.MultipleChoice
  if (raw.includes('true') || raw.includes('false') || raw.includes('judge') || raw.includes('判断'))
    return TheoryQuestionType.TrueFalse
  return TheoryQuestionType.SingleChoice
}

const parseAnswerIndexes = (raw: unknown, options: string[]): number[] => {
  const parseOne = (value: unknown): number[] => {
    if (typeof value === 'number') return [value]
    if (typeof value === 'boolean') return [value ? 0 : 1]

    const text = String(value ?? '').trim()
    if (!text) return []

    const optionIndex = options.findIndex((option) => option.trim() === text)
    if (optionIndex >= 0) return [optionIndex]

    const upper = text.toUpperCase()
    const letter = upper.charCodeAt(0) - 65
    if (/^[A-Z]$/.test(upper) && letter >= 0) return [letter]

    const numeric = Number(text)
    return Number.isInteger(numeric) ? [numeric] : []
  }

  if (Array.isArray(raw)) return raw.flatMap(parseOne)
  if (typeof raw === 'string' && /[,，;；、\s]/.test(raw)) return raw.split(/[,，;；、\s]+/).flatMap(parseOne)
  return parseOne(raw)
}

const parseImportedQuestions = (text: string, bankName: string): TheoryQuestionEditModel[] => {
  const parsed = JSON.parse(text)
  const source = Array.isArray(parsed) ? parsed : parsed.questions
  if (!Array.isArray(source)) throw new Error('JSON 中没有 questions 数组')

  return source.map((item, index) => {
    const type = normalizeType(item.type ?? item.questionType)
    const options =
      type === TheoryQuestionType.TrueFalse
        ? ['正确', '错误']
        : (item.options ?? item.choices ?? item.answers ?? [])
            .map((option: unknown) => String(option).trim())
            .filter(Boolean)
    const safeOptions = options.length >= 2 ? options : ['选项 A', '选项 B']
    const answerRaw = item.answerIndexes ?? item.answerIndex ?? item.correctIndexes ?? item.correctIndex ?? item.answer

    return normalizeDraft({
      type,
      bankName: normalizeBankName(item.bankName ?? bankName),
      title: String(item.title ?? item.question ?? item.stem ?? `导入题目 ${index + 1}`),
      content: String(item.content ?? item.description ?? item.analysis ?? ''),
      options: safeOptions,
      answerIndexes: parseAnswerIndexes(answerRaw, safeOptions),
    })
  })
}

const QuestionEditorModal: FC<{
  opened: boolean
  question?: TheoryQuestionEditModel
  loading: boolean
  onClose: () => void
  onSave: (question: TheoryQuestionEditModel) => Promise<void>
}> = ({ opened, question, loading, onClose, onSave }) => {
  const [draft, setDraft] = useState<TheoryQuestionEditModel>(emptyQuestion())

  useEffect(() => {
    if (opened) setDraft(normalizeDraft(question ?? emptyQuestion()))
  }, [opened, question])

  const setType = (type: TheoryQuestionType) => {
    setDraft(
      normalizeDraft({
        ...draft,
        type,
        options: type === TheoryQuestionType.TrueFalse ? ['正确', '错误'] : draft.options,
        answerIndexes: [0],
      })
    )
  }

  const setOption = (index: number, value: string) => {
    setDraft({ ...draft, options: draft.options.map((option, idx) => (idx === index ? value : option)) })
  }

  const removeOption = (index: number) => {
    const options = draft.options.filter((_, idx) => idx !== index)
    setDraft(
      normalizeDraft({
        ...draft,
        options,
        answerIndexes: draft.answerIndexes
          .filter((answerIndex) => answerIndex !== index)
          .map((answerIndex) => (answerIndex > index ? answerIndex - 1 : answerIndex)),
      })
    )
  }

  const multiple = draft.type === TheoryQuestionType.MultipleChoice
  const trueFalse = draft.type === TheoryQuestionType.TrueFalse

  return (
    <Modal opened={opened} onClose={onClose} title="题目编辑" size="xl">
      <YinyuModalBody p="md">
        <Stack gap="md">
          <SimpleGrid cols={{ base: 1, md: 2 }}>
            <TextInput
              label="题库"
              value={draft.bankName ?? DEFAULT_BANK_NAME}
              onChange={(event) => setDraft({ ...draft, bankName: event.currentTarget.value })}
            />
            <Select
              label="题型"
              data={questionTypeOptions}
              value={draft.type}
              onChange={(value) => value && setType(value as TheoryQuestionType)}
            />
          </SimpleGrid>

          <TextInput
            required
            label="题干"
            value={draft.title}
            onChange={(event) => setDraft({ ...draft, title: event.currentTarget.value })}
          />
          <Textarea
            label="说明"
            autosize
            minRows={2}
            value={draft.content}
            onChange={(event) => setDraft({ ...draft, content: event.currentTarget.value })}
          />

          <Stack gap="xs">
            <Group justify="space-between">
              <Text fw={700}>选项与答案</Text>
              {!trueFalse && (
                <Button
                  size="xs"
                  variant="default"
                  leftSection={<Icon path={mdiPlus} size={0.75} />}
                  onClick={() =>
                    setDraft({
                      ...draft,
                      options: [...draft.options, `选项 ${String.fromCharCode(65 + draft.options.length)}`],
                    })
                  }
                >
                  添加选项
                </Button>
              )}
            </Group>

            {multiple ? (
              <Checkbox.Group
                value={draft.answerIndexes.map(String)}
                onChange={(values) =>
                  setDraft({
                    ...draft,
                    answerIndexes: normalizeAnswerIndexes(values.map(Number), draft.options.length, true),
                  })
                }
              >
                <Stack gap="xs">
                  {draft.options.map((option, index) => (
                    <Group key={index} wrap="nowrap" align="center">
                      <Checkbox value={String(index)} />
                      <TextInput
                        value={option}
                        onChange={(event) => setOption(index, event.currentTarget.value)}
                        disabled={trueFalse}
                        style={{ flex: 1 }}
                      />
                      {!trueFalse && draft.options.length > 2 && (
                        <ActionIcon color="red" variant="subtle" onClick={() => removeOption(index)}>
                          <Icon path={mdiDeleteOutline} size={0.8} />
                        </ActionIcon>
                      )}
                    </Group>
                  ))}
                </Stack>
              </Checkbox.Group>
            ) : (
              <Radio.Group
                value={String(draft.answerIndexes[0] ?? 0)}
                onChange={(value) => setDraft({ ...draft, answerIndexes: [Number(value)] })}
              >
                <Stack gap="xs">
                  {draft.options.map((option, index) => (
                    <Group key={index} wrap="nowrap" align="center">
                      <Radio value={String(index)} />
                      <TextInput
                        value={option}
                        onChange={(event) => setOption(index, event.currentTarget.value)}
                        disabled={trueFalse}
                        style={{ flex: 1 }}
                      />
                      {!trueFalse && draft.options.length > 2 && (
                        <ActionIcon color="red" variant="subtle" onClick={() => removeOption(index)}>
                          <Icon path={mdiDeleteOutline} size={0.8} />
                        </ActionIcon>
                      )}
                    </Group>
                  ))}
                </Stack>
              </Radio.Group>
            )}
          </Stack>

          <Group justify="right">
            <Button variant="default" onClick={onClose}>
              取消
            </Button>
            <Button
              loading={loading}
              leftSection={<Icon path={mdiCheck} size={1} />}
              onClick={() => onSave(normalizeDraft(draft))}
            >
              保存题目
            </Button>
          </Group>
        </Stack>
      </YinyuModalBody>
    </Modal>
  )
}

const TheoryBank: FC = () => {
  const { t } = useTranslation()
  const [questions, setQuestions] = useState<TheoryQuestionBankItemModel[]>([])
  const [loading, setLoading] = useState(false)
  const [keyword, setKeyword] = useState('')
  const [typeFilter, setTypeFilter] = useState<string>('All')
  const [bankFilter, setBankFilter] = useState<string>('All')
  const [editorOpened, setEditorOpened] = useState(false)
  const [jsonOpened, setJsonOpened] = useState(false)
  const [activeQuestion, setActiveQuestion] = useState<TheoryQuestionBankItemModel | undefined>()
  const [jsonText, setJsonText] = useState('')
  const [jsonBankName, setJsonBankName] = useState(DEFAULT_BANK_NAME)

  const fetchQuestions = async () => {
    try {
      setLoading(true)
      const res = await theoryAdminApi.getQuestions(keyword.trim() || undefined)
      setQuestions(res.data)
    } catch (err) {
      showErrorMsg(err, t)
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    fetchQuestions()
  }, [])

  const bankOptions = useMemo(() => {
    const banks = [...new Set(questions.map((question) => normalizeBankName(question.bankName)))].sort()
    return [{ value: 'All', label: '全部题库' }, ...banks.map((bank) => ({ value: bank, label: bank }))]
  }, [questions])

  const typeStats = useMemo(
    () => ({
      [TheoryQuestionType.SingleChoice]: questions.filter(
        (question) => question.type === TheoryQuestionType.SingleChoice
      ).length,
      [TheoryQuestionType.MultipleChoice]: questions.filter(
        (question) => question.type === TheoryQuestionType.MultipleChoice
      ).length,
      [TheoryQuestionType.TrueFalse]: questions.filter((question) => question.type === TheoryQuestionType.TrueFalse)
        .length,
    }),
    [questions]
  )

  const filteredQuestions = useMemo(
    () =>
      questions.filter((question) => {
        const matchesType = typeFilter === 'All' || question.type === typeFilter
        const matchesBank = bankFilter === 'All' || normalizeBankName(question.bankName) === bankFilter
        return matchesType && matchesBank
      }),
    [bankFilter, questions, typeFilter]
  )

  const openCreate = () => {
    setActiveQuestion(undefined)
    setEditorOpened(true)
  }

  const openEdit = (question: TheoryQuestionBankItemModel) => {
    setActiveQuestion(question)
    setEditorOpened(true)
  }

  const saveQuestion = async (question: TheoryQuestionEditModel) => {
    if (!question.title.trim()) return

    try {
      setLoading(true)
      if (activeQuestion?.id) {
        await theoryAdminApi.updateQuestion(activeQuestion.id, question)
        showNotification({ color: 'teal', message: '题目已更新', icon: <Icon path={mdiCheck} size={1} /> })
      } else {
        await theoryAdminApi.createQuestion(question)
        showNotification({ color: 'teal', message: '题目已创建', icon: <Icon path={mdiCheck} size={1} /> })
      }

      setEditorOpened(false)
      await fetchQuestions()
    } catch (err) {
      showErrorMsg(err, t)
    } finally {
      setLoading(false)
    }
  }

  const deleteQuestion = async (question: TheoryQuestionBankItemModel) => {
    try {
      setLoading(true)
      await theoryAdminApi.deleteQuestion(question.id)
      showNotification({ color: 'teal', message: '题目已删除', icon: <Icon path={mdiCheck} size={1} /> })
      setQuestions((items) => items.filter((item) => item.id !== question.id))
    } catch (err) {
      showErrorMsg(err, t)
    } finally {
      setLoading(false)
    }
  }

  const onJsonFile = async (file: File | null) => {
    if (!file) return
    setJsonText(await file.text())
  }

  const importJson = async () => {
    try {
      setLoading(true)
      const imported = parseImportedQuestions(jsonText, jsonBankName)

      for (const question of imported) {
        await theoryAdminApi.createQuestion(question)
      }

      showNotification({
        color: 'teal',
        message: `已导入 ${imported.length} 道题目`,
        icon: <Icon path={mdiCheck} size={1} />,
      })
      setJsonText('')
      setJsonOpened(false)
      await fetchQuestions()
    } catch (err) {
      showErrorMsg(err, t)
    } finally {
      setLoading(false)
    }
  }

  return (
    <AdminPage
      isLoading={loading && !questions.length}
      head={
        <>
          <TextInput
            w="34%"
            leftSection={<Icon path={mdiMagnify} size={1} />}
            placeholder="搜索题干或说明"
            value={keyword}
            onChange={(event) => setKeyword(event.currentTarget.value)}
            onKeyDown={(event) => {
              if (event.key === 'Enter') fetchQuestions()
            }}
          />
          <Group justify="right">
            <Button
              variant="default"
              leftSection={<Icon path={mdiRefresh} size={1} />}
              loading={loading}
              onClick={fetchQuestions}
            >
              刷新
            </Button>
            <Button
              variant="default"
              leftSection={<Icon path={mdiFileUploadOutline} size={1} />}
              onClick={() => setJsonOpened(true)}
            >
              JSON 导入
            </Button>
            <Button leftSection={<Icon path={mdiPlus} size={1} />} onClick={openCreate}>
              新增题目
            </Button>
          </Group>
        </>
      }
    >
      <Stack gap="md" w="100%">
        <YinyuPanel p="md" w="100%">
          <SimpleGrid cols={{ base: 1, md: 5 }}>
            <Select label="题型" data={filterTypeOptions} value={typeFilter} onChange={(value) => setTypeFilter(value ?? 'All')} />
            <Select
              label="题库"
              data={bankOptions}
              value={bankFilter}
              onChange={(value) => setBankFilter(value ?? 'All')}
              searchable
            />
            <Badge size="lg" variant="light" color="teal" className="yy-semantic-badge" data-semantic="type-single">
              单选 {typeStats[TheoryQuestionType.SingleChoice]}
            </Badge>
            <Badge size="lg" variant="light" color="blue" className="yy-semantic-badge" data-semantic="type-multiple">
              多选 {typeStats[TheoryQuestionType.MultipleChoice]}
            </Badge>
            <Badge size="lg" variant="light" color="grape" className="yy-semantic-badge" data-semantic="type-judge">
              判断 {typeStats[TheoryQuestionType.TrueFalse]}
            </Badge>
          </SimpleGrid>
        </YinyuPanel>

        <YinyuTableShell p="xs" w="100%">
          <ScrollArea offsetScrollbars scrollbarSize={4} h="calc(100vh - 245px)">
            <Table className={tableClasses.table} striped highlightOnHover>
              <Table.Thead>
                <Table.Tr>
                  <Table.Th miw={120}>题型</Table.Th>
                  <Table.Th miw={140}>题库</Table.Th>
                  <Table.Th miw={360}>题干</Table.Th>
                  <Table.Th miw={220}>答案</Table.Th>
                  <Table.Th miw={130}>更新时间</Table.Th>
                  <Table.Th w={100} />
                </Table.Tr>
              </Table.Thead>
              <Table.Tbody>
                {filteredQuestions.map((question) => (
                  <Table.Tr key={question.id}>
                    <Table.Td>
                      <Badge
                        variant="light"
                        color={
                          question.type === TheoryQuestionType.MultipleChoice
                            ? 'blue'
                            : question.type === TheoryQuestionType.TrueFalse
                              ? 'grape'
                              : 'teal'
                        }
                        className="yy-semantic-badge"
                        data-semantic={questionTypeSemantic(question.type)}
                      >
                        {questionTypeLabel(question.type)}
                      </Badge>
                    </Table.Td>
                    <Table.Td>{normalizeBankName(question.bankName)}</Table.Td>
                    <Table.Td>
                      <Text fw={600} lineClamp={1}>
                        {question.title}
                      </Text>
                      {question.content && (
                        <Text size="xs" className="yy-readable-text" lineClamp={1}>
                          {question.content}
                        </Text>
                      )}
                    </Table.Td>
                    <Table.Td>
                      <Text size="sm" lineClamp={2}>
                        {getAnswerLabel(question)}
                      </Text>
                    </Table.Td>
                    <Table.Td>{new Date(question.updatedAt).toLocaleString('zh-CN')}</Table.Td>
                    <Table.Td align="right">
                      <Group wrap="nowrap" gap="xs" justify="right">
                        <Tooltip label="编辑">
                          <ActionIcon color="blue" onClick={() => openEdit(question)}>
                            <Icon path={mdiPencilOutline} size={1} />
                          </ActionIcon>
                        </Tooltip>
                        <ActionIconWithConfirm
                          iconPath={mdiDeleteOutline}
                          color="alert"
                          message={`确定删除题目「${question.title}」？`}
                          disabled={loading}
                          onClick={() => deleteQuestion(question)}
                        />
                      </Group>
                    </Table.Td>
                  </Table.Tr>
                ))}
              </Table.Tbody>
            </Table>
            {!filteredQuestions.length && <Empty description="当前筛选条件下没有题目" />}
          </ScrollArea>
        </YinyuTableShell>
      </Stack>

      <QuestionEditorModal
        opened={editorOpened}
        question={activeQuestion}
        loading={loading}
        onClose={() => setEditorOpened(false)}
        onSave={saveQuestion}
      />

      <Modal opened={jsonOpened} onClose={() => setJsonOpened(false)} title="批量导入题库 JSON" size="lg">
        <YinyuModalBody p="md">
          <Stack gap="md">
            <Group justify="space-between">
              <Text size="sm" className="yy-readable-text">
                支持数组或包含 questions 数组的对象，answerIndexes 使用从 0 开始的选项下标。
              </Text>
              <FileButton onChange={onJsonFile} accept="application/json,.json">
                {(props) => (
                  <Button {...props} variant="default" leftSection={<Icon path={mdiFileUploadOutline} size={1} />}>
                    选择 JSON 文件
                  </Button>
                )}
              </FileButton>
            </Group>
            <TextInput
              label="默认题库"
              value={jsonBankName}
              onChange={(event) => setJsonBankName(event.currentTarget.value)}
            />
            <Textarea
              autosize
              minRows={12}
              label="JSON 内容"
              value={jsonText}
              onChange={(event) => setJsonText(event.currentTarget.value)}
              placeholder='[{"type":"SingleChoice","title":"题干","options":["A","B"],"answerIndexes":[0]}]'
            />
            <Group justify="right">
              <Button variant="default" onClick={() => setJsonOpened(false)}>
                取消
              </Button>
              <Button loading={loading} onClick={importJson}>
                导入
              </Button>
            </Group>
          </Stack>
        </YinyuModalBody>
      </Modal>
    </AdminPage>
  )
}

export default TheoryBank
