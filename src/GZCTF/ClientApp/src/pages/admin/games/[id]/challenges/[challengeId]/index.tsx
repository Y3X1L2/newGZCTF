import { useEffect, useState } from 'react'
import { useParams, useNavigate } from 'react-router'
import {
  Button, Card, Group, NumberInput, Select, Stack, Switch,
  Text, TextInput, Textarea, Badge, Alert, Loader,
} from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { AdminPage } from '@Components/admin/AdminPage'

interface ImageTemplate {
  id: number
  name: string
  description: string
  localFilePath?: string
  imagePath?: string
  osType: number
}

interface ChallengeEditData {
  id: number
  title: string
  content: string
  category: string
  type: string
  environment: string
  imageTemplateId: number | null
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

// EnvironmentType enum string values matching backend JsonStringEnumConverter
const ENV_NONE = 'None'
const ENV_DOCKER = 'Docker'
const ENV_WINDOWS_VM = 'WindowsVM'

const envOptions = [
  { value: 'None', label: '无环境（附件题）' },
  { value: 'Docker', label: 'Linux Docker 容器' },
  { value: 'WindowsVM', label: 'Windows 虚拟机 (RDP)' },
]

export default function ChallengeEdit() {
  const { id: gameId, challengeId } = useParams<{ id: string; challengeId: string }>()
  const navigate = useNavigate()
  const [challenge, setChallenge] = useState<ChallengeEditData | null>(null)
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [newFlag, setNewFlag] = useState('')
  const [addingFlag, setAddingFlag] = useState(false)
  const [imageTemplates, setImageTemplates] = useState<ImageTemplate[]>([])

  const load = async () => {
    setLoading(true)
    try {
      const res = await fetch(`/api/edit/Games/${gameId}/Challenges/${challengeId}`)
      if (!res.ok) {
        console.error('Challenge load failed:', res.status)
        return
      }
      const c = await res.json()
      if (c) {
        // Ensure defaults for fields that may be missing
        setChallenge({
          ...c,
          environment: c.environment ?? 'None',
          imageTemplateId: c.imageTemplateId ?? null,
          flags: c.flags ?? [],
          hints: c.hints ?? [],
          containerImage: c.containerImage ?? '',
          memoryLimit: c.memoryLimit ?? 64,
          cpuCount: c.cpuCount ?? 1,
          storageLimit: c.storageLimit ?? 256,
          exposePort: c.exposePort ?? 80,
          originalScore: c.originalScore ?? 500,
          minScoreRate: c.minScoreRate ?? 0.25,
          difficulty: c.difficulty ?? 3,
          submissionLimit: c.submissionLimit ?? 0,
          acceptedCount: c.acceptedCount ?? 0,
          enableTrafficCapture: c.enableTrafficCapture ?? false,
          disableBloodBonus: c.disableBloodBonus ?? false,
        })
      }
    } catch (err) {
      console.error('Challenge load error:', err)
    } finally { setLoading(false) }
  }

  const loadTemplates = async () => {
    try {
      const res = await fetch('/api/v1/image-templates')
      if (res.ok) {
        const data = await res.json()
        // API returns paginated: { total, items: [...] }
        setImageTemplates(data.items ?? data ?? [])
      }
    } catch { /* ignore */ }
  }

  useEffect(() => { load(); loadTemplates() }, [gameId, challengeId])

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
          environment: challenge.environment,
          imageTemplateId: challenge.imageTemplateId,
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

  const envType = challenge.environment ?? ENV_NONE
  const isContainer = envType === ENV_DOCKER
  const isWindowsVM = envType === ENV_WINDOWS_VM

  const envLabel = envOptions.find(o => o.value === envType)?.label ?? '未知'

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
        <Badge color={isWindowsVM ? 'orange' : isContainer ? 'cyan' : 'gray'}>
          {envLabel}
        </Badge>
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

        {/* Environment Config */}
        <Card shadow="sm" padding="md" withBorder>
          <Text fw={700} mb="sm">环境配置</Text>
          <Select
            label="环境类型"
            data={envOptions}
            value={envType}
            onChange={(v) => {
              const newEnv = v || ENV_NONE
              setChallenge({
                ...challenge,
                environment: newEnv,
                // Clear irrelevant fields when switching
                imageTemplateId: newEnv === ENV_WINDOWS_VM ? challenge.imageTemplateId : null,
              })
            }}
          />

          {/* Container-specific config */}
          {isContainer && (
            <Stack gap="sm" mt="md">
              <Text size="sm" fw={600} c="cyan">容器配置</Text>
              <TextInput label="容器镜像" value={challenge.containerImage}
                onChange={e => setChallenge({ ...challenge, containerImage: e.currentTarget.value })} />
              <Group>
                <NumberInput label="内存 (MB)" value={challenge.memoryLimit} min={32} max={4096}
                  onChange={v => setChallenge({ ...challenge, memoryLimit: Number(v) || 64 })} />
                <NumberInput label="CPU" value={challenge.cpuCount} min={1} max={8}
                  onChange={v => setChallenge({ ...challenge, cpuCount: Number(v) || 1 })} />
                <NumberInput label="存储 (MB)" value={challenge.storageLimit} min={64} max={10240}
                  onChange={v => setChallenge({ ...challenge, storageLimit: Number(v) || 256 })} />
                <NumberInput label="端口" value={challenge.exposePort} min={1} max={65535}
                  onChange={v => setChallenge({ ...challenge, exposePort: Number(v) || 80 })} />
              </Group>
            </Stack>
          )}

          {/* Windows VM-specific config */}
          {isWindowsVM && (
            <Stack gap="sm" mt="md">
              <Text size="sm" fw={600} c="orange">Windows 虚拟机配置</Text>
              <Select
                label="镜像模板"
                placeholder="选择 Windows 镜像模板..."
                data={imageTemplates.map(t => ({
                  value: String(t.id),
                  label: `${t.name}${t.description ? ' — ' + t.description : ''}`,
                }))}
                value={challenge.imageTemplateId ? String(challenge.imageTemplateId) : null}
                onChange={(v) => setChallenge({ ...challenge, imageTemplateId: v ? Number(v) : null })}
              />
              {challenge.imageTemplateId && (
                <Alert color="blue" variant="light">
                  已选择镜像模板 ID: {challenge.imageTemplateId}
                  {imageTemplates.find(t => t.id === challenge.imageTemplateId) && (
                    <Text size="xs" mt={4}>
                      路径: {imageTemplates.find(t => t.id === challenge.imageTemplateId)?.localFilePath ?? '未知'}
                    </Text>
                  )}
                </Alert>
              )}
              <Group>
                <NumberInput label="内存 (MB)" value={challenge.memoryLimit} min={512} max={16384}
                  onChange={v => setChallenge({ ...challenge, memoryLimit: Number(v) || 4096 })} />
                <NumberInput label="CPU 核数" value={challenge.cpuCount} min={1} max={8}
                  onChange={v => setChallenge({ ...challenge, cpuCount: Number(v) || 2 })} />
              </Group>
              <Alert color="orange" variant="light" mt="xs">
                Windows 虚拟机将通过 Guacamole RDP 代理提供远程桌面访问。每个队伍启动后获得独立 VM 实例。
              </Alert>
            </Stack>
          )}

          {/* Attachment mode info */}
          {!isContainer && !isWindowsVM && (
            <Alert color="gray" variant="light" mt="md">
              附件题模式：无需环境配置，仅通过附件和 Flag 评判。
            </Alert>
          )}
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
