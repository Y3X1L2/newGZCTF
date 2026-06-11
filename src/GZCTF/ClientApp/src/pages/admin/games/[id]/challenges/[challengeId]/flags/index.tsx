import { Alert, Button, Group, Modal, NumberInput, Select, Stack, Text, Textarea, TextInput } from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { useEffect, useState } from 'react'
import { useNavigate, useParams } from 'react-router'
import { AdminPage } from '@Components/admin/AdminPage'
import { YinyuModalBody, YinyuPanel } from '@Components/yinyu/YinyuUI'
import { AnswerType, FlagScoreMode } from '@Api'

interface FlagInfo {
  id: number
  flag: string
  orderIndex: number
  description?: string
  scoreMode: FlagScoreMode
  fixedScore: number
  maxAttempts: number
  answerType: AnswerType
  customName?: string
  attachment?: { name: string }
}

const scoreModeOptions = [
  { value: FlagScoreMode.InheritDecay, label: '继承衰减' },
  { value: FlagScoreMode.FixedScore, label: '固定分值' },
]

const answerTypeOptions = [
  { value: AnswerType.Flag, label: 'Flag' },
  { value: AnswerType.File, label: '文件' },
  { value: AnswerType.Custom, label: '自定义' },
]

const emptyFlagDraft = () => ({
  flag: '',
  orderIndex: 0,
  description: '',
  scoreMode: FlagScoreMode.InheritDecay,
  fixedScore: 0,
  maxAttempts: 0,
  answerType: AnswerType.Flag,
  customName: '',
})

