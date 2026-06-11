import { Alert, Badge, Button, Group, Modal, Progress, Stack, Text, TextInput, Title } from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { useCallback, useEffect, useState } from 'react'
import TimeSlotPicker from '../../components/scenario/TimeSlotPicker'
import { YinyuModalBody, YinyuPanel } from '../../components/yinyu/YinyuUI'
import { CheckpointCompletedPayload, scenarioHub } from '../../services/scenarioHub'

interface CheckpointInfo {
  id: number
  description: string
  verificationType: string
  score: number
  completed: boolean
}

interface IRInstanceStatus {
  instanceId: string
  challengeId: number
  status: string
  remainingTime: string
  accessDetails: {
    linux?: { protocol: string; host: string; port: number; username: string; credential: string }
    windows?: { protocol: string; connectionUrl: string; token: string }
  }
  checkpoints: CheckpointInfo[]
  totalScore: number
}

export default function IRChallengePlayer({ challengeId }: { challengeId: number }) {
  const [instance, setInstance] = useState<IRInstanceStatus | null>(null)
  const [answer, setAnswer] = useState('')
  const [submittingCp, setSubmittingCp] = useState<number | null>(null)
  const [resetModal, setResetModal] = useState(false)
  const [resetting, setResetting] = useState(false)

  const loadStatus = useCallback(async (instanceId: string) => {
    const res = await fetch(`/api/v1/ir-challenges/instances/${instanceId}`)
    if (res.ok) setInstance(await res.json())
  }, [])

  useEffect(() => {
    const handleCheckpointCompleted = (payload: CheckpointCompletedPayload) => {
      notifications.show({ title: '检查点完成', message: `+${payload.score} 分`, color: 'green' })
      if (instance) loadStatus(instance.instanceId)
    }
    const handleEnvironmentResetComplete = () => {
      notifications.show({ title: '环境已重置', message: '环境已恢复至初始状态', color: 'blue' })
      setResetting(false)
      if (instance) loadStatus(instance.instanceId)
    }

    scenarioHub.onCheckpointCompleted(handleCheckpointCompleted)
    scenarioHub.onEnvironmentResetComplete(handleEnvironmentResetComplete)

    return () => {
      scenarioHub.offCheckpointCompleted(handleCheckpointCompleted)
    }
  }, [instance, loadStatus])

  const handleReserved = async (slot: { id: number }) => {
    const res = await fetch(`/api/v1/ir-challenges/${challengeId}/instances`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ timeSlotId: slot.id }),
    })
    if (res.ok) {
      const data = await res.json()
      setInstance(data)
      await scenarioHub.joinIR(data.instanceId)
    }
  }

  const submitCheckpointAnswer = async (checkpointId: number) => {
    if (!instance || !answer.trim()) return
    setSubmittingCp(checkpointId)
    try {
      const res = await fetch(
        `/api/v1/ir-challenges/instances/${instance.instanceId}/checkpoints/${checkpointId}/submit`,
        { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ answer }) }
      )
      if (res.ok) {
        setAnswer('')
        await loadStatus(instance.instanceId)
      }
    } finally {
      setSubmittingCp(null)
    }
  }

  const handleReset = async () => {
    if (!instance) return
    setResetting(true)
    try {
      await fetch(`/api/v1/ir-challenges/instances/${instance.instanceId}/reset`, { method: 'POST' })
      setResetModal(false)
    } catch {
      notifications.show({ title: '重置失败', message: '请重试', color: 'red' })
      setResetting(false)
    }
  }

  if (!instance) {
    return <TimeSlotPicker scenarioId={challengeId} onReserved={handleReserved} />
  }

  const completedCount = instance.checkpoints.filter((checkpoint) => checkpoint.completed).length
  const totalCount = instance.checkpoints.length
  const progress = totalCount > 0 ? (completedCount / totalCount) * 100 : 0

  return (
    <Stack maw={900} mx="auto" p="md" gap="lg">
      <Group justify="space-between" align="flex-start">
        <Stack gap={2}>
          <Title order={2}>应急响应挑战</Title>
          <Text size="sm" className="yy-readable-text">
            查看靶机访问方式，完成检查点验证，并在需要时重置环境。
          </Text>
        </Stack>
        <Group>
          <Badge size="lg" className="yy-status-badge">
            {instance.remainingTime}
          </Badge>
          <Badge color={instance.status === 'Ready' ? 'green' : 'yellow'} className="yy-status-badge">
            {instance.status}
          </Badge>
        </Group>
      </Group>

      <YinyuPanel p="md" className="admin-panel">
        <Text fw={700} mb="sm">
          环境访问信息
        </Text>
        {instance.accessDetails?.linux && (
          <Alert color="blue" mb="sm">
            <Text size="sm">
              SSH: ssh {instance.accessDetails.linux.username}@{instance.accessDetails.linux.host} -p{' '}
              {instance.accessDetails.linux.port}
            </Text>
            <Text size="sm">密码: {instance.accessDetails.linux.credential}</Text>
          </Alert>
        )}
        {instance.accessDetails?.windows && (
          <Button
            component="a"
            href={instance.accessDetails.windows.connectionUrl}
            target="_blank"
            color="teal"
            fullWidth
          >
            打开 Web 远程桌面
          </Button>
        )}
      </YinyuPanel>

      <YinyuPanel p="md" className="admin-panel">
        <Text fw={700} mb="md">
          检查点 ({completedCount}/{totalCount})
        </Text>
        <Progress value={progress} mb="lg" />
        <div data-testid="checkpoint-list">
          {instance.checkpoints.map((checkpoint, index) => (
            <YinyuPanel
              key={checkpoint.id}
              data-testid={`checkpoint-item-${index}`}
              p="sm"
              mt="sm"
              cells={18}
              className="task-row"
            >
              <Group justify="space-between">
                <div>
                  <Text fw={500}>{checkpoint.description}</Text>
                  <Text size="sm" className="yy-readable-text">
                    {checkpoint.score} 分 | {checkpoint.verificationType}
                  </Text>
                </div>
                {checkpoint.completed ? (
                  <Badge color="green">已完成</Badge>
                ) : checkpoint.verificationType === 'ManualAnswer' ? (
                  <Group gap="xs">
                    <TextInput
                      data-testid={`checkpoint-answer-${index}`}
                      placeholder="输入答案..."
                      value={answer}
                      onChange={(event) => setAnswer(event.currentTarget.value)}
                    />
                    <Button
                      data-testid={`submit-checkpoint-${index}`}
                      size="xs"
                      loading={submittingCp === checkpoint.id}
                      onClick={() => submitCheckpointAnswer(checkpoint.id)}
                    >
                      提交
                    </Button>
                  </Group>
                ) : (
                  <Badge color="yellow">未完成</Badge>
                )}
              </Group>
            </YinyuPanel>
          ))}
        </div>
      </YinyuPanel>

      <Group justify="flex-end">
        <Button data-testid="reset-environment" variant="outline" color="orange" onClick={() => setResetModal(true)}>
          重置环境
        </Button>
      </Group>

      <Modal
        opened={resetModal}
        onClose={() => setResetModal(false)}
        title="确认重置环境"
        data-testid="reset-confirmation"
      >
        <YinyuModalBody p="md">
          <Text className="yy-readable-text">
            重置后环境将恢复至初始状态，所有当前操作进度可能丢失。
          </Text>
          <Group justify="flex-end" mt="md">
            <Button variant="default" onClick={() => setResetModal(false)}>
              取消
            </Button>
            <Button data-testid="confirm-reset" color="orange" loading={resetting} onClick={handleReset}>
              确认重置
            </Button>
          </Group>
        </YinyuModalBody>
      </Modal>
    </Stack>
  )
}
