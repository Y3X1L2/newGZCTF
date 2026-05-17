import { useState } from 'react';
import {
  Stepper, Button, TextInput, Textarea, Select, NumberInput, Group, Card, Badge, ActionIcon
} from '@mantine/core';
import { notifications } from '@mantine/notifications';
import { useNavigate } from 'react-router';

interface StageData {
  orderIndex: number;
  title: string;
  skillDescription: string;
  prerequisiteStageIds: number[];
  environmentImageIds: number[];
  flag: string;
}

interface ScoringRuleData {
  submissionType: string;
  weight: number;
  verificationMode: string;
  maxAttempts: number;
}

const SUBMISSION_TYPES = [
  { value: 'Flag', label: 'Flag' },
  { value: 'Writeup', label: '解题报告 (Writeup)' },
  { value: 'IP', label: '攻击者 IP' },
  { value: 'Credential', label: '关键凭证' },
];

export default function ScenarioCreate() {
  const navigate = useNavigate();
  const [activeStep, setActiveStep] = useState(0);
  const [title, setTitle] = useState('');
  const [description, setDescription] = useState('');
  const [gameId, setGameId] = useState<number | ''>('');
  const [stages, setStages] = useState<StageData[]>([]);
  const [scoringRules, setScoringRules] = useState<ScoringRuleData[]>([
    { submissionType: 'Flag', weight: 50, verificationMode: 'AutoExact', maxAttempts: 10 },
    { submissionType: 'Writeup', weight: 30, verificationMode: 'ManualReview', maxAttempts: 3 },
    { submissionType: 'IP', weight: 20, verificationMode: 'AutoExact', maxAttempts: 5 },
  ]);
  const [submitting, setSubmitting] = useState(false);

  const addStage = () => {
    setStages([
      ...stages,
      {
        orderIndex: stages.length + 1,
        title: '',
        skillDescription: '',
        prerequisiteStageIds: [],
        environmentImageIds: [],
        flag: '',
      },
    ]);
  };

  const removeStage = (index: number) => {
    setStages(stages.filter((_, i) => i !== index).map((s, i) => ({ ...s, orderIndex: i + 1 })));
  };

  const handleSubmit = async () => {
    if (!title || !gameId || stages.length < 2) return;
    const totalWeight = scoringRules.reduce((sum, r) => sum + r.weight, 0);
    if (Math.abs(totalWeight - 100) > 0.01) {
      notifications.show({ title: '评分权重错误', message: '权重之和必须为100%', color: 'red' });
      return;
    }

    setSubmitting(true);
    try {
      const res = await fetch('/api/v1/scenarios', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ title, description, gameId, stages, scoringRules }),
      });
      if (!res.ok) throw new Error('创建失败');
      const data = await res.json();
      notifications.show({ title: '创建成功', message: `场景 "${data.title}" 已创建`, color: 'green' });
      navigate('/admin/scenarios');
    } catch {
      notifications.show({ title: '创建失败', message: '请检查输入后重试', color: 'red' });
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div style={{ maxWidth: 900, margin: '0 auto', padding: '2rem' }}>
      <h1>创建新场景</h1>

      <Stepper active={activeStep} onStepClick={setActiveStep} allowNextStepsSelect={false}>
        {/* Step 1: Basic Info */}
        <Stepper.Step label="基本信息" description="场景标题和描述">
          <TextInput data-testid="scenario-title" label="场景标题" required value={title}
            onChange={e => setTitle(e.currentTarget.value)} />
          <Textarea data-testid="scenario-description" label="场景描述" required mt="md" minRows={4}
            value={description} onChange={e => setDescription(e.currentTarget.value)}
            placeholder="描述场景的故事背景和考察目标..." />
          <Select label="所属赛事" data-testid="scenario-game" required mt="md"
            placeholder="选择赛事" data={[]} value={gameId.toString()}
            onChange={v => setGameId(v ? parseInt(v) : '')} />
        </Stepper.Step>

        {/* Step 2: Stages */}
        <Stepper.Step label="阶段配置" description="配置攻击链各阶段">
          {stages.map((stage, i) => (
            <Card key={i} shadow="sm" padding="md" mt="md" withBorder>
              <Group justify="space-between" mb="xs">
                <Badge size="lg">阶段 {i + 1}</Badge>
                <ActionIcon color="red" onClick={() => removeStage(i)}>×</ActionIcon>
              </Group>
              <TextInput data-testid={`stage-title-${i}`} label="阶段名称" required
                value={stage.title} onChange={e => {
                  const updated = [...stages];
                  updated[i].title = e.currentTarget.value;
                  setStages(updated);
                }} />
              <Textarea data-testid={`stage-skill-${i}`} label="考察能力说明" required mt="sm"
                value={stage.skillDescription} onChange={e => {
                  const updated = [...stages];
                  updated[i].skillDescription = e.currentTarget.value;
                  setStages(updated);
                }} />
              <Select data-testid={`stage-image-${i}`} label="环境模板" mt="sm" placeholder="选择镜像"
                data={[]} />
              <TextInput data-testid={`stage-flag-${i}`} label="Flag" required mt="sm"
                value={stage.flag} onChange={e => {
                  const updated = [...stages];
                  updated[i].flag = e.currentTarget.value;
                  setStages(updated);
                }} />
            </Card>
          ))}
          <Button data-testid="add-stage" mt="md" variant="outline" onClick={addStage}>
            + 添加阶段
          </Button>
        </Stepper.Step>

        {/* Step 3: Scoring */}
        <Stepper.Step label="评分配置" description="设置提交类型和权重">
          {scoringRules.map((rule, i) => (
            <Card key={i} shadow="sm" padding="md" mt="md" withBorder>
              <Group>
                <Select label="提交类型" data={SUBMISSION_TYPES} value={rule.submissionType}
                  onChange={v => {
                    const updated = [...scoringRules];
                    updated[i].submissionType = v ?? 'Flag';
                    setScoringRules(updated);
                  }} />
                <NumberInput data-testid={`weight-${rule.submissionType}`} label="权重 (%)"
                  value={rule.weight} min={0} max={100}
                  onChange={v => {
                    const updated = [...scoringRules];
                    updated[i].weight = Number(v) || 0;
                    setScoringRules(updated);
                  }} />
                <Select label="验证方式" data={[
                  { value: 'AutoExact', label: '自动精确匹配' },
                  { value: 'AutoRegex', label: '自动正则匹配' },
                  { value: 'ManualReview', label: '人工评审' },
                ]} value={rule.verificationMode} onChange={v => {
                  const updated = [...scoringRules];
                  updated[i].verificationMode = v ?? 'AutoExact';
                  setScoringRules(updated);
                }} />
                <NumberInput label="最大尝试" value={rule.maxAttempts} min={0}
                  onChange={v => {
                    const updated = [...scoringRules];
                    updated[i].maxAttempts = Number(v) || 0;
                    setScoringRules(updated);
                  }} />
              </Group>
            </Card>
          ))}
        </Stepper.Step>
      </Stepper>

      <Group justify="flex-end" mt="xl">
        <Button variant="default" onClick={() => setActiveStep(Math.max(0, activeStep - 1))}
          disabled={activeStep === 0}>
          上一步
        </Button>
        {activeStep < 2 ? (
          <Button onClick={() => setActiveStep(activeStep + 1)}>下一步</Button>
        ) : (
          <Button data-testid="submit-scenario" loading={submitting} onClick={handleSubmit}>
            创建场景
          </Button>
        )}
      </Group>
    </div>
  );
}
