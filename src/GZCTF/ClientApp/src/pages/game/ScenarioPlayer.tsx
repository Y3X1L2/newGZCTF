import { Alert, Badge, Button, Group, Modal, Stack, Tabs, Text, TextInput, Timeline, Title } from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { useCallback, useEffect, useState } from 'react'
import MultiTypeSubmission from '../../components/scenario/MultiTypeSubmission'
import TimeSlotPicker from '../../components/scenario/TimeSlotPicker'
import { YinyuModalBody, YinyuPanel } from '../../components/yinyu/YinyuUI'
import {
  scenarioHub,
  ScoreUpdatedPayload,
  StageUnlockedPayload,
  TimeWarningPayload,
} from '../../services/scenarioHub'

interface StageInfo {
  id: number
  orderIndex: number
  title: string
  skillDescription: string
  status: 'locked' | 'unlocked' | 'completed'
}

interface InstanceStatus {
  instanceId: string
  scenarioId: number
  currentStageId: number
  stages: StageInfo[]
  timeRemaining: string
  totalScore: number
  timeSlot: { startTime: string; endTime: string } | null
}

const stageStatusLabel = (status: StageInfo['status']) =>
  status === 'completed' ? '已完成' : status === 'unlocked' ? '进行中' : '锁定'

const stageStatusColor = (status: StageInfo['status']) =>
  status === 'completed' ? 'green' : status === 'unlocked' ? 'blue' : 'yellow'

