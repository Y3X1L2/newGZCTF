import {
  ActionIcon,
  Badge,
  Button,
  Checkbox,
  FileButton,
  Group,
  Modal,
  Paper,
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
import { ActionIconWithConfirm } from '@Components/ActionIconWithConfirm'
import { AdminPage } from '@Components/admin/AdminPage'
import { Empty } from '@Components/Empty'
import { showErrorMsg } from '@Utils/Shared'
import {
  theoryAdminApi,
  TheoryQuestionBankItemModel,
  TheoryQuestionEditModel,
  TheoryQuestionType,
} from '../../Api/TheoryApi'
import tableClasses from '@Styles/Table.module.css'

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

const emptyQuestion = (): TheoryQuestionEditModel => ({
  type: TheoryQuestionType.SingleChoice,
  bankName: DEFAULT_BANK_NAME,
  title: '',
  content: '',
  options: ['选项 A', '选项 B'],
  answerIndexes: [0],
})

const normalizeAnswerIndexes = (indexes: number[], optionCount: number, multiple: boolean) => {
  const values = [...new Set(indexes.map(Number).filter((index) => Number.isInteger(index) && index >= 0 && index < optionCount))]
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
    answerIndexes: normalizeAnswerIndexes(question.answerIndexes, safeOptions.length, type === TheoryQuestionType.MultipleChoice),
  }
}

const getAnswerLabel = (question: TheoryQuestionEditModel) =>
  question.answerIndexes
    .map((index) => question.options[index])
    .filter(Boolean)
    .join('、') || '-'

