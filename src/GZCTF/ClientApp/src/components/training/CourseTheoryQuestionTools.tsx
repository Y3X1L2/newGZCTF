import { ActionIcon, Button, Checkbox, Group, Modal, Radio, Select, SimpleGrid, Stack, Text, Textarea, TextInput } from '@mantine/core'
import { mdiCheck, mdiDeleteOutline, mdiPlus } from '@mdi/js'
import { Icon } from '@mdi/react'
import { FC, useEffect, useState } from 'react'
import { YinyuModalBody } from '@Components/yinyu/YinyuUI'
import { TheoryQuestionEditModel, TheoryQuestionType } from '../../Api/TheoryApi'

export const DEFAULT_THEORY_BANK_NAME = 'Default'

export const theoryQuestionTypeOptions = [
  { value: TheoryQuestionType.SingleChoice, label: '单选题' },
  { value: TheoryQuestionType.MultipleChoice, label: '多选题' },
  { value: TheoryQuestionType.TrueFalse, label: '判断题' },
]

export const theoryQuestionTypeFilterOptions = [{ value: 'All', label: '全部题型' }, ...theoryQuestionTypeOptions]

export const theoryQuestionTypeLabel = (type: TheoryQuestionType) =>
  type === TheoryQuestionType.MultipleChoice ? '多选题' : type === TheoryQuestionType.TrueFalse ? '判断题' : '单选题'

export const theoryQuestionTypeShort = (type: TheoryQuestionType) =>
  type === TheoryQuestionType.MultipleChoice ? '多' : type === TheoryQuestionType.TrueFalse ? '判' : '单'

export const emptyTheoryQuestion = (): TheoryQuestionEditModel => ({
  type: TheoryQuestionType.SingleChoice,
  bankName: DEFAULT_THEORY_BANK_NAME,
  title: '',
  content: '',
  options: ['选项 A', '选项 B'],
  answerIndexes: [0],
})

const normalizeBankName = (bankName?: string | null) => {
  const value = bankName?.trim()
  return value ? value.slice(0, 128) : DEFAULT_THEORY_BANK_NAME
}

const normalizeAnswerIndexes = (indexes: number[], optionCount: number, multiple: boolean) => {
  const values = [
    ...new Set(indexes.map(Number).filter((index) => Number.isInteger(index) && index >= 0 && index < optionCount)),
  ]
  if (values.length === 0) return [0]
  return multiple ? values.sort((a, b) => a - b) : [values[0]]
}

export const normalizeTheoryQuestionDraft = (question: TheoryQuestionEditModel): TheoryQuestionEditModel => {
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

export const getTheoryAnswerLabel = (question: TheoryQuestionEditModel) =>
  question.answerIndexes
    .map((index) => question.options[index])
    .filter(Boolean)
    .join('、') || '-'

const normalizeType = (value: unknown): TheoryQuestionType => {
  const raw = String(value ?? '').toLowerCase()
  if (raw.includes('multiple') || raw.includes('multi') || raw.includes('多')) return TheoryQuestionType.MultipleChoice
  if (raw.includes('true') || raw.includes('false') || raw.includes('judge') || raw.includes('判'))
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

export const parseImportedTheoryQuestions = (text: string, bankName: string): TheoryQuestionEditModel[] => {
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

    return normalizeTheoryQuestionDraft({
      type,
      bankName: normalizeBankName(item.bankName ?? bankName),
      title: String(item.title ?? item.question ?? item.stem ?? `导入题目 ${index + 1}`),
      content: String(item.content ?? item.description ?? item.analysis ?? ''),
      options: safeOptions,
      answerIndexes: parseAnswerIndexes(answerRaw, safeOptions),
    })
  })
}

export const TheoryQuestionEditorModal: FC<{
  opened: boolean
  question?: TheoryQuestionEditModel
  loading: boolean
  onClose: () => void
  onSave: (question: TheoryQuestionEditModel) => Promise<void>
}> = ({ opened, question, loading, onClose, onSave }) => {
  const [draft, setDraft] = useState<TheoryQuestionEditModel>(emptyTheoryQuestion())

  useEffect(() => {
    if (opened) setDraft(normalizeTheoryQuestionDraft(question ?? emptyTheoryQuestion()))
  }, [opened, question])

  const setType = (type: TheoryQuestionType) => {
    setDraft(
      normalizeTheoryQuestionDraft({
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
      normalizeTheoryQuestionDraft({
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
              value={draft.bankName ?? DEFAULT_THEORY_BANK_NAME}
              onChange={(event) => setDraft({ ...draft, bankName: event.currentTarget.value })}
            />
            <Select
              label="题型"
              data={theoryQuestionTypeOptions}
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
              onClick={() => onSave(normalizeTheoryQuestionDraft(draft))}
            >
              保存题目
            </Button>
          </Group>
        </Stack>
      </YinyuModalBody>
    </Modal>
  )
}
