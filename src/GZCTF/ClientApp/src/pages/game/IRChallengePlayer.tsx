import { useState, useEffect, useCallback } from 'react';
import { Card, Badge, Button, Group, Text, Alert, Progress, TextInput, Modal } from '@mantine/core';
import { notifications } from '@mantine/notifications';
import { scenarioHub } from '../../services/scenarioHub';
import TimeSlotPicker from '../../components/scenario/TimeSlotPicker';

interface CheckpointInfo {
  id: number;
  description: string;
  verificationType: string;
  score: number;
  completed: boolean;
}

interface IRInstanceStatus {
  instanceId: string;
  challengeId: number;
  status: string;
  remainingTime: string;
  accessDetails: {
    linux?: { protocol: string; host: string; port: number; username: string; credential: string };
    windows?: { protocol: string; connectionUrl: string; token: string };
  };
  checkpoints: CheckpointInfo[];
  totalScore: number;
}

export default function IRChallengePlayer({ challengeId }: { challengeId: number }) {
  const [instance, setInstance] = useState<IRInstanceStatus | null>(null);
  const [answer, setAnswer] = useState('');
  const [submittingCp, setSubmittingCp] = useState<number | null>(null);
  const [resetModal, setResetModal] = useState(false);
  const [resetting, setResetting] = useState(false);

  const loadStatus = useCallback(async (instanceId: string) => {
    const res = await fetch(`/api/v1/ir-challenges/instances/${instanceId}`);
    if (res.ok) setInstance(await res.json());
  }, []);

  useEffect(() => {
    scenarioHub.onCheckpointCompleted((payload) => {
      notifications.show({ title: '检查点完成', message: `+${payload.score} 分`, color: 'green' });
      if (instance) loadStatus(instance.instanceId);
    });
    scenarioHub.onEnvironmentResetComplete(() => {
      notifications.show({ title: '环境已重置', message: '环境已恢复至初始状态', color: 'blue' });
      setResetting(false);
    });
    return () => {
      scenarioHub.offCheckpointCompleted(() => {});
    };
  }, [instance, loadStatus]);

  const handleReserved = async (slot: { id: number }) => {
    const res = await fetch(`/api/v1/ir-challenges/${challengeId}/instances`, {
      method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ timeSlotId: slot.id }),
    });
    if (res.ok) {
      const data = await res.json();
      setInstance(data);
      await scenarioHub.joinIR(data.instanceId);
    }
  };

  const submitCheckpointAnswer = async (checkpointId: number) => {
    if (!instance || !answer.trim()) return;
    setSubmittingCp(checkpointId);
    try {
      const res = await fetch(
        `/api/v1/ir-challenges/instances/${instance.instanceId}/checkpoints/${checkpointId}/submit`,
        { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ answer }) }
      );
      if (res.ok) {
        setAnswer('');
        await loadStatus(instance.instanceId);
      }
    } finally { setSubmittingCp(null); }
  };

  const handleReset = async () => {
    if (!instance) return;
    setResetting(true);
    try {
      await fetch(`/api/v1/ir-challenges/instances/${instance.instanceId}/reset`, { method: 'POST' });
      setResetModal(false);
    } catch {
      notifications.show({ title: '重置失败', message: '请重试', color: 'red' });
      setResetting(false);
    }
  };

  if (!instance) {
    return <TimeSlotPicker scenarioId={challengeId} onReserved={handleReserved} />;
  }

  const completedCount = instance.checkpoints.filter(c => c.completed).length;
  const totalCount = instance.checkpoints.length;

  return (
    <div style={{ maxWidth: 800, margin: '0 auto', padding: '1rem' }}>
      <Group justify="space-between" mb="md">
        <h2>应急响应挑战</h2>
        <Group>
          <Badge size="lg">{instance.remainingTime}</Badge>
          <Badge color={instance.status === 'Ready' ? 'green' : 'yellow'}>{instance.status}</Badge>
        </Group>
      </Group>

      {/* Access Info */}
      <Card shadow="sm" padding="md" withBorder mb="lg">
        <Text fw={700} mb="sm">环境访问信息</Text>
        {instance.accessDetails?.linux && (
          <Alert color="blue" mb="sm">
            <Text size="sm">SSH: ssh {instance.accessDetails.linux.username}@{instance.accessDetails.linux.host} -p {instance.accessDetails.linux.port}</Text>
            <Text size="sm">密码: {instance.accessDetails.linux.credential}</Text>
          </Alert>
        )}
        {instance.accessDetails?.windows && (
          <Button component="a" href={instance.accessDetails.windows.connectionUrl}
            target="_blank" color="teal" fullWidth>
            打开 Web 远程桌面
          </Button>
        )}
      </Card>

      {/* Checkpoints */}
      <Card shadow="sm" padding="md" withBorder mb="lg">
        <Text fw={700} mb="md">检查点 ({completedCount}/{totalCount})</Text>
        <Progress value={(completedCount / totalCount) * 100} mb="lg" />
        <div data-testid="checkpoint-list">
          {instance.checkpoints.map((cp, i) => (
            <Card key={cp.id} data-testid={`checkpoint-item-${i}`} shadow="xs" padding="sm" mt="sm" withBorder>
              <Group justify="space-between">
                <div>
                  <Text fw={500}>{cp.description}</Text>
                  <Text size="sm" c="dimmed">{cp.score} 分 | {cp.verificationType}</Text>
                </div>
                {cp.completed ? (
                  <Badge color="green">已完成</Badge>
                ) : cp.verificationType === 'ManualAnswer' ? (
                  <Group gap="xs">
                    <TextInput data-testid={`checkpoint-answer-${i}`} placeholder="输入答案..."
                      value={answer} onChange={e => setAnswer(e.currentTarget.value)} />
                    <Button data-testid={`submit-checkpoint-${i}`} size="xs"
                      loading={submittingCp === cp.id}
                      onClick={() => submitCheckpointAnswer(cp.id)}>提交</Button>
                  </Group>
                ) : (
                  <Badge color="gray">未完成</Badge>
                )}
              </Group>
            </Card>
          ))}
        </div>
      </Card>

      {/* Reset */}
      <Group justify="flex-end">
        <Button data-testid="reset-environment" variant="outline" color="orange"
          onClick={() => setResetModal(true)}>重置环境</Button>
      </Group>

      <Modal opened={resetModal} onClose={() => setResetModal(false)} title="确认重置环境"
        data-testid="reset-confirmation">
        <Text>重置后环境将恢复至初始状态，所有当前操作进度将丢失。</Text>
        <Group justify="flex-end" mt="md">
          <Button variant="default" onClick={() => setResetModal(false)}>取消</Button>
          <Button data-testid="confirm-reset" color="orange" loading={resetting}
            onClick={handleReset}>确认重置</Button>
        </Group>
      </Modal>
    </div>
  );
}
