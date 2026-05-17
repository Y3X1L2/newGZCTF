import { useState } from 'react';
import { Stepper, Button, TextInput, Textarea, Select, NumberInput, Group, Card, ActionIcon, Badge } from '@mantine/core';
import { notifications } from '@mantine/notifications';
import { useNavigate } from 'react-router';

interface CheckpointData {
  orderIndex: number;
  description: string;
  verificationType: string;
  verificationConfig: string;
  score: number;
  isRequired: boolean;
}

const VERIFICATION_TYPES = [
  { value: 'AutoCommand', label: '自动命令检测' },
  { value: 'AutoScript', label: '自动脚本检测' },
  { value: 'ManualAnswer', label: '手动答案提交' },
  { value: 'ManualReview', label: '人工评审' },
];

export default function IRChallengeCreate() {
  const navigate = useNavigate();
  const [activeStep, setActiveStep] = useState(0);
  const [title, setTitle] = useState('');
  const [description, setDescription] = useState('');
  const [gameId, setGameId] = useState<number | ''>('');
  const [osType, setOsType] = useState<string>('Linux');
  const [checkpoints, setCheckpoints] = useState<CheckpointData[]>([]);
  const [submitting, setSubmitting] = useState(false);

  const addCheckpoint = () => {
    setCheckpoints([...checkpoints, {
      orderIndex: checkpoints.length + 1,
      description: '',
      verificationType: 'AutoCommand',
      verificationConfig: '{}',
      score: 100,
      isRequired: true,
    }]);
  };

  const removeCheckpoint = (index: number) => {
    setCheckpoints(checkpoints.filter((_, i) => i !== index).map((c, i) => ({ ...c, orderIndex: i + 1 })));
  };

  const handleSubmit = async () => {
    if (!title || !gameId || checkpoints.length === 0) return;
    setSubmitting(true);
    try {
      const res = await fetch('/api/v1/ir-challenges', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ title, description, gameId, osType, checkpoints }),
      });
      if (!res.ok) throw new Error('创建失败');
      notifications.show({ title: '创建成功', message: 'IR 题目已创建', color: 'green' });
      navigate('/admin/ir-challenges');
    } catch {
      notifications.show({ title: '创建失败', message: '请检查输入后重试', color: 'red' });
    } finally { setSubmitting(false); }
  };

  return (
    <div style={{ maxWidth: 800, margin: '0 auto', padding: '2rem' }}>
      <h1>创建应急响应题目</h1>
      <Stepper active={activeStep} onStepClick={setActiveStep} allowNextStepsSelect={false}>
        <Stepper.Step label="基本信息" description="题目描述和靶机配置">
          <TextInput data-testid="ir-title" label="题目名称" required value={title}
            onChange={e => setTitle(e.currentTarget.value)} />
          <Textarea data-testid="ir-description" label="应急场景描述" required mt="md" minRows={4}
            value={description} onChange={e => setDescription(e.currentTarget.value)} />
          <Select label="所属赛事" required mt="md" placeholder="选择赛事" data={[]}
            value={gameId.toString()} onChange={v => setGameId(v ? parseInt(v) : '')} />
          <Select data-testid="ir-os-type" label="靶机系统" required mt="md"
            data={[{ value: 'Linux', label: 'Linux (SSH 访问)' }, { value: 'Windows', label: 'Windows (Web 桌面代理)' }]}
            value={osType} onChange={v => setOsType(v ?? 'Linux')} />
        </Stepper.Step>

        <Stepper.Step label="检查点配置" description="设置验证目标和分值">
          {checkpoints.map((cp, i) => (
            <Card key={i} shadow="sm" padding="md" mt="md" withBorder>
              <Group justify="space-between" mb="xs">
                <Badge size="lg">检查点 {i + 1}</Badge>
                <ActionIcon color="red" onClick={() => removeCheckpoint(i)}>×</ActionIcon>
              </Group>
              <Textarea data-testid={`checkpoint-desc-${i}`} label="检查点描述" required
                value={cp.description} onChange={e => {
                  const updated = [...checkpoints];
                  updated[i].description = e.currentTarget.value;
                  setCheckpoints(updated);
                }} />
              <Select data-testid={`checkpoint-verify-type-${i}`} label="验证方式" mt="sm"
                data={VERIFICATION_TYPES} value={cp.verificationType} onChange={v => {
                  const updated = [...checkpoints];
                  updated[i].verificationType = v ?? 'AutoCommand';
                  setCheckpoints(updated);
                }} />
              <NumberInput data-testid={`checkpoint-score-${i}`} label="分值" mt="sm"
                value={cp.score} min={1} onChange={v => {
                  const updated = [...checkpoints];
                  updated[i].score = Number(v) || 1;
                  setCheckpoints(updated);
                }} />
            </Card>
          ))}
          <Button data-testid="add-checkpoint" mt="md" variant="outline" onClick={addCheckpoint}>
            + 添加检查点
          </Button>
        </Stepper.Step>
      </Stepper>

      <Group justify="flex-end" mt="xl">
        <Button variant="default" onClick={() => setActiveStep(Math.max(0, activeStep - 1))}
          disabled={activeStep === 0}>上一步</Button>
        {activeStep < 1 ? (
          <Button onClick={() => setActiveStep(activeStep + 1)}>下一步</Button>
        ) : (
          <Button data-testid="submit-ir-challenge" loading={submitting} onClick={handleSubmit}>创建题目</Button>
        )}
      </Group>
    </div>
  );
}