const normalizeType = (value: unknown): TheoryQuestionType => {
  const raw = String(value ?? '').toLowerCase()
  if (raw.includes('multiple') || raw.includes('multi') || raw.includes('多')) return TheoryQuestionType.MultipleChoice
  if (raw.includes('true') || raw.includes('false') || raw.includes('judge') || raw.includes('判断')) return TheoryQuestionType.TrueFalse
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

    const letter = text.toUpperCase().charCodeAt(0) - 65
    if (/^[A-Z]$/.test(text.toUpperCase()) && letter >= 0) return [letter]

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
        : (item.options ?? item.choices ?? item.answers ?? []).map((option: unknown) => String(option).trim()).filter(Boolean)
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
          label="补充说明"
          minRows={2}
          value={draft.content}
          onChange={(event) => setDraft({ ...draft, content: event.currentTarget.value })}
        />

        <Stack gap="xs">
          <Group justify="space-between">
            <Text fw={600}>选项与答案</Text>
            {!trueFalse && (
              <Button size="xs" variant="default" onClick={() => setDraft({ ...draft, options: [...draft.options, ''] })}>
                添加选项
              </Button>
            )}
          </Group>

          {multiple ? (
            <Checkbox.Group
              value={draft.answerIndexes.map(String)}
              onChange={(values) =>
                setDraft({ ...draft, answerIndexes: normalizeAnswerIndexes(values.map(Number), draft.options.length, true) })
              }
            >
              <Stack gap="xs">
                {draft.options.map((option, index) => (
                  <Group key={index} wrap="nowrap">
                    <Checkbox value={String(index)} />
                    <TextInput
                      flex={1}
                      value={option}
                      disabled={trueFalse}
                      onChange={(event) => setOption(index, event.currentTarget.value)}
                    />
                    {!trueFalse && (
                      <Button variant="subtle" color="red" disabled={draft.options.length <= 2} onClick={() => removeOption(index)}>
                        删除
                      </Button>
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
                  <Group key={index} wrap="nowrap">
                    <Radio value={String(index)} />
                    <TextInput
                      flex={1}
                      value={option}
                      disabled={trueFalse}
                      onChange={(event) => setOption(index, event.currentTarget.value)}
                    />
                    {!trueFalse && (
                      <Button variant="subtle" color="red" disabled={draft.options.length <= 2} onClick={() => removeOption(index)}>
                        删除
                      </Button>
                    )}
                  </Group>
                ))}
              </Stack>
            </Radio.Group>
          )}
        </Stack>

        <Group justify="flex-end">
          <Button variant="default" disabled={loading} onClick={onClose}>
            取消
          </Button>
          <Button
            loading={loading}
            onClick={() => {
              const normalized = normalizeDraft(draft)
              if (!normalized.title) {
                showNotification({ color: 'red', message: '题干不能为空' })
                return
              }
              onSave(normalized)
            }}
          >
            保存
          </Button>
        </Group>
      </Stack>
    </Modal>
  )
}

const TheoryBank: FC = () => {
  const [questions, setQuestions] = useState<TheoryQuestionBankItemModel[]>([])
  const [loading, setLoading] = useState(false)
  const [editorOpened, setEditorOpened] = useState(false)
  const [activeQuestion, setActiveQuestion] = useState<TheoryQuestionBankItemModel>()
  const [keyword, setKeyword] = useState('')
  const [typeFilter, setTypeFilter] = useState<string>('All')
  const [bankFilter, setBankFilter] = useState<string>('All')
  const [jsonOpened, setJsonOpened] = useState(false)
  const [jsonText, setJsonText] = useState('')
  const [importBankName, setImportBankName] = useState(DEFAULT_BANK_NAME)

  const fetchQuestions = async () => {
    setLoading(true)
    try {
      const res = await theoryAdminApi.getQuestions(undefined, 5000)
      setQuestions(res.data ?? [])
    } catch (err) {
      showErrorMsg(err, (key) => key)
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    fetchQuestions()
  }, [])

  const bankOptions = useMemo(() => {
    const banks = [...new Set(questions.map((question) => normalizeBankName(question.bankName)))].sort((a, b) => a.localeCompare(b))
    return [{ value: 'All', label: '全部题库' }, ...banks.map((bank) => ({ value: bank, label: bank }))]
  }, [questions])

  const filteredQuestions = useMemo(() => {
    const hint = keyword.trim().toLowerCase()

    return questions.filter((question) => {
      if (typeFilter !== 'All' && question.type !== typeFilter) return false
      if (bankFilter !== 'All' && normalizeBankName(question.bankName) !== bankFilter) return false
      if (!hint) return true
      return `${question.title} ${question.content}`.toLowerCase().includes(hint)
    })
  }, [questions, keyword, typeFilter, bankFilter])

  const typeStats = useMemo(
    () =>
      questions.reduce(
        (stats, question) => {
          stats[question.type] += 1
          return stats
        },
        {
          [TheoryQuestionType.SingleChoice]: 0,
          [TheoryQuestionType.MultipleChoice]: 0,
          [TheoryQuestionType.TrueFalse]: 0,
        }
      ),
    [questions]
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
    setLoading(true)
    try {
      const res = activeQuestion
        ? await theoryAdminApi.updateQuestion(activeQuestion.id, question)
        : await theoryAdminApi.createQuestion(question)
      setQuestions((items) =>
        activeQuestion
          ? items.map((item) => (item.id === activeQuestion.id ? res.data : item))
          : [res.data, ...items]
      )
      showNotification({ color: 'teal', message: activeQuestion ? '题目已更新' : '题目已创建', icon: <Icon path={mdiCheck} size={1} /> })
      setEditorOpened(false)
    } catch (err) {
      showErrorMsg(err, (key) => key)
    } finally {
      setLoading(false)
    }
  }

  const deleteQuestion = async (question: TheoryQuestionBankItemModel) => {
    setLoading(true)
    try {
      await theoryAdminApi.deleteQuestion(question.id)
      setQuestions((items) => items.filter((item) => item.id !== question.id))
      showNotification({ color: 'teal', message: '题目已删除', icon: <Icon path={mdiCheck} size={1} /> })
    } catch (err) {
      showErrorMsg(err, (key) => key)
    } finally {
      setLoading(false)
    }
  }

  const importQuestions = async (text: string) => {
    const imported = parseImportedQuestions(text, importBankName)
    if (!imported.length) {
      showNotification({ color: 'yellow', message: '没有可导入的题目' })
      return
    }

    setLoading(true)
    try {
      const created: TheoryQuestionBankItemModel[] = []
      for (const question of imported) {
        const res = await theoryAdminApi.createQuestion(question)
        created.push(res.data)
      }
      setQuestions((items) => [...created.reverse(), ...items])
      setJsonOpened(false)
      setJsonText('')
      showNotification({ color: 'teal', message: `已导入 ${created.length} 道题目`, icon: <Icon path={mdiCheck} size={1} /> })
    } catch (err) {
      showErrorMsg(err, (key) => key)
    } finally {
      setLoading(false)
    }
  }

  const onJsonFile = async (file: File | null) => {
    if (!file) return
    setJsonText(await file.text())
    setJsonOpened(true)
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
          />
          <Group justify="right">
            <Button variant="default" leftSection={<Icon path={mdiRefresh} size={1} />} loading={loading} onClick={fetchQuestions}>
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
        <Paper shadow="md" p="md" w="100%">
          <SimpleGrid cols={{ base: 1, md: 5 }}>
            <Select label="题型" data={filterTypeOptions} value={typeFilter} onChange={(value) => setTypeFilter(value ?? 'All')} />
            <Select label="题库" data={bankOptions} value={bankFilter} onChange={(value) => setBankFilter(value ?? 'All')} searchable />
            <Badge size="lg" variant="light" color="teal">
              单选 {typeStats[TheoryQuestionType.SingleChoice]}
            </Badge>
            <Badge size="lg" variant="light" color="blue">
              多选 {typeStats[TheoryQuestionType.MultipleChoice]}
            </Badge>
            <Badge size="lg" variant="light" color="grape">
              判断 {typeStats[TheoryQuestionType.TrueFalse]}
            </Badge>
          </SimpleGrid>
        </Paper>

        <Paper shadow="md" p="xs" w="100%">
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
                      <Badge variant="light">{questionTypeLabel(question.type)}</Badge>
                    </Table.Td>
                    <Table.Td>{normalizeBankName(question.bankName)}</Table.Td>
                    <Table.Td>
                      <Text fw={600} lineClamp={1}>
                        {question.title}
                      </Text>
                      {question.content && (
                        <Text size="xs" c="dimmed" lineClamp={1}>
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
        </Paper>
      </Stack>

      <QuestionEditorModal
        opened={editorOpened}
        question={activeQuestion}
        loading={loading}
        onClose={() => setEditorOpened(false)}
        onSave={saveQuestion}
      />

      <Modal opened={jsonOpened} onClose={() => setJsonOpened(false)} title="批量导入题库 JSON" size="lg">
        <Stack gap="md">
          <Group justify="space-between">
            <Text size="sm" c="dimmed">
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
            value={importBankName}
            onChange={(event) => setImportBankName(event.currentTarget.value)}
          />
          <Textarea
            minRows={14}
            value={jsonText}
            onChange={(event) => setJsonText(event.currentTarget.value)}
            placeholder='{"questions":[{"type":"SingleChoice","bankName":"Web 基础","title":"...","options":["A","B"],"answerIndexes":[0]}]}'
          />
          <Group justify="flex-end">
            <Button variant="default" disabled={loading} onClick={() => setJsonOpened(false)}>
              取消
            </Button>
            <Button loading={loading} disabled={!jsonText.trim()} onClick={() => importQuestions(jsonText)}>
              导入
            </Button>
          </Group>
        </Stack>
      </Modal>
    </AdminPage>
  )
}

export default TheoryBank
