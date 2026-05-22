import { useEffect, useState } from 'react'
import { useParams, useNavigate } from 'react-router'
import {
  Button, Card, Group, Stack, Text, TextInput, Alert, ActionIcon,
  NumberInput, Select, Textarea, Modal,
} from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { AdminPage } from '@Components/admin/AdminPage'
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

export default function FlagsEdit() {
  const { id: gameId, challengeId } = useParams<{ id: string; challengeId: string }>()
  const navigate = useNavigate()
  const [flags, setFlags] = useState<FlagInfo[]>([])
  const [loading, setLoading] = useState(true)
  const [newFlag, setNewFlag] = useState('')
  const [newOrderIndex, setNewOrderIndex] = useState(0)
  const [newDescription, setNewDescription] = useState('')
  const [newScoreMode, setNewScoreMode] = useState<FlagScoreMode>(FlagScoreMode.InheritDecay)
  const [newFixedScore, setNewFixedScore] = useState(0)
  const [newMaxAttempts, setNewMaxAttempts] = useState(0)
  const [newAnswerType, setNewAnswerType] = useState<AnswerType>(AnswerType.Flag)
  const [newCustomName, setNewCustomName] = useState('')

  const [editingFlag, setEditingFlag] = useState<FlagInfo | null>(null)
  const [editFlag, setEditFlag] = useState('')
  const [editScoreMode, setEditScoreMode] = useState<string>('InheritDecay')
  const [editFixedScore, setEditFixedScore] = useState(0)
  const [editMaxAttempts, setEditMaxAttempts] = useState(0)
  const [editOrderIndex, setEditOrderIndex] = useState(0)
  const [editDescription, setEditDescription] = useState('')
  const [editAnswerType, setEditAnswerType] = useState<string>('Flag')
  const [editCustomName, setEditCustomName] = useState('')

  const load = async () => {
    setLoading(true)
    try {
      const res = await fetch(`/api/edit/Games/${gameId}/Challenges/${challengeId}`)
      if (res.ok) {
        const data = await res.json()
        if (data.flags) setFlags(data.flags)
      }
    } finally { setLoading(false) }
  }

  useEffect(() => { load() }, [])

  const handleAddFlag = async () => {
    if (!newFlag.trim()) return
    try {
      const res = await fetch(`/api/edit/Games/${gameId}/Challenges/${challengeId}/Flags`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify([{
          flag: newFlag,
          orderIndex: newOrderIndex,
          description: newDescription || undefined,
          scoreMode: newScoreMode,
          fixedScore: newFixedScore,
          maxAttempts: newMaxAttempts,
          answerType: newAnswerType,
          customName: newCustomName || undefined,
        }]),
      })
      if (res.ok) {
        notifications.show({ title: 'Flag 已添加', message: '新 Flag 已成功添加', color: 'green' })
        setNewFlag('')
        setNewOrderIndex(0)
        setNewDescription('')
        setNewScoreMode(FlagScoreMode.InheritDecay)
        setNewFixedScore(0)
        setNewMaxAttempts(0)
        setNewAnswerType(AnswerType.Flag)
        setNewCustomName('')
        load()
      } else {
        notifications.show({ title: '添加失败', message: '请检查 Flag 格式', color: 'red' })
      }
    } catch { /* ignore */ }
  }

  const handleUpdateFlag = async () => {
    if (!editingFlag) return
    try {
      const res = await fetch(
        `/api/edit/Games/${gameId}/Challenges/${challengeId}/Flags/${editingFlag.id}`,
        {
          method: 'PUT',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            flag: editFlag,
            orderIndex: editOrderIndex,
            description: editDescription,
            scoreMode: Number(editScoreMode),
            fixedScore: editFixedScore,
            maxAttempts: editMaxAttempts,
            answerType: Number(editAnswerType),
            customName: editCustomName,
          }),
        }
      )
      if (res.ok) {
        notifications.show({ title: '更新成功', message: 'Flag 已更新', color: 'green' })
        setEditingFlag(null)
        load()
      }
    } catch { /* ignore */ }
  }

  return (
    <AdminPage head={
      <Group>
        <Button variant="default" onClick={() => navigate(`/admin/games/${gameId}/challenges/${challengeId}`)}>
          ← 返回题目编辑
        </Button>
        <Button variant="default" onClick={() => navigate(`/admin/games/${gameId}/challenges`)}>
          返回题目列表
        </Button>
      </Group>
    } isLoading={loading}>
      <Card shadow="sm" padding="md" withBorder>
        <Text fw={700} mb="md">Flag 管理</Text>
        <Stack gap="xs" mb="md">
          <Group>
            <TextInput placeholder="输入新 Flag..." value={newFlag}
              onChange={e => setNewFlag(e.currentTarget.value)} style={{ flex: 1 }} />
            <NumberInput label="顺序" value={newOrderIndex} onChange={v => setNewOrderIndex(Number(v) ?? 0)} w={80} />
            <Select label="评分模式" data={scoreModeOptions} value={newScoreMode}
              onChange={v => setNewScoreMode(v as FlagScoreMode)} w={140} />
          </Group>
          <Group>
            {newScoreMode === FlagScoreMode.FixedScore && (
              <NumberInput label="固定分值" value={newFixedScore} onChange={v => setNewFixedScore(Number(v) ?? 0)} w={120} />
            )}
            <NumberInput label="最大尝试" value={newMaxAttempts} onChange={v => setNewMaxAttempts(Number(v) ?? 0)} w={120} placeholder="0=无限" />
            <Select label="答案类型" data={answerTypeOptions} value={newAnswerType}
              onChange={v => setNewAnswerType(v as AnswerType)} w={140} />
          </Group>
          <Group>
            <TextInput label="自定义名称" placeholder="如: 步骤1" value={newCustomName}
              onChange={e => setNewCustomName(e.currentTarget.value)} style={{ flex: 1 }} />
          </Group>
          <Textarea label="描述" placeholder="Flag 描述信息" value={newDescription}
            onChange={e => setNewDescription(e.currentTarget.value)} rows={2} />
          <Button onClick={handleAddFlag}>添加 Flag</Button>
        </Stack>
        <Stack>
          {flags.length === 0 && <Text c="dimmed">暂无 Flag，请添加</Text>}
          {flags.map((f) => (
            <Alert key={f.id} color="green" py="xs">
              <Group justify="space-between">
                <Text size="sm" ff="monospace">{f.flag}</Text>
                <Group gap="xs">
                  {f.customName && <Text size="xs" c="blue">{f.customName}</Text>}
                  {f.scoreMode === FlagScoreMode.FixedScore && <Text size="xs" c="orange">{f.fixedScore}分</Text>}
                  {f.maxAttempts > 0 && <Text size="xs" c="dimmed">限{f.maxAttempts}次</Text>}
                  <Button size="xs" variant="subtle" onClick={() => {
                    setEditingFlag(f)
                    setEditFlag(f.flag)
                    setEditScoreMode(String(f.scoreMode ?? 0))
                    setEditFixedScore(f.fixedScore ?? 0)
                    setEditMaxAttempts(f.maxAttempts ?? 0)
                    setEditOrderIndex(f.orderIndex ?? 0)
                    setEditDescription(f.description ?? '')
                    setEditAnswerType(String(f.answerType ?? 0))
                    setEditCustomName(f.customName ?? '')
                  }}>编辑</Button>
                </Group>
              </Group>
              {f.description && <Text size="xs" c="dimmed" mt={4}>{f.description}</Text>}
            </Alert>
          ))}
        </Stack>
        <Modal opened={!!editingFlag} onClose={() => setEditingFlag(null)} title="编辑 Flag">
          <Stack>
            <TextInput label="Flag" required value={editFlag} onChange={e => setEditFlag(e.currentTarget.value)} />
            <Group>
              <NumberInput label="顺序" value={editOrderIndex} onChange={v => setEditOrderIndex(Number(v) ?? 0)} />
              <Select label="评分模式" data={scoreModeOptions} value={editScoreMode} onChange={v => v && setEditScoreMode(v)} />
              {editScoreMode === '1' && (
                <NumberInput label="固定分值" value={editFixedScore} onChange={v => setEditFixedScore(Number(v) ?? 0)} />
              )}
            </Group>
            <Group>
              <NumberInput label="最大尝试" value={editMaxAttempts} onChange={v => setEditMaxAttempts(Number(v) ?? 0)} placeholder="0=无限" />
              <Select label="答案类型" data={answerTypeOptions} value={editAnswerType} onChange={v => v && setEditAnswerType(v)} />
            </Group>
            <TextInput label="自定义名称" value={editCustomName} onChange={e => setEditCustomName(e.currentTarget.value)} placeholder="如: 步骤1" />
            <Textarea label="描述" value={editDescription} onChange={e => setEditDescription(e.currentTarget.value)} />
            <Button fullWidth onClick={handleUpdateFlag}>保存修改</Button>
          </Stack>
        </Modal>
      </Card>
    </AdminPage>
  )
}
