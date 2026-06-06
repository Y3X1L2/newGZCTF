import { useState, useEffect, useCallback } from 'react';
import { Card, Badge, Progress, Button, TextInput, Group, Text, Modal, Timeline, Alert, Tabs } from '@mantine/core';
import { notifications } from '@mantine/notifications';
import { scenarioHub } from '../../services/scenarioHub';
import TimeSlotPicker from '../../components/scenario/TimeSlotPicker';
import MultiTypeSubmission from '../../components/scenario/MultiTypeSubmission';

interface StageInfo {
  id: number;
  orderIndex: number;
  title: string;
  skillDescription: string;
  status: 'locked' | 'unlocked' | 'completed';
}

interface InstanceStatus {
  instanceId: string;
  scenarioId: number;
  currentStageId: number;
  stages: StageInfo[];
  timeRemaining: string;
  totalScore: number;
  timeSlot: { startTime: string; endTime: string } | null;
}

export default function ScenarioPlayer({ scenarioId }: { scenarioId: number }) {
  const [instance, setInstance] = useState<InstanceStatus | null>(null);
  const [flag, setFlag] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [flagResult, setFlagResult] = useState<'correct' | 'incorrect' | null>(null);
  const [showCompletion, setShowCompletion] = useState(false);

  const loadStatus = useCallback(async (instanceId: string) => {
    const res = await fetch(`/api/v1/scenarios/instances/${instanceId}`);
    if (res.ok) setInstance(await res.json());
  }, []);

  useEffect(() => {
    scenarioHub.onStageUnlocked((payload) => {
      notifications.show({ title: '阶段解锁', message: `新阶段已解锁：${payload.title}`, color: 'teal' });
      if (instance) loadStatus(instance.instanceId);
    });
    scenarioHub.onTimeWarning((payload) => {
      notifications.show({ title: '时间提醒', message: `还剩 ${payload.remainingMinutes} 分钟`, color: 'orange' });
    });
    scenarioHub.onScoreUpdated((payload) => {
      if (instance) setInstance({ ...instance, totalScore: payload.totalScore });
    });

    return () => {
      scenarioHub.offStageUnlocked(() => {});
      scenarioHub.offTimeWarning(() => {});
      scenarioHub.offScoreUpdated(() => {});
    };
  }, [instance, loadStatus]);

  const handleReserved = async (slot: { id: number }) => {
    const res = await fetch(`/api/v1/scenarios/${scenarioId}/instances`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ timeSlotId: slot.id }),
    });
    if (res.ok) {
      const data = await res.json();
      setInstance(data);
      await scenarioHub.joinScenario(data.instanceId);
    }
  };

  const submitFlag = async () => {
    if (!instance || !flag.trim()) return;
    setSubmitting(true);
    setFlagResult(null);
    try {
      const res = await fetch(
        `/api/v1/scenarios/instances/${instance.instanceId}/stages/${instance.currentStageId}/submit`,
        { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ flag }) }
      );
      const data = await res.json();
      if (data.correct) {
        setFlagResult('correct');
        setFlag('');
        if (data.nextStageUnlocked) {
          setInstance({ ...instance, currentStageId: data.nextStageUnlocked.stageId });
        }
        if (data.allCompleted) setShowCompletion(true);
        await loadStatus(instance.instanceId);
      } else {
        setFlagResult('incorrect');
      }
    } finally {
      setSubmitting(false);
    }
  };

  if (!instance) {
    return <TimeSlotPicker scenarioId={scenarioId} onReserved={handleReserved} />;
  }

  const currentStage = instance.stages.find(s => s.id === instance.currentStageId);

  return (
    <div style={{ maxWidth: 800, margin: '0 auto', padding: '1rem' }}>
      <Group justify="space-between" mb="md">
        <h2>场景挑战</h2>
        <Badge size="lg" data-testid="time-remaining">{instance.timeRemaining}</Badge>
      </Group>

      {/* Stage Progress Timeline */}
      <Timeline active={instance.stages.filter(s => s.status === 'completed').length - 1} mb="xl">
        {instance.stages.map(stage => (
          <Timeline.Item key={stage.id} title={stage.title}
            bullet={<Text size="xs">{stage.orderIndex}</Text>}
            data-testid={`stage-status-${stage.id}`}
            data-status={stage.status}>
            <Text size="sm" c="dimmed">{stage.skillDescription}</Text>
            {stage.status === 'completed' && <Badge color="green">已完成</Badge>}
            {stage.status === 'unlocked' && <Badge color="blue">进行中</Badge>}
            {stage.status === 'locked' && <Badge color="gray">锁定</Badge>}
          </Timeline.Item>
        ))}
      </Timeline>

      {/* Current Stage */}
      {currentStage && (
        <Card shadow="sm" padding="lg" withBorder mb="lg">
          <Text fw={700} size="lg" data-testid="current-stage-title">{currentStage.title}</Text>
          <Text mt="xs">{currentStage.skillDescription}</Text>

          <Tabs defaultValue="flag" mt="lg">
            <Tabs.List>
              <Tabs.Tab value="flag">提交 Flag</Tabs.Tab>
              <Tabs.Tab value="submissions">综合提交</Tabs.Tab>
            </Tabs.List>

            <Tabs.Panel value="flag" pt="md">
              <Group>
                <TextInput data-testid="flag-input" placeholder="输入 Flag..." value={flag}
                  onChange={e => setFlag(e.currentTarget.value)} style={{ flex: 1 }}
                  onKeyDown={e => e.key === 'Enter' && submitFlag()} />
                <Button data-testid="submit-flag" loading={submitting} onClick={submitFlag}>
                  提交
                </Button>
              </Group>
              {flagResult === 'correct' && (
                <Alert color="green" mt="sm" data-testid="flag-correct">Flag 正确！阶段完成。</Alert>
              )}
              {flagResult === 'incorrect' && (
                <Alert color="red" mt="sm" data-testid="flag-incorrect">Flag 错误，请重试。</Alert>
              )}
            </Tabs.Panel>

            <Tabs.Panel value="submissions" pt="md">
              <MultiTypeSubmission challengeId={scenarioId} instanceId={instance.instanceId} />
            </Tabs.Panel>
          </Tabs>
        </Card>
      )}

      {/* Completion Modal */}
      <Modal opened={showCompletion} onClose={() => setShowCompletion(false)} title="挑战完成!" size="lg"
        data-testid="completion-summary">
        <Alert color="green" mb="md" data-testid="completed-all-stages">
          恭喜！你已完成所有阶段。
        </Alert>
        <Text fw={700} data-testid="total-score">总得分: {instance.totalScore}</Text>
        <Text mt="md" size="sm" c="dimmed">
          你的解题报告将计入综合评分，管理员评审后会更新最终得分。
        </Text>
      </Modal>
    </div>
  );
}
