import {
  ActionIcon,
  Badge,
  Button,
  FileButton,
  Group,
  Modal,
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
import { mdiDeleteOutline, mdiFileUploadOutline, mdiMagnify, mdiPencilOutline, mdiPlus, mdiRefresh } from '@mdi/js'
import { Icon } from '@mdi/react'
import { FC, useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { ActionIconWithConfirm } from '@Components/ActionIconWithConfirm'
import { Empty } from '@Components/Empty'
import { YinyuModalBody, YinyuPanel } from '@Components/yinyu/YinyuUI'
import { showErrorMsg } from '@Utils/Shared'
import {
  TrainingCourseTheoryQuestionModel,
  trainingCourseAdminApi,
} from '@Utils/TrainingApi'
import { TheoryQuestionEditModel, TheoryQuestionType } from '../../Api/TheoryApi'
import {
  DEFAULT_THEORY_BANK_NAME,
  TheoryQuestionEditorModal,
  getTheoryAnswerLabel,
  parseImportedTheoryQuestions,
  theoryQuestionTypeFilterOptions,
  theoryQuestionTypeLabel,
} from './CourseTheoryQuestionTools'

export const CourseTheoryBankPanel: FC<{ courseId: number }> = ({ courseId }) => {
  const { t } = useTranslation()
  const [questions, setQuestions] = useState<TrainingCourseTheoryQuestionModel[]>([])
  const [loading, setLoading] = useState(false)
  const [keyword, setKeyword] = useState('')
  const [typeFilter, setTypeFilter] = useState<string>('All')
  const [bankFilter, setBankFilter] = useState<string>('All')
  const [editorOpened, setEditorOpened] = useState(false)
  const [jsonOpened, setJsonOpened] = useState(false)
  const [activeQuestion, setActiveQuestion] = useState<TrainingCourseTheoryQuestionModel | undefined>()
  const [jsonText, setJsonText] = useState('')
  const [jsonBankName, setJsonBankName] = useState(DEFAULT_THEORY_BANK_NAME)

  const fetchQuestions = async () => {
    try {
      setLoading(true)
      const res = await trainingCourseAdminApi.theoryQuestions(courseId, {
        keyword: keyword.trim() || undefined,
        type: typeFilter === 'All' ? undefined : (typeFilter as TheoryQuestionType),
        bankName: bankFilter === 'All' ? undefined : bankFilter,
        count: 5000,
      })
      setQuestions(res.data)
    } catch (err) {
      showErrorMsg(err, t)
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    void fetchQuestions()
  }, [courseId])

  const bankNames = useMemo(() => {
    const names = [...new Set(questions.map((question) => question.bankName || DEFAULT_THEORY_BANK_NAME))].sort()
    return ['All', ...names]
  }, [questions])

  const visibleQuestions = useMemo(
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

  const saveQuestion = async (question: TheoryQuestionEditModel) => {
    try {
      setLoading(true)
      if (activeQuestion) {
        await trainingCourseAdminApi.updateTheoryQuestion(courseId, activeQuestion.id, question)
        showNotification({ color: 'teal', message: '题目已更新' })
      } else {
        await trainingCourseAdminApi.createTheoryQuestion(courseId, question)
        showNotification({ color: 'teal', message: '题目已创建' })
      }
      setEditorOpened(false)
      setActiveQuestion(undefined)
      await fetchQuestions()
    } catch (err) {
      showErrorMsg(err, t)
    } finally {
      setLoading(false)
    }
  }

  const deleteQuestion = async (questionId: number) => {
    try {
      await trainingCourseAdminApi.deleteTheoryQuestion(courseId, questionId)
      showNotification({ color: 'teal', message: '题目已删除' })
      await fetchQuestions()
    } catch (err) {
      showErrorMsg(err, t)
    }
  }

  const importJson = async () => {
    try {
      setLoading(true)
      const parsed = parseImportedTheoryQuestions(jsonText, jsonBankName)
      for (const question of parsed) {
        await trainingCourseAdminApi.createTheoryQuestion(courseId, question)
      }
      showNotification({ color: 'teal', message: `已导入 ${parsed.length} 道题目` })
      setJsonOpened(false)
      setJsonText('')
      await fetchQuestions()
    } catch (err) {
      showErrorMsg(err, t)
    } finally {
      setLoading(false)
    }
  }

  const readJsonFile = async (file: File | null) => {
    if (!file) return
    setJsonText(await file.text())
    if (!jsonBankName || jsonBankName === DEFAULT_THEORY_BANK_NAME) {
      setJsonBankName(file.name.replace(/\.[^.]+$/, '') || DEFAULT_THEORY_BANK_NAME)
    }
    setJsonOpened(true)
  }

  return (
    <YinyuPanel p="lg">
      <Stack gap="md">
        <Group justify="space-between" align="flex-start">
          <Stack gap={4}>
            <Text className="yy-section-kicker">Theory Bank</Text>
            <Text fw={900} size="lg">
              课程题库
            </Text>
            <Text size="sm" c="dimmed">
              当前课程内共享，章节课后练习只能从这里选择题目。
            </Text>
          </Stack>
          <Group gap="xs">
            <Button
              variant="light"
              leftSection={<Icon path={mdiRefresh} size={0.82} />}
              loading={loading}
              onClick={fetchQuestions}
            >
              刷新
            </Button>
            <FileButton onChange={readJsonFile} accept="application/json,.json">
              {(props) => (
                <Button {...props} variant="light" leftSection={<Icon path={mdiFileUploadOutline} size={0.82} />}>
                  JSON 导入
                </Button>
              )}
            </FileButton>
            <Button
              leftSection={<Icon path={mdiPlus} size={0.82} />}
              onClick={() => {
                setActiveQuestion(undefined)
                setEditorOpened(true)
              }}
            >
              新建题目
            </Button>
          </Group>
        </Group>

        <SimpleGrid cols={{ base: 1, md: 3 }} spacing="sm">
          <TextInput
            leftSection={<Icon path={mdiMagnify} size={0.75} />}
            placeholder="搜索题干、说明、题库"
            value={keyword}
            onChange={(event) => setKeyword(event.currentTarget.value)}
            onKeyDown={(event) => {
              if (event.key === 'Enter') void fetchQuestions()
            }}
          />
          <Select data={theoryQuestionTypeFilterOptions} value={typeFilter} onChange={(value) => setTypeFilter(value ?? 'All')} />
          <Select
            data={bankNames.map((name) => ({ value: name, label: name === 'All' ? '全部题库' : name }))}
            value={bankFilter}
            onChange={(value) => setBankFilter(value ?? 'All')}
            searchable
          />
        </SimpleGrid>

        <ScrollArea.Autosize mah={520}>
          <Table striped highlightOnHover verticalSpacing="sm">
            <Table.Thead>
              <Table.Tr>
                <Table.Th>题库</Table.Th>
                <Table.Th>题型</Table.Th>
                <Table.Th>题干</Table.Th>
                <Table.Th>答案</Table.Th>
                <Table.Th w={96}>操作</Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {visibleQuestions.map((question) => (
                <Table.Tr key={question.id}>
                  <Table.Td>
                    <Badge variant="light">{question.bankName || DEFAULT_THEORY_BANK_NAME}</Badge>
                  </Table.Td>
                  <Table.Td>{theoryQuestionTypeLabel(question.type)}</Table.Td>
                  <Table.Td>
                    <Stack gap={2}>
                      <Text fw={800} lineClamp={1}>
                        {question.title}
                      </Text>
                      {question.content ? (
                        <Text size="xs" c="dimmed" lineClamp={1}>
                          {question.content}
                        </Text>
                      ) : null}
                    </Stack>
                  </Table.Td>
                  <Table.Td>
                    <Text size="sm" lineClamp={1}>
                      {getTheoryAnswerLabel(question)}
                    </Text>
                  </Table.Td>
                  <Table.Td>
                    <Group gap={6} wrap="nowrap">
                      <Tooltip label="编辑">
                        <ActionIcon
                          variant="light"
                          onClick={() => {
                            setActiveQuestion(question)
                            setEditorOpened(true)
                          }}
                        >
                          <Icon path={mdiPencilOutline} size={0.8} />
                        </ActionIcon>
                      </Tooltip>
                      <ActionIconWithConfirm
                        iconPath={mdiDeleteOutline}
                        color="red"
                        message="确认删除这道题目？已被章节测试引用的题目不能删除。"
                        onClick={() => deleteQuestion(question.id)}
                      />
                    </Group>
                  </Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
          {visibleQuestions.length === 0 ? <Empty description="当前筛选条件下暂无题目" /> : null}
        </ScrollArea.Autosize>
      </Stack>

      <TheoryQuestionEditorModal
        opened={editorOpened}
        question={activeQuestion}
        loading={loading}
        onClose={() => {
          setEditorOpened(false)
          setActiveQuestion(undefined)
        }}
        onSave={saveQuestion}
      />

      <Modal
        opened={jsonOpened}
        onClose={() => setJsonOpened(false)}
        title="JSON 批量导入"
        size="min(96vw, 1120px)"
        classNames={{
          content: 'yy-theory-json-import-modal-content',
          body: 'yy-theory-json-import-modal-body',
        }}
      >
        <YinyuModalBody p="md">
          <Stack gap="md">
            <TextInput label="默认题库名称" value={jsonBankName} onChange={(event) => setJsonBankName(event.currentTarget.value)} />
            <Textarea
              className="yy-theory-json-import-input"
              label="题库 JSON"
              autosize
              minRows={14}
              maxRows={22}
              value={jsonText}
              onChange={(event) => setJsonText(event.currentTarget.value)}
              placeholder='{"questions":[{"type":"SingleChoice","title":"题干","options":["A","B"],"answer":"A"}]}'
            />
            <Group justify="flex-end" className="yy-theory-json-import-actions">
              <Button variant="default" onClick={() => setJsonOpened(false)}>
                取消
              </Button>
              <Button loading={loading} onClick={importJson}>
                导入题目
              </Button>
            </Group>
          </Stack>
        </YinyuModalBody>
      </Modal>
    </YinyuPanel>
  )
}