export default function ScenarioPlayer({ scenarioId }: { scenarioId: number }) {
  const [instance, setInstance] = useState<InstanceStatus | null>(null)
  const [flag, setFlag] = useState('')
  const [submitting, setSubmitting] = useState(false)
  const [flagResult, setFlagResult] = useState<'correct' | 'incorrect' | null>(null)
  const [showCompletion, setShowCompletion] = useState(false)

  const loadStatus = useCallback(async (instanceId: string) => {
    const res = await fetch(`/api/v1/scenarios/instances/${instanceId}`)
    if (res.ok) setInstance(await res.json())
  }, [])

  useEffect(() => {
    const handleStageUnlocked = (payload: StageUnlockedPayload) => {
      notifications.show({ title: '阶段解锁', message: `新阶段已解锁：${payload.title}`, color: 'teal' })
      if (instance) loadStatus(instance.instanceId)
    }
    const handleTimeWarning = (payload: TimeWarningPayload) => {
      notifications.show({ title: '时间提醒', message: `还剩 ${payload.remainingMinutes} 分钟`, color: 'orange' })
    }
    const handleScoreUpdated = (payload: ScoreUpdatedPayload) => {
      if (instance) setInstance({ ...instance, totalScore: payload.totalScore })
    }

    scenarioHub.onStageUnlocked(handleStageUnlocked)
    scenarioHub.onTimeWarning(handleTimeWarning)
    scenarioHub.onScoreUpdated(handleScoreUpdated)

    return () => {
      scenarioHub.offStageUnlocked(handleStageUnlocked)
      scenarioHub.offTimeWarning(handleTimeWarning)
      scenarioHub.offScoreUpdated(handleScoreUpdated)
    }
  }, [instance, loadStatus])

  const handleReserved = async (slot: { id: number }) => {
    const res = await fetch(`/api/v1/scenarios/${scenarioId}/instances`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ timeSlotId: slot.id }),
    })
    if (res.ok) {
      const data = await res.json()
      setInstance(data)
      await scenarioHub.joinScenario(data.instanceId)
    }
  }

  const submitFlag = async () => {
    if (!instance || !flag.trim()) return
    setSubmitting(true)
    setFlagResult(null)
    try {
      const res = await fetch(
        `/api/v1/scenarios/instances/${instance.instanceId}/stages/${instance.currentStageId}/submit`,
        { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ flag }) }
      )
      const data = await res.json()
      if (data.correct) {
        setFlagResult('correct')
        setFlag('')
        if (data.nextStageUnlocked) {
          setInstance({ ...instance, currentStageId: data.nextStageUnlocked.stageId })
        }
        if (data.allCompleted) setShowCompletion(true)
        await loadStatus(instance.instanceId)
      } else {
        setFlagResult('incorrect')
      }
    } finally {
      setSubmitting(false)
    }
  }

  if (!instance) {
    return <TimeSlotPicker scenarioId={scenarioId} onReserved={handleReserved} />
  }

  const currentStage = instance.stages.find((stage) => stage.id === instance.currentStageId)
  const completedCount = instance.stages.filter((stage) => stage.status === 'completed').length

  return (
    <Stack maw={900} mx="auto" p="md" gap="lg">
      <Group justify="space-between" align="flex-start">
        <Stack gap={2}>
          <Title order={2}>场景挑战</Title>
          <Text size="sm" className="yy-readable-text">
            沿阶段推进完成验证，综合提交会进入后续评分。
          </Text>
        </Stack>
        <Badge size="lg" data-testid="time-remaining" className="yy-status-badge">
          {instance.timeRemaining}
        </Badge>
      </Group>

      <YinyuPanel p="md">
        <Timeline active={Math.max(0, completedCount - 1)}>
          {instance.stages.map((stage) => (
            <Timeline.Item
              key={stage.id}
              title={stage.title}
              bullet={<Text size="xs">{stage.orderIndex}</Text>}
              data-testid={`stage-status-${stage.id}`}
              data-status={stage.status}
            >
              <Text size="sm" className="yy-readable-text">
                {stage.skillDescription}
              </Text>
              <Badge color={stageStatusColor(stage.status)} variant="light" className="yy-status-badge">
                {stageStatusLabel(stage.status)}
              </Badge>
            </Timeline.Item>
          ))}
        </Timeline>
      </YinyuPanel>

      {currentStage && (
        <YinyuPanel p="lg" className="admin-panel">
          <Text fw={700} size="lg" data-testid="current-stage-title">
            {currentStage.title}
          </Text>
          <Text mt="xs" className="yy-readable-text">
            {currentStage.skillDescription}
          </Text>

          <Tabs defaultValue="flag" mt="lg">
            <Tabs.List>
              <Tabs.Tab value="flag">提交 Flag</Tabs.Tab>
              <Tabs.Tab value="submissions">综合提交</Tabs.Tab>
            </Tabs.List>

            <Tabs.Panel value="flag" pt="md">
              <Group>
                <TextInput
                  data-testid="flag-input"
                  placeholder="输入 Flag..."
                  value={flag}
                  onChange={(event) => setFlag(event.currentTarget.value)}
                  style={{ flex: 1 }}
                  onKeyDown={(event) => event.key === 'Enter' && submitFlag()}
                />
                <Button data-testid="submit-flag" loading={submitting} onClick={submitFlag}>
                  提交
                </Button>
              </Group>
              {flagResult === 'correct' && (
                <Alert color="green" mt="sm" data-testid="flag-correct">
                  Flag 正确，阶段已完成。
                </Alert>
              )}
              {flagResult === 'incorrect' && (
                <Alert color="red" mt="sm" data-testid="flag-incorrect">
                  Flag 错误，请重试。
                </Alert>
              )}
            </Tabs.Panel>

            <Tabs.Panel value="submissions" pt="md">
              <MultiTypeSubmission challengeId={scenarioId} instanceId={instance.instanceId} />
            </Tabs.Panel>
          </Tabs>
        </YinyuPanel>
      )}

      <Modal
        opened={showCompletion}
        onClose={() => setShowCompletion(false)}
        title="挑战完成"
        size="lg"
        data-testid="completion-summary"
      >
        <YinyuModalBody p="md">
          <Alert color="green" mb="md" data-testid="completed-all-stages">
            恭喜，你已完成所有阶段。
          </Alert>
          <Text fw={700} data-testid="total-score">
            总得分：{instance.totalScore}
          </Text>
          <Text mt="md" size="sm" className="yy-readable-text">
            你的解题报告将计入综合评分，管理员评审后会更新最终得分。
          </Text>
        </YinyuModalBody>
      </Modal>
    </Stack>
  )
}
