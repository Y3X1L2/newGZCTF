import { Card, Button, Badge, Group, Text, Alert, Stack, Title } from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { useState, useEffect } from 'react'
import gameClasses from '../../styles/components/GameCard.module.css'

interface TimeSlot {
  id: number
  startTime: string
  endTime: string
  maxParticipants: number
  currentParticipants: number
}

interface TimeSlotPickerProps {
  scenarioId: number
  onReserved: (slot: TimeSlot) => void
}

export default function TimeSlotPicker({ scenarioId, onReserved }: TimeSlotPickerProps) {
  const [slots, setSlots] = useState<TimeSlot[]>([])
  const [loading, setLoading] = useState(true)
  const [reserving, setReserving] = useState<number | null>(null)

  useEffect(() => {
    fetch(`/api/v1/scenarios/${scenarioId}/timeslots`)
      .then((r) => r.json())
      .then((data) => {
        setSlots(data.items ?? data)
        setLoading(false)
      })
      .catch(() => {
        setLoading(false)
      })
  }, [scenarioId])

  const handleReserve = async (slotId: number) => {
    setReserving(slotId)
    try {
      const res = await fetch(`/api/v1/scenarios/${scenarioId}/timeslots/${slotId}/reserve`, { method: 'POST' })
      if (!res.ok) throw new Error('预约失败')
      notifications.show({ title: '预约成功', message: '环境将在预约时间自动启动', color: 'green' })
      const slot = slots.find((s) => s.id === slotId)
      if (slot) onReserved(slot)
    } catch {
      notifications.show({ title: '预约失败', message: '该时段可能已满', color: 'red' })
    } finally {
      setReserving(null)
    }
  }

  if (loading) return <Text>加载时间段...</Text>
  if (slots.length === 0) return <Alert color="yellow">当前没有可用时间段</Alert>

  return (
    <Stack data-testid="timeslot-picker" gap="sm">
      <Stack gap={2}>
        <Title order={3}>选择参与时间段</Title>
        <Text size="sm" c="dimmed">
          预约后环境会在对应时间段内启动。
        </Text>
      </Stack>
      {slots.map((slot) => {
        const isFull = slot.currentParticipants >= slot.maxParticipants
        return (
          <Card
            key={slot.id}
            shadow="sm"
            padding="md"
            mt="sm"
            withBorder
            className={gameClasses.sidePanel}
            data-testid={isFull ? 'timeslot-full' : 'timeslot-available'}
          >
            <Group justify="space-between">
              <div>
                <Text fw={500}>
                  {new Date(slot.startTime).toLocaleString('zh-CN')} - {new Date(slot.endTime).toLocaleString('zh-CN')}
                </Text>
                <Text size="sm" c="dimmed">
                  {slot.currentParticipants}/{slot.maxParticipants} 人已预约
                </Text>
              </div>
              <Badge color={isFull ? 'red' : 'green'}>
                {isFull ? '已满' : `${slot.maxParticipants - slot.currentParticipants} 个名额`}
              </Badge>
              <Button
                data-testid={isFull ? undefined : 'reserve-slot'}
                disabled={isFull}
                loading={reserving === slot.id}
                onClick={() => handleReserve(slot.id)}
                size="xs"
              >
                预约
              </Button>
            </Group>
          </Card>
        )
      })}
    </Stack>
  )
}
