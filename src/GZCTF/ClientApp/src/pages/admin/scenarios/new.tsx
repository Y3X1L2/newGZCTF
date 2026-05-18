import { useState, useEffect } from 'react'
import {
  Stepper, Button, TextInput, Textarea, Select, NumberInput, Group, Card, Badge, ActionIcon, Loader,
} from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { useNavigate, useParams } from 'react-router'

interface StageData { title: string; skillDescription: string; flag: string; environmentImageIds: number[] }
interface ScoringRuleData { submissionType: string; weight: number; verificationMode: string; maxAttempts: number }

interface GameOption { value: string; label: string }
interface TemplateOption { value: string; label: string; osType: string; imageType: string }

const SUBMISSION_TYPES = [
  { value: 'Flag', label: 'Flag' }, { value: 'Writeup', label: '解题报告' },
  { value: 'IP', label: '攻击者 IP' }, { value: 'Credential', label: '关键凭证' },
]

export default function ScenarioCreate() {
  const navigate = useNavigate()
  const { id: editId } = useParams<{ id?: string }>()
  const isEdit = !!editId

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

  // Data from APIs
  const [games, setGames] = useState<GameOption[]>([])
  const [templates, setTemplates] = useState<TemplateOption[]>([])
  const [loadingGames, setLoadingGames] = useState(true)
  const [loadingTemplates, setLoadingTemplates] = useState(true)

  // Fetch games
  useEffect(() => {
    fetch('/api/edit/Games?count=100')
      .then(r => r.json())
      .then(data => {
        const list = Array.isArray(data) ? data : (data.data ?? [])
        setGames(list.map((g: { id: number; title: string }) => ({ value: String(g.id), label: g.title })))
      }).catch(() => {}).finally(() => setLoadingGames(false))
  }, [])

  // Fetch image templates
  useEffect(() => {
    fetch('/api/v1/image-templates')
      .then(r => r.json())
      .then(data => {
        const list = Array.isArray(data) ? data : (data.data ?? data.items ?? [])
        setTemplates(list.map((t: { id: number; name: string; osType: string; imageType: string }) => ({
          value: String(t.id), label: `${t.name} (${t.osType}/${t.imageType})`, osType: t.osType, imageType: t.imageType,
        })))
      }).catch(() => {}).finally(() => setLoadingTemplates(false))
  }, [])

  // Load scenario data if editing
  useEffect(() => {
    if (editId) {
      fetch(`/api/v1/scenarios/${editId}`)
        .then(r => r.json())
        .then(data => {
          setTitle(data.title ?? '')
          setDescription(data.description ?? '')
          setGameId(String(data.gameId ?? ''))
          if (data.stages?.length) {
            setStages(data.stages.map((s: { title: string; skillDescription?: string; flag?: string; environmentImageIds?: number[] }) => ({
              title: s.title,
              skillDescription: s.skillDescription ?? '',
              flag: '',
              environmentImageIds: s.environmentImageIds ?? [],
            })))
          }
          if (data.scoringRules?.length) {
            setScoringRules(data.scoringRules.map((r: { submissionType?: string; weight?: number; verificationMode?: string; maxAttempts?: number }) => ({
              submissionType: r.submissionType ?? 'Flag', weight: r.weight ?? 0,
              verificationMode: r.verificationMode ?? 'AutoExact', maxAttempts: r.maxAttempts ?? 0,
            })))
          }
        }).catch(() => {})
    }
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
      notifications.show({ title: '评分权重错误', message: '权重之和必须为100%', color: 'red' })
      return
    }

    setSubmitting(true)
    try {
      const url = editId ? `/api/v1/scenarios/${editId}` : '/api/v1/scenarios'
      const method = editId ? 'PUT' : 'POST'
      const body = JSON.stringify({
        title, description, gameId: Number(gameId),
        stages: stages.map((s, i) => ({ ...s, orderIndex: i + 1 })),
        scoringRules,
      })
      const res = await fetch(url, { method, headers: { 'Content-Type': 'application/json' }, body })
      if (!res.ok) { const err = await res.json().catch(() => ({})); throw new Error((err as { title?: string }).title ?? 'Failed') }
      notifications.show({ title: editId ? '更新成功' : '创建成功', color: 'green' })
      navigate('/admin/scenarios')
    } catch (e) {
      notifications.show({ title: '操作失败', message: (e as Error).message, color: 'red' })
    } finally { setSubmitting(false) }
  }

  return (
    <div style={{ maxWidth: 900, margin: '0 auto', padding: '2rem' }}>
      <h1>{editId ? '编辑场景' : '创建新场景'}</h1>
      <Stepper active={activeStep} onStepClick={setActiveStep} allowNextStepsSelect={false}>
        <Stepper.Step label="基本信息">
          <TextInput data-testid="scenario-title" label="场景标题" required value={title} onChange={e => setTitle(e.currentTarget.value)} />
          <Textarea data-testid="scenario-description" label="场景描述" mt="md" minRows={4} value={description} onChange={e => setDescription(e.currentTarget.value)} />
          {loadingGames ? <Loader size="sm" mt="md" /> :
            <Select label="所属赛事" data-testid="scenario-game" required mt="md" placeholder="选择赛事"
              data={games} value={gameId} onChange={setGameId} searchable />
          }
        </Stepper.Step>

        <Stepper.Step label="阶段配置">
          {loadingTemplates && <Loader size="sm" />}
          {stages.map((stage, i) => (
            <Card key={i} shadow="sm" padding="md" mt="md" withBorder>
              <Group justify="space-between" mb="xs">
                <Badge size="lg">阶段 {i + 1}</Badge>
                <ActionIcon color="red" onClick={() => removeStage(i)}>×</ActionIcon>
              </Group>
              <TextInput data-testid={`stage-title-${i}`} label="阶段名称" required value={stage.title}
                onChange={e => { const u = [...stages]; u[i].title = e.currentTarget.value; setStages(u) }} />
              <Textarea data-testid={`stage-skill-${i}`} label="考察能力说明" mt="sm" value={stage.skillDescription}
                onChange={e => { const u = [...stages]; u[i].skillDescription = e.currentTarget.value; setStages(u) }} />
              <Select data-testid={`stage-image-${i}`} label="环境模板" mt="sm" placeholder="选择镜像"
                data={templates} searchable clearable
                value={stage.environmentImageIds[0] ? String(stage.environmentImageIds[0]) : null}
                onChange={v => { const u = [...stages]; u[i].environmentImageIds = v ? [Number(v)] : []; setStages(u) }} />
              <TextInput data-testid={`stage-flag-${i}`} label="Flag" required mt="sm" value={stage.flag}
                onChange={e => { const u = [...stages]; u[i].flag = e.currentTarget.value; setStages(u) }} />
            </Card>
          ))}
          <Button data-testid="add-stage" mt="md" variant="outline" onClick={addStage}>+ 添加阶段</Button>
        </Stepper.Step>

        <Stepper.Step label="评分配置">
          {scoringRules.map((rule, i) => (
            <Card key={i} shadow="sm" padding="md" mt="md" withBorder>
              <Group>
                <Select label="提交类型" data={SUBMISSION_TYPES} value={rule.submissionType}
                  onChange={v => { const u = [...scoringRules]; u[i].submissionType = v ?? 'Flag'; setScoringRules(u) }} />
                <NumberInput data-testid={`weight-${rule.submissionType}`} label="权重 (%)" value={rule.weight} min={0} max={100}
                  onChange={v => { const u = [...scoringRules]; u[i].weight = Number(v) || 0; setScoringRules(u) }} />
                <Select label="验证方式" data={[{ value: 'AutoExact', label: '自动精确' }, { value: 'AutoRegex', label: '自动正则' }, { value: 'ManualReview', label: '人工评审' }]}
                  value={rule.verificationMode}
                  onChange={v => { const u = [...scoringRules]; u[i].verificationMode = v ?? 'AutoExact'; setScoringRules(u) }} />
                <NumberInput label="最大尝试" value={rule.maxAttempts} min={0}
                  onChange={v => { const u = [...scoringRules]; u[i].maxAttempts = Number(v) || 0; setScoringRules(u) }} />
              </Group>
            </Card>
          ))}
        </Stepper.Step>
      </Stepper>
      <Group justify="flex-end" mt="xl">
        <Button variant="default" onClick={() => setActiveStep(Math.max(0, activeStep - 1))} disabled={activeStep === 0}>上一步</Button>
        {activeStep < 2 ? <Button onClick={() => setActiveStep(activeStep + 1)}>下一步</Button>
          : <Button data-testid="submit-scenario" loading={submitting} onClick={handleSubmit}>{editId ? '更新场景' : '创建场景'}</Button>}
      </Group>
    </div>
  )
}
