import {
  ActionIcon,
  Badge,
  Button,
  Group,
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
import { YinyuPanel, YinyuRouteLoader } from '@Components/yinyu/YinyuUI'

interface StageData {
  title: string
  skillDescription: string
  flag: string
  environmentImageIds: number[]
}

interface ScoringRuleData {
  submissionType: string
  weight: number
  verificationMode: string
  maxAttempts: number
}

interface GameOption {
  value: string
  label: string
}

interface TemplateOption {
  value: string
  label: string
  osType: string
  imageType: string
}

const SUBMISSION_TYPES = [
  { value: 'Flag', label: 'Flag' },
  { value: 'Writeup', label: '解题报告' },
  { value: 'IP', label: '攻击者 IP' },
  { value: 'Credential', label: '关键凭证' },
]

export default function ScenarioCreate() {
  const navigate = useNavigate()
  const { id: editId } = useParams<{ id?: string }>()

  const [activeStep, setActiveStep] = useState(0)
  const [title, setTitle] = useState('')
  const [description, setDescription] = useState('')
  const [gameId, setGameId] = useState<string | null>(null)
  const [stages, setStages] = useState<StageData[]>([])
  const [scoringRules, setScoringRules] = useState<ScoringRuleData[]>([
    { submissionType: 'Flag', weight: 50, verificationMode: 'AutoExact', maxAttempts: 10 },
    { submissionType: 'Writeup', weight: 30, verificationMode: 'ManualReview', maxAttempts: 3 },
    { submissionType: 'IP', weight: 20, verificationMode: 'AutoExact', maxAttempts: 5 },
  ])
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
          list.map((t: { id: number; name: string; osType: string; imageType: string }) => ({
            value: String(t.id),
            label: `${t.name} (${t.osType}/${t.imageType})`,
            osType: t.osType,
            imageType: t.imageType,
          }))
        )
      })
      .catch(() => {})
      .finally(() => setLoadingTemplates(false))
  }, [])

  useEffect(() => {
    if (!editId) return

    fetch(`/api/v1/scenarios/${editId}`)
      .then((r) => r.json())
      .then((data) => {
        setTitle(data.title ?? '')
        setDescription(data.description ?? '')
        setGameId(String(data.gameId ?? ''))

        if (data.stages?.length) {
          setStages(
            data.stages.map(
              (s: { title: string; skillDescription?: string; flag?: string; environmentImageIds?: number[] }) => ({
                title: s.title,
                skillDescription: s.skillDescription ?? '',
                flag: '',
                environmentImageIds: s.environmentImageIds ?? [],
              })
            )
          )
        }

        if (data.scoringRules?.length) {
          setScoringRules(
            data.scoringRules.map(
              (r: { submissionType?: string; weight?: number; verificationMode?: string; maxAttempts?: number }) => ({
                submissionType: r.submissionType ?? 'Flag',
                weight: r.weight ?? 0,
                verificationMode: r.verificationMode ?? 'AutoExact',
                maxAttempts: r.maxAttempts ?? 0,
              })
            )
          )
        }
      })
      .catch(() => {})
  }, [editId])

  const addStage = () => setStages([...stages, { title: '', skillDescription: '', flag: '', environmentImageIds: [] }])
  const removeStage = (i: number) => setStages(stages.filter((_, idx) => idx !== i))

  const handleSubmit = async () => {
    if (!title || !gameId || stages.length < 1) {
      notifications.show({ title: '请填写必填字段', message: '标题、赛事和至少一个阶段为必填', color: 'red' })
      return
    }

    const totalWeight = scoringRules.reduce((sum, r) => sum + r.weight, 0)
    if (Math.abs(totalWeight - 100) > 0.01) {
      notifications.show({ title: '评分权重错误', message: '权重之和必须为 100%', color: 'red' })
      return
    }

    setSubmitting(true)
    try {
      const url = editId ? `/api/v1/scenarios/${editId}` : '/api/v1/scenarios'
      const method = editId ? 'PUT' : 'POST'
      const body = JSON.stringify({
        title,
        description,
        gameId: Number(gameId),
        stages: stages.map((s, i) => ({ ...s, orderIndex: i + 1 })),
        scoringRules,
      })
      const res = await fetch(url, { method, headers: { 'Content-Type': 'application/json' }, body })
      if (!res.ok) {
        const err = await res.json().catch(() => ({}))
        throw new Error((err as { title?: string }).title ?? 'Failed')
      }
      notifications.show({ title: editId ? '更新成功' : '创建成功', message: '场景已保存', color: 'green' })
      navigate('/admin/scenarios')
    } catch (e) {
      notifications.show({ title: '操作失败', message: (e as Error).message, color: 'red' })
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <AdminPage>
      <Stack gap="lg" w="100%" maw={980} mx="auto">
        <Stack gap={2}>
          <Title order={2}>{editId ? '编辑场景' : '创建新场景'}</Title>
          <Text size="sm" c="dimmed">
            配置多阶段场景、环境模板、提交类型与评分权重。
          </Text>
        </Stack>

        <YinyuPanel p="lg">
          <Stepper active={activeStep} onStepClick={setActiveStep} allowNextStepsSelect={false}>
            <Stepper.Step label="基本信息">
              <TextInput
                data-testid="scenario-title"
                label="场景标题"
                required
                value={title}
                onChange={(e) => setTitle(e.currentTarget.value)}
              />
              <Textarea
                data-testid="scenario-description"
                label="场景描述"
                mt="md"
                minRows={4}
                value={description}
                onChange={(e) => setDescription(e.currentTarget.value)}
              />
              {loadingGames ? (
                <div className="yy-admin-field-loader">
                  <YinyuRouteLoader title="赛事列表" description="正在读取可关联赛事" />
                </div>
              ) : (
                <Select
                  label="所属赛事"
                  data-testid="scenario-game"
                  required
                  mt="md"
                  placeholder="选择赛事"
                  data={games}
                  value={gameId}
                  onChange={setGameId}
                  searchable
                />
              )}
            </Stepper.Step>

            <Stepper.Step label="阶段配置">
              {loadingTemplates && (
                <div className="yy-admin-field-loader">
                  <YinyuRouteLoader title="环境模板" description="正在读取可用模板" />
                </div>
              )}
              {stages.map((stage, i) => (
                <YinyuPanel key={i} p="md" mt="md">
                  <Group justify="space-between" mb="xs">
                    <Badge size="lg">阶段 {i + 1}</Badge>
                    <ActionIcon color="red" onClick={() => removeStage(i)}>
                      <Icon path={mdiClose} size={0.85} />
                    </ActionIcon>
                  </Group>
                  <TextInput
                    data-testid={`stage-title-${i}`}
                    label="阶段名称"
                    required
                    value={stage.title}
                    onChange={(e) => {
                      const next = [...stages]
                      next[i].title = e.currentTarget.value
                      setStages(next)
                    }}
                  />
                  <Textarea
                    data-testid={`stage-skill-${i}`}
                    label="考察能力说明"
                    mt="sm"
                    value={stage.skillDescription}
                    onChange={(e) => {
                      const next = [...stages]
                      next[i].skillDescription = e.currentTarget.value
                      setStages(next)
                    }}
                  />
                  <Select
                    data-testid={`stage-image-${i}`}
                    label="环境模板"
                    mt="sm"
                    placeholder="选择镜像"
                    data={templates}
                    searchable
                    clearable
                    value={stage.environmentImageIds[0] ? String(stage.environmentImageIds[0]) : null}
                    onChange={(value) => {
                      const next = [...stages]
                      next[i].environmentImageIds = value ? [Number(value)] : []
                      setStages(next)
                    }}
                  />
                  <TextInput
                    data-testid={`stage-flag-${i}`}
                    label="Flag"
                    required
                    mt="sm"
                    value={stage.flag}
                    onChange={(e) => {
                      const next = [...stages]
                      next[i].flag = e.currentTarget.value
                      setStages(next)
                    }}
                  />
                </YinyuPanel>
              ))}
              <Button
                data-testid="add-stage"
                mt="md"
                variant="outline"
                leftSection={<Icon path={mdiPlus} size={0.8} />}
                onClick={addStage}
              >
                添加阶段
              </Button>
            </Stepper.Step>

            <Stepper.Step label="评分配置">
              {scoringRules.map((rule, i) => (
                <YinyuPanel key={i} p="md" mt="md">
                  <Group>
                    <Select
                      label="提交类型"
                      data={SUBMISSION_TYPES}
                      value={rule.submissionType}
                      onChange={(value) => {
                        const next = [...scoringRules]
                        next[i].submissionType = value ?? 'Flag'
                        setScoringRules(next)
                      }}
                    />
                    <NumberInput
                      data-testid={`weight-${rule.submissionType}`}
                      label="权重 (%)"
                      value={rule.weight}
                      min={0}
                      max={100}
                      onChange={(value) => {
                        const next = [...scoringRules]
                        next[i].weight = Number(value) || 0
                        setScoringRules(next)
                      }}
                    />
                    <Select
                      label="验证方式"
                      data={[
                        { value: 'AutoExact', label: '自动精确' },
                        { value: 'AutoRegex', label: '自动正则' },
                        { value: 'ManualReview', label: '人工评审' },
                      ]}
                      value={rule.verificationMode}
                      onChange={(value) => {
                        const next = [...scoringRules]
                        next[i].verificationMode = value ?? 'AutoExact'
                        setScoringRules(next)
                      }}
                    />
                    <NumberInput
                      label="最大尝试"
                      value={rule.maxAttempts}
                      min={0}
                      onChange={(value) => {
                        const next = [...scoringRules]
                        next[i].maxAttempts = Number(value) || 0
                        setScoringRules(next)
                      }}
                    />
                  </Group>
                </YinyuPanel>
              ))}
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
          {activeStep < 2 ? (
            <Button onClick={() => setActiveStep(activeStep + 1)}>下一步</Button>
          ) : (
            <Button data-testid="submit-scenario" loading={submitting} onClick={handleSubmit}>
              {editId ? '更新场景' : '创建场景'}
            </Button>
          )}
        </Group>
      </Stack>
    </AdminPage>
  )
}
