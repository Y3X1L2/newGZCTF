import { useEffect, useState } from 'react'
import { useParams, useNavigate } from 'react-router'
import {
  Button, Card, Group, NumberInput, Select, Stack, Switch,
  Text, TextInput, Textarea, Badge, Alert,
} from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { useTranslation } from 'react-i18next'
import { AdminPage } from '@Components/admin/AdminPage'

interface ChallengeEditData {
  id: number
  title: string
  content: string
  category: string
  type: string
  environment: string | null
  containerImage: string
  memoryLimit: number
  cpuCount: number
  storageLimit: number
  exposePort: number
  originalScore: number
  minScoreRate: number
  difficulty: number
  isEnabled: boolean
  enableTrafficCapture: boolean
  disableBloodBonus: boolean
  submissionLimit: number
  flagTemplate: string | null
  hints: string[]
  acceptedCount: number
  flags: { id: number; flag: string; orderIndex?: number; scoreMode?: string }[]
}

export default function ChallengeEdit() {
  const { id: gameId, challengeId } = useParams<{ id: string; challengeId: string }>()
  const navigate = useNavigate()
  const { t } = useTranslation()
  const [challenge, setChallenge] = useState<ChallengeEditData | null>(null)
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [newFlag, setNewFlag] = useState('')
  const [addingFlag, setAddingFlag] = useState(false)

  const load = async () => {
    setLoading(true)
    try {
      const res = await fetch(`/api/edit/Games/${gameId}/Challenges/${challengeId}`)
      const c = res.ok ? await res.json() : null
      if (c) setChallenge(c)
    } finally { setLoading(false) }
  }

  useEffect(() => { load() }, [gameId, challengeId])

  const handleSave = async () => {
    if (!challenge) return
    setSaving(true)
    try {
      const res = await fetch(`/api/edit/Games/${gameId}/Challenges/${challengeId}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          title: challenge.title,
          content: challenge.content,
          containerImage: challenge.containerImage,
          memoryLimit: challenge.memoryLimit,
          cpuCount: challenge.cpuCount,
          storageLimit: challenge.storageLimit,
          exposePort: challenge.exposePort,
          originalScore: challenge.originalScore,
          minScoreRate: challenge.minScoreRate,
          difficulty: challenge.difficulty,
          submissionLimit: challenge.submissionLimit,
          environment: challenge.environment ?? 'None',
          enableTrafficCapture: challenge.enableTrafficCapture,
          disableBloodBonus: challenge.disableBloodBonus,
          flagTemplate: challenge.flagTemplate,
        }),
      })
      if (res.ok) {
        notifications.show({ title: '保存成功', message: '题目配置已更新', color: 'green' })
        load()
      } else {
        const err = await res.json()
        notifications.show({ title: '保存失败', message: err.title ?? '请检查输入', color: 'red' })
      }
    } finally { setSaving(false) }
  }

  const handleAddFlag = async () => {
    if (!newFlag.trim()) return
    setAddingFlag(true)
    try {
      const res = await fetch(`/api/edit/Games/${gameId}/Challenges/${challengeId}/Flags`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify([{ flag: newFlag }]),
      })
      if (res.ok) {
        notifications.show({ title: 'Flag 已添加', message: '新 Flag 已成功添加', color: 'green' })
        setNewFlag('')
        load()
      }
    } finally { setAddingFlag(false) }
  }

  const handleToggle = async (enabled: boolean) => {
    if (!challenge) return
    try {
      const res = await fetch(`/api/edit/Games/${gameId}/Challenges/${challengeId}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ isEnabled: enabled }),
      })
      if (res.ok) {
        setChallenge({ ...challenge, isEnabled: enabled })
        notifications.show({ title: enabled ? '已启用' : '已禁用', message: `题目状态已更新为${enabled ? '启用' : '禁用'}`, color: 'green' })
      }
    } catch { /* ignore */ }
  }

  const updateFlag = async (index: number, field: string, value: string) => {
    if (!challenge) return
    const newFlags = [...challenge.flags]
    newFlags[index] = { ...newFlags[index], [field]: value }
    setChallenge({ ...challenge, flags: newFlags })
    // Persist to backend
    try {
      await fetch(`/api/edit/Games/${gameId}/Challenges/${challengeId}/Flags/${newFlags[index].id}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ [field]: value }),
      })
    } catch { /* ignore */ }
  }

  if (loading) return <AdminPage isLoading />
  if (!challenge) return <AdminPage><Alert color="red">题目不存在</Alert></AdminPage>

  return (
    <AdminPage head={
      <Group>
        <Button variant="default" onClick={() => navigate(`/admin/games/${gameId}/challenges`)}>
          ← 返回题目列表
        </Button>
        <Switch
          label={challenge.isEnabled ? '已启用' : '已禁用'}
          checked={challenge.isEnabled}
          onChange={(e) => handleToggle(e.currentTarget.checked)}
        />
        <Badge color="blue">{challenge.category}</Badge>
        <Badge color="grape">{challenge.type}</Badge>
        <Badge color="green">Accepted: {challenge.acceptedCount}</Badge>
      </Group>
    }>
      <Stack gap="md" w="100%">
        {/* Basic Info */}
        <Card shadow="sm" padding="md" withBorder>
          <Text fw={700} mb="sm">基本信息</Text>
          <TextInput label="题目标题" value={challenge.title}
            onChange={e => setChallenge({ ...challenge, title: e.currentTarget.value })} />
          <Textarea label="题目内容 (Markdown)" mt="sm" minRows={4} value={challenge.content}
            onChange={e => setChallenge({ ...challenge, content: e.currentTarget.value })} />
        </Card>

        {/* Container Config */}
        <Card shadow="sm" padding="md" withBorder>
          <Text fw={700} mb="sm">容器配置</Text>
          <TextInput label="容器镜像" value={challenge.containerImage}
            onChange={e => setChallenge({ ...challenge, containerImage: e.currentTarget.value })} />
          <Group mt="sm">
            <NumberInput label="内存 (MB)" value={challenge.memoryLimit} min={32} max={4096}
              onChange={v => setChallenge({ ...challenge, memoryLimit: Number(v) || 64 })} />
            <NumberInput label="CPU" value={challenge.cpuCount} min={1} max={8}
              onChange={v => setChallenge({ ...challenge, cpuCount: Number(v) || 1 })} />
            <NumberInput label="存储 (MB)" value={challenge.storageLimit} min={64} max={10240}
              onChange={v => setChallenge({ ...challenge, storageLimit: Number(v) || 256 })} />
            <NumberInput label="端口" value={challenge.exposePort} min={1} max={65535}
              onChange={v => setChallenge({ ...challenge, exposePort: Number(v) || 80 })} />
          </Group>
        </Card>

        {/* Environment Config */}
        <Card shadow="sm" padding="md" mt="md" withBorder>
          <Text fw={700} mb="sm">环境配置</Text>
          <Select
            label="环境类型"
            data={[
              { value: 'None', label: '无环境（附件题）' },
              { value: 'Docker', label: 'Linux Docker 容器' },
              { value: 'WindowsVM', label: 'Windows 虚拟机 (RDP)' },
            ]}
            value={challenge.environment ?? 'None'}
            onChange={(v) => setChallenge({ ...challenge, environment: v })}
          />
        </Card>

        {/* Scoring */}
        <Card shadow="sm" padding="md" withBorder>
          <Text fw={700} mb="sm">评分配置</Text>
          <Group>
            <NumberInput label="原始分数" value={challenge.originalScore} min={100} max={5000}
              onChange={v => setChallenge({ ...challenge, originalScore: Number(v) || 1000 })} />
            <NumberInput label="最低得分率" value={challenge.minScoreRate} min={0} max={1} step={0.05}
              onChange={v => setChallenge({ ...challenge, minScoreRate: Number(v) || 0.25 })} />
            <NumberInput label="难度系数" value={challenge.difficulty} min={1} max={20} step={0.5}
              onChange={v => setChallenge({ ...challenge, difficulty: Number(v) || 5 })} />
            <NumberInput label="提交限制 (0=无限制)" value={challenge.submissionLimit} min={0}
              onChange={v => setChallenge({ ...challenge, submissionLimit: Number(v) || 0 })} />
          </Group>
        </Card>

        {/* Dynamic Flag */}
        <Card shadow="sm" padding="md" withBorder>
          <Text fw={700} mb="sm">动态 Flag</Text>
          <TextInput label="Flag 模板 (动态容器使用)" value={challenge.flagTemplate ?? ''}
            onChange={e => setChallenge({ ...challenge, flagTemplate: e.currentTarget.value || null })} />
        </Card>

        {/* Flags */}
        <Card shadow="sm" padding="md" withBorder>
          <Text fw={700} mb="sm">Flag 管理</Text>
          <Group>
            <TextInput placeholder="输入新 Flag..." value={newFlag}
              onChange={e => setNewFlag(e.currentTarget.value)} style={{ flex: 1 }} />
            <Button loading={addingFlag} onClick={handleAddFlag}>添加 Flag</Button>
          </Group>
          {challenge.flags?.map((f, i) => (
            <Alert key={f.id ?? i} color="green" mt="xs" py="xs">
              <Group wrap="nowrap" align="flex-start">
                <Text size="sm" ff="monospace" style={{ flex: 1 }}>{f.flag}</Text>
                <Select
                  label="计分模式"
                  data={[
                    { value: 'InheritDecay', label: '跟随衰减' },
                    { value: 'FixedScore', label: '固定分值' },
                  ]}
                  value={f.scoreMode ?? 'InheritDecay'}
                  onChange={(v) => updateFlag(i, 'scoreMode', v!)}
                  size="xs"
                  maw={140}
                />
              </Group>
            </Alert>
          ))}
        </Card>

        {/* Save */}
        <Group justify="flex-end">
          <Button loading={saving} onClick={handleSave} size="lg">
            保存配置
          </Button>
        </Group>
      </Stack>
    </AdminPage>
  )
}