export default function FlagsEdit() {
  const { id: gameId, challengeId } = useParams<{ id: string; challengeId: string }>()
  const navigate = useNavigate()
  const [flags, setFlags] = useState<FlagInfo[]>([])
  const [loading, setLoading] = useState(true)
  const [draft, setDraft] = useState(emptyFlagDraft())
  const [editingFlag, setEditingFlag] = useState<FlagInfo | null>(null)
  const [editDraft, setEditDraft] = useState(emptyFlagDraft())

  const load = async () => {
    setLoading(true)
    try {
      const res = await fetch(`/api/edit/Games/${gameId}/Challenges/${challengeId}`)
      if (res.ok) {
        const data = await res.json()
        if (data.flags) setFlags(data.flags)
      }
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    load()
  }, [gameId, challengeId])

  const handleAddFlag = async () => {
    if (!draft.flag.trim()) return

    try {
      const res = await fetch(`/api/edit/Games/${gameId}/Challenges/${challengeId}/Flags`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify([
          {
            flag: draft.flag,
            orderIndex: draft.orderIndex,
            description: draft.description || undefined,
            scoreMode: draft.scoreMode,
            fixedScore: draft.fixedScore,
            maxAttempts: draft.maxAttempts,
            answerType: draft.answerType,
            customName: draft.customName || undefined,
          },
        ]),
      })

      if (res.ok) {
        notifications.show({ title: 'Flag 已添加', message: '新 Flag 已成功添加', color: 'green' })
        setDraft(emptyFlagDraft())
        load()
      } else {
        notifications.show({ title: '添加失败', message: '请检查 Flag 格式', color: 'red' })
      }
    } catch {
      // Keep the previous silent network behavior.
    }
  }

  const openEdit = (flag: FlagInfo) => {
    setEditingFlag(flag)
    setEditDraft({
      flag: flag.flag,
      orderIndex: flag.orderIndex ?? 0,
      description: flag.description ?? '',
      scoreMode: flag.scoreMode ?? FlagScoreMode.InheritDecay,
      fixedScore: flag.fixedScore ?? 0,
      maxAttempts: flag.maxAttempts ?? 0,
      answerType: flag.answerType ?? AnswerType.Flag,
      customName: flag.customName ?? '',
    })
  }

  const handleUpdateFlag = async () => {
    if (!editingFlag) return

    try {
      const res = await fetch(`/api/edit/Games/${gameId}/Challenges/${challengeId}/Flags/${editingFlag.id}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          flag: editDraft.flag,
          orderIndex: editDraft.orderIndex,
          description: editDraft.description,
          scoreMode: editDraft.scoreMode,
          fixedScore: editDraft.fixedScore,
          maxAttempts: editDraft.maxAttempts,
          answerType: editDraft.answerType,
          customName: editDraft.customName,
        }),
      })

      if (res.ok) {
        notifications.show({ title: '更新成功', message: 'Flag 已更新', color: 'green' })
        setEditingFlag(null)
        load()
      }
    } catch {
      // Keep the previous silent network behavior.
    }
  }

  return (
    <AdminPage
      head={
        <Group>
          <Button variant="default" onClick={() => navigate(`/admin/games/${gameId}/challenges/${challengeId}`)}>
            返回题目编辑
          </Button>
          <Button variant="default" onClick={() => navigate(`/admin/games/${gameId}/challenges`)}>
            返回题目列表
          </Button>
        </Group>
      }
      isLoading={loading}
    >
      <YinyuPanel p="md">
        <Text fw={700} mb="md">
          Flag 管理
        </Text>
        <Stack gap="xs" mb="md">
          <Group>
            <TextInput
              placeholder="输入新 Flag..."
              value={draft.flag}
              onChange={(event) => setDraft({ ...draft, flag: event.currentTarget.value })}
              style={{ flex: 1 }}
            />
            <NumberInput
              label="顺序"
              value={draft.orderIndex}
              onChange={(value) => setDraft({ ...draft, orderIndex: Number(value) || 0 })}
              w={90}
            />
            <Select
              label="评分模式"
              data={scoreModeOptions}
              value={draft.scoreMode}
              onChange={(value) => value && setDraft({ ...draft, scoreMode: value as FlagScoreMode })}
              w={150}
            />
          </Group>
          <Group>
            {draft.scoreMode === FlagScoreMode.FixedScore && (
              <NumberInput
                label="固定分值"
                value={draft.fixedScore}
                onChange={(value) => setDraft({ ...draft, fixedScore: Number(value) || 0 })}
                w={130}
              />
            )}
            <NumberInput
              label="最大尝试"
              value={draft.maxAttempts}
              onChange={(value) => setDraft({ ...draft, maxAttempts: Number(value) || 0 })}
              w={130}
              placeholder="0=无限"
            />
            <Select
              label="答案类型"
              data={answerTypeOptions}
              value={draft.answerType}
              onChange={(value) => value && setDraft({ ...draft, answerType: value as AnswerType })}
              w={150}
            />
          </Group>
          <TextInput
            label="自定义名称"
            placeholder="例如：步骤 1"
            value={draft.customName}
            onChange={(event) => setDraft({ ...draft, customName: event.currentTarget.value })}
          />
          <Textarea
            label="描述"
            placeholder="Flag 描述信息"
            value={draft.description}
            onChange={(event) => setDraft({ ...draft, description: event.currentTarget.value })}
            rows={2}
          />
          <Button onClick={handleAddFlag}>添加 Flag</Button>
        </Stack>

        <Stack>
          {flags.length === 0 && <Text className="yy-readable-text">暂无 Flag，请添加</Text>}
          {flags.map((flag) => (
            <Alert key={flag.id} color="green" py="xs">
              <Group justify="space-between">
                <Text size="sm" ff="monospace">
                  {flag.flag}
                </Text>
                <Group gap="xs">
                  {flag.customName && (
                    <Text size="xs" c="blue">
                      {flag.customName}
                    </Text>
                  )}
                  {flag.scoreMode === FlagScoreMode.FixedScore && (
                    <Text size="xs" c="orange">
                      {flag.fixedScore} 分
                    </Text>
                  )}
                  {flag.maxAttempts > 0 && (
                    <Text size="xs" className="yy-readable-text">
                      限 {flag.maxAttempts} 次
                    </Text>
                  )}
                  <Button size="xs" variant="subtle" onClick={() => openEdit(flag)}>
                    编辑
                  </Button>
                </Group>
              </Group>
              {flag.description && (
                <Text size="xs" className="yy-readable-text" mt={4}>
                  {flag.description}
                </Text>
              )}
            </Alert>
          ))}
        </Stack>

        <Modal opened={!!editingFlag} onClose={() => setEditingFlag(null)} title="编辑 Flag">
          <YinyuModalBody p="md">
            <Stack>
              <TextInput
                label="Flag"
                required
                value={editDraft.flag}
                onChange={(event) => setEditDraft({ ...editDraft, flag: event.currentTarget.value })}
              />
              <Group>
                <NumberInput
                  label="顺序"
                  value={editDraft.orderIndex}
                  onChange={(value) => setEditDraft({ ...editDraft, orderIndex: Number(value) || 0 })}
                />
                <Select
                  label="评分模式"
                  data={scoreModeOptions}
                  value={editDraft.scoreMode}
                  onChange={(value) => value && setEditDraft({ ...editDraft, scoreMode: value as FlagScoreMode })}
                />
                {editDraft.scoreMode === FlagScoreMode.FixedScore && (
                  <NumberInput
                    label="固定分值"
                    value={editDraft.fixedScore}
                    onChange={(value) => setEditDraft({ ...editDraft, fixedScore: Number(value) || 0 })}
                  />
                )}
              </Group>
              <Group>
                <NumberInput
                  label="最大尝试"
                  value={editDraft.maxAttempts}
                  onChange={(value) => setEditDraft({ ...editDraft, maxAttempts: Number(value) || 0 })}
                  placeholder="0=无限"
                />
                <Select
                  label="答案类型"
                  data={answerTypeOptions}
                  value={editDraft.answerType}
                  onChange={(value) => value && setEditDraft({ ...editDraft, answerType: value as AnswerType })}
                />
              </Group>
              <TextInput
                label="自定义名称"
                value={editDraft.customName}
                onChange={(event) => setEditDraft({ ...editDraft, customName: event.currentTarget.value })}
                placeholder="例如：步骤 1"
              />
              <Textarea
                label="描述"
                value={editDraft.description}
                onChange={(event) => setEditDraft({ ...editDraft, description: event.currentTarget.value })}
              />
              <Button fullWidth onClick={handleUpdateFlag}>
                保存修改
              </Button>
            </Stack>
          </YinyuModalBody>
        </Modal>
      </YinyuPanel>
    </AdminPage>
  )
}
