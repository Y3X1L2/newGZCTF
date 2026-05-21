import { useEffect, useState } from 'react'
import { useParams, useNavigate } from 'react-router'
import { Button, Card, Group, Stack, Text, TextInput, Alert, ActionIcon } from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { AdminPage } from '@Components/admin/AdminPage'

interface FlagInfo {
  id: number
  flag: string
  attachment?: { name: string }
}

export default function FlagsEdit() {
  const { id: gameId, challengeId } = useParams<{ id: string; challengeId: string }>()
  const navigate = useNavigate()
  const [flags, setFlags] = useState<FlagInfo[]>([])
  const [loading, setLoading] = useState(true)
  const [newFlag, setNewFlag] = useState('')

  const load = async () => {
    setLoading(true)
    try {
      const res = await fetch(`/api/edit/Games/${gameId}/Challenges`)
      if (res.ok) {
        const data = await res.json()
        const c = data.find((c: { id: number }) => c.id === Number(challengeId))
        if (c?.flags) setFlags(c.flags)
      }
    } finally { setLoading(false) }
  }

  useEffect(() => { load() }, [])

  const handleAddFlag = async () => {
    if (!newFlag.trim()) return
    try {
      const res = await fetch(`/api/edit/Games/${gameId}/Challenges/${challengeId}/Flags`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify([{ flag: newFlag }]),
      })
      if (res.ok) {
        notifications.show({ title: 'Flag 已添加', message: '新 Flag 已成功添加', color: 'green' })
        setNewFlag('')
        load()
      } else {
        notifications.show({ title: '添加失败', message: '请检查 Flag 格式', color: 'red' })
      }
    } catch { /* ignore */ }
  }

  return (
    <AdminPage head={
      <Group>
        <Button variant="default" onClick={() => navigate(`/admin/games/${gameId}/challenges/${challengeId}`)}>
          ← 返回题目编辑
        </Button>
        <Button variant="default" onClick={() => navigate(`/admin/games/${gameId}/challenges`)}>
          返回题目列表
        </Button>
      </Group>
    } isLoading={loading}>
      <Card shadow="sm" padding="md" withBorder>
        <Text fw={700} mb="md">Flag 管理</Text>
        <Group mb="md">
          <TextInput placeholder="输入新 Flag..." value={newFlag}
            onChange={e => setNewFlag(e.currentTarget.value)} style={{ flex: 1 }} />
          <Button onClick={handleAddFlag}>添加 Flag</Button>
        </Group>
        <Stack>
          {flags.length === 0 && <Text c="dimmed">暂无 Flag，请添加</Text>}
          {flags.map((f) => (
            <Alert key={f.id} color="green" py="xs">
              <Text size="sm" ff="monospace">{f.flag}</Text>
            </Alert>
          ))}
        </Stack>
      </Card>
    </AdminPage>
  )
}
