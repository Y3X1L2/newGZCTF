import {
  ActionIcon,
  Badge,
  Button,
  Group,
  Loader,
  NumberInput,
  Select,
  Stack,
  Stepper,
  Text,
  Textarea,
  TextInput,
  Title,
} from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { mdiClose, mdiPlus } from '@mdi/js'
import { Icon } from '@mdi/react'
import { useEffect, useState } from 'react'
import { useNavigate, useParams } from 'react-router'
import { AdminPage } from '@Components/admin/AdminPage'
import { YinyuPanel } from '@Components/yinyu/YinyuUI'

interface CheckpointData {
  description: string
  verificationType: string
  score: number
  isRequired: boolean
}

interface GameOption {
  value: string
  label: string
}

interface TemplateOption {
  value: string
  label: string
  osType: string
}

const VERIFICATION_TYPES = [
  { value: 'AutoCommand', label: '自动命令检测' },
  { value: 'AutoScript', label: '自动脚本检测' },
  { value: 'ManualAnswer', label: '手动答案提交' },
  { value: 'ManualReview', label: '人工评审' },
]

export default function IRChallengeCreate() {
  const navigate = useNavigate()
  const { id: editId } = useParams<{ id?: string }>()

  const [activeStep, setActiveStep] = useState(0)
  const [title, setTitle] = useState('')
  const [description, setDescription] = useState('')
  const [gameId, setGameId] = useState<string | null>(null)
  const [osType, setOsType] = useState<string>('Linux')
  const [imageTemplateId, setImageTemplateId] = useState<string | null>(null)
  const [checkpoints, setCheckpoints] = useState<CheckpointData[]>([])
  const [submitting, setSubmitting] = useState(false)
  const [games, setGames] = useState<GameOption[]>([])
  const [templates, setTemplates] = useState<TemplateOption[]>([])
  const [loadingGames, setLoadingGames] = useState(true)
  const [loadingTemplates, setLoadingTemplates] = useState(true)

  useEffect(() => {
    fetch('/api/edit/Games?count=100')
      .then((r) => r.json())
      .then((data) => {
        const list = Array.isArray(data) ? data : (data.data ?? [])
        setGames(list.map((g: { id: number; title: string }) => ({ value: String(g.id), label: g.title })))
      })
      .catch(() => {})
      .finally(() => setLoadingGames(false))
  }, [])

  useEffect(() => {
    fetch('/api/v1/image-templates')
      .then((r) => r.json())
      .then((data) => {
        const list = Array.isArray(data) ? data : (data.data ?? data.items ?? [])
        setTemplates(
          list.map((t: { id: number; name: string; osType: string }) => ({
            value: String(t.id),
            label: `${t.name} (${t.osType})`,
            osType: t.osType,
          }))
        )
      })
      .catch(() => {})
      .finally(() => setLoadingTemplates(false))
  }, [])

  useEffect(() => {
    if (!editId) return

    fetch(`/api/v1/ir-challenges/${editId}`)
      .then((r) => r.json())
      .then((data) => {
        setTitle(data.title ?? '')
        setDescription(data.description ?? '')
        setGameId(String(data.gameId ?? ''))
        setOsType(data.osType ?? 'Linux')
        setImageTemplateId(data.imageTemplateId ? String(data.imageTemplateId) : null)
        if (data.checkpoints?.length) setCheckpoints(data.checkpoints)
      })
      .catch(() => {})
  }, [editId])

  const filteredTemplates = templates.filter((template) => !osType || template.osType === osType)
  const addCheckpoint = () =>
    setCheckpoints([...checkpoints, { description: '', verificationType: 'AutoCommand', score: 100, isRequired: true }])
  const removeCheckpoint = (i: number) => setCheckpoints(checkpoints.filter((_, idx) => idx !== i))

  const handleSubmit = async () => {
    if (!title || !gameId || checkpoints.length === 0) {
      notifications.show({ title: '请填写必填字段', message: '标题、赛事和至少一个检查点为必填', color: 'red' })
      return
    }

    setSubmitting(true)
    try {
      const url = editId ? `/api/v1/ir-challenges/${editId}` : '/api/v1/ir-challenges'
      const res = await fetch(url, {
        method: editId ? 'PUT' : 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          title,
          description,
          gameId: Number(gameId),
          osType,
          imageTemplateId: imageTemplateId ? Number(imageTemplateId) : null,
          checkpoints,
        }),
      })
      if (!res.ok) {
        const err = await res.json().catch(() => ({}))
        throw new Error((err as { title?: string }).title ?? 'Failed')
      }
      notifications.show({ title: editId ? '更新成功' : '创建成功', message: '题目已保存', color: 'green' })
      navigate('/admin/ir-challenges')
    } catch (e) {
      notifications.show({ title: '操作失败', message: (e as Error).message, color: 'red' })
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <AdminPage>
      <Stack gap="lg" w="100%" maw={920} mx="auto">
        <Stack gap={2}>
          <Title order={2}>{editId ? '编辑 IR 题目' : '创建应急响应题目'}</Title>
          <Text size="sm" c="dimmed">
            配置 Linux/Windows 靶机、环境模板与检查点验证规则。
          </Text>
        </Stack>

        <YinyuPanel p="lg">
          <Stepper active={activeStep} onStepClick={setActiveStep} allowNextStepsSelect={false}>
            <Stepper.Step label="基本信息">
              <TextInput
                data-testid="ir-title"
                label="题目名称"
                required
                value={title}
                onChange={(e) => setTitle(e.currentTarget.value)}
              />
              <Textarea
                data-testid="ir-description"
                label="应急场景描述"
                required
                mt="md"
                minRows={4}
                value={description}
                onChange={(e) => setDescription(e.currentTarget.value)}
              />
              {loadingGames ? (
                <Loader size="sm" mt="md" />
              ) : (
                <Select
                  label="所属赛事"
                  required
                  mt="md"
                  placeholder="选择赛事"
                  data={games}
                  value={gameId}
                  onChange={setGameId}
                  searchable
                />
              )}
              <Select
                data-testid="ir-os-type"
                label="靶机系统"
                required
                mt="md"
                data={[
                  { value: 'Linux', label: 'Linux (SSH 访问)' },
                  { value: 'Windows', label: 'Windows (Web 桌面代理)' },
                ]}
                value={osType}
                onChange={(value) => {
                  setOsType(value ?? 'Linux')
                  setImageTemplateId(null)
                }}
              />
              {loadingTemplates ? (
                <Loader size="sm" mt="md" />
              ) : (
                <Select
                  label="环境镜像 / 模板"
                  mt="md"
                  placeholder={filteredTemplates.length === 0 ? '暂无可用模板 (请先上传镜像)' : '选择环境模板'}
                  data={filteredTemplates}
                  value={imageTemplateId}
                  onChange={setImageTemplateId}
                  searchable
                  clearable
                  description="选择已注册的环境模板。如无可选项，请先在环境模板管理中上传镜像。"
                />
              )}
            </Stepper.Step>

            <Stepper.Step label="检查点配置">
              {checkpoints.map((checkpoint, i) => (
                <YinyuPanel key={i} p="md" mt="md">
                  <Group justify="space-between" mb="xs">
                    <Badge size="lg">检查点 {i + 1}</Badge>
                    <ActionIcon color="red" onClick={() => removeCheckpoint(i)}>
                      <Icon path={mdiClose} size={0.85} />
                    </ActionIcon>
                  </Group>
                  <Textarea
                    data-testid={`checkpoint-desc-${i}`}
                    label="检查点描述"
                    required
                    value={checkpoint.description}
                    onChange={(e) => {
                      const next = [...checkpoints]
                      next[i].description = e.currentTarget.value
                      setCheckpoints(next)
                    }}
                  />
                  <Select
                    data-testid={`checkpoint-verify-type-${i}`}
                    label="验证方式"
                    mt="sm"
                    data={VERIFICATION_TYPES}
                    value={checkpoint.verificationType}
                    onChange={(value) => {
                      const next = [...checkpoints]
                      next[i].verificationType = value ?? 'AutoCommand'
                      setCheckpoints(next)
                    }}
                  />
                  <NumberInput
                    data-testid={`checkpoint-score-${i}`}
                    label="分值"
                    mt="sm"
                    value={checkpoint.score}
                    min={1}
                    onChange={(value) => {
                      const next = [...checkpoints]
                      next[i].score = Number(value) || 1
                      setCheckpoints(next)
                    }}
                  />
                </YinyuPanel>
              ))}
              <Button
                data-testid="add-checkpoint"
                mt="md"
                variant="outline"
                leftSection={<Icon path={mdiPlus} size={0.8} />}
                onClick={addCheckpoint}
              >
                添加检查点
              </Button>
            </Stepper.Step>
          </Stepper>
        </YinyuPanel>

        <Group justify="flex-end">
          <Button
            variant="default"
            onClick={() => setActiveStep(Math.max(0, activeStep - 1))}
            disabled={activeStep === 0}
          >
            上一步
          </Button>
          {activeStep < 1 ? (
            <Button onClick={() => setActiveStep(activeStep + 1)}>下一步</Button>
          ) : (
            <Button data-testid="submit-ir-challenge" loading={submitting} onClick={handleSubmit}>
              {editId ? '更新题目' : '创建题目'}
            </Button>
          )}
        </Group>
      </Stack>
    </AdminPage>
  )
}
