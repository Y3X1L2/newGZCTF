import {
  ActionIcon,
  Badge,
  Button,
  Group,
  Modal,
  ScrollArea,
  Stack,
  Table,
  Text,
  TextInput,
  Title,
  Tooltip,
} from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { mdiDeleteOutline, mdiMagnify, mdiPencilOutline, mdiPlus, mdiRefresh } from '@mdi/js'
import { Icon } from '@mdi/react'
import { useCallback, useEffect, useMemo, useState } from 'react'
import { useNavigate } from 'react-router'
import { Empty } from '@Components/Empty'
import { AdminPage } from '@Components/admin/AdminPage'
import { YinyuMetricTile, YinyuModalBody, YinyuPanel, YinyuTableShell } from '@Components/yinyu/YinyuUI'
import tableClasses from '@Styles/Table.module.css'

interface IRChallengeSummary {
  id: number
  title: string
  gameId: number
  gameTitle: string
  osType: string
  checkpointCount: number
  status: string
  isEnabled: boolean
  createdAt: string
}

const readList = (data: unknown): IRChallengeSummary[] => {
  if (Array.isArray(data)) return data as IRChallengeSummary[]
  const source = data as { data?: IRChallengeSummary[]; items?: IRChallengeSummary[] }
  return source.data ?? source.items ?? []
}

export default function IRChallengeList() {
  const navigate = useNavigate()
  const [challenges, setChallenges] = useState<IRChallengeSummary[]>([])
  const [loading, setLoading] = useState(true)
  const [search, setSearch] = useState('')
  const [deleteTarget, setDeleteTarget] = useState<IRChallengeSummary | null>(null)
  const [deleting, setDeleting] = useState(false)

  const load = useCallback(async () => {
    setLoading(true)
    try {
      const res = await fetch(`/api/v1/ir-challenges?search=${encodeURIComponent(search)}`)
      if (res.ok) setChallenges(readList(await res.json()))
    } finally {
      setLoading(false)
    }
  }, [search])

  useEffect(() => {
    load()
  }, [load])

  const stats = useMemo(
    () => ({
      total: challenges.length,
      windows: challenges.filter((item) => item.osType === 'Windows').length,
      linux: challenges.filter((item) => item.osType !== 'Windows').length,
    }),
    [challenges]
  )

  const handleDelete = async () => {
    if (!deleteTarget) return

    setDeleting(true)
    try {
      const res = await fetch(`/api/v1/ir-challenges/${deleteTarget.id}`, { method: 'DELETE' })
      if (!res.ok) throw new Error('Delete failed')

      notifications.show({ title: '已删除', message: '题目已删除', color: 'green' })
      setChallenges((items) => items.filter((item) => item.id !== deleteTarget.id))
      setDeleteTarget(null)
    } catch {
      notifications.show({ title: '删除失败', message: '请检查网络后重试', color: 'red' })
    } finally {
      setDeleting(false)
    }
  }

  return (
    <AdminPage isLoading={loading && !challenges.length}>
      <Stack gap="lg" w="100%">
        <Group justify="space-between" align="flex-start">
          <Stack gap={2}>
            <Title order={2}>应急响应题目管理</Title>
            <Text size="sm" className="yy-readable-text">
              管理取证、排障与靶机访问类应急响应题目。
            </Text>
          </Stack>
          <Group wrap="nowrap" style={{ overflowX: 'auto' }}>
            <Button variant="default" leftSection={<Icon path={mdiRefresh} size={0.82} />} onClick={load}>
              刷新
            </Button>
            <Button
              leftSection={<Icon path={mdiPlus} size={0.82} />}
              onClick={() => navigate('/admin/ir-challenges/new')}
            >
              创建新题目
            </Button>
          </Group>
        </Group>

        <Group grow>
          <YinyuMetricTile label="题目总数" value={stats.total} tone="neutral" />
          <YinyuMetricTile label="Linux" value={stats.linux} tone="success" />
          <YinyuMetricTile label="Windows" value={stats.windows} tone="warm" />
        </Group>

        <YinyuPanel p="md">
          <TextInput
            leftSection={<Icon path={mdiMagnify} size={0.76} />}
            placeholder="搜索 IR 题目标题、赛事或系统"
            value={search}
            onChange={(event) => setSearch(event.currentTarget.value)}
          />
        </YinyuPanel>

        <YinyuTableShell p="xs" w="100%">
          <ScrollArea offsetScrollbars scrollbarSize={4}>
            <Table className={tableClasses.table} striped highlightOnHover miw={920}>
              <Table.Thead>
                <Table.Tr>
                  <Table.Th>ID</Table.Th>
                  <Table.Th>标题</Table.Th>
                  <Table.Th>赛事</Table.Th>
                  <Table.Th>系统</Table.Th>
                  <Table.Th>检查点</Table.Th>
                  <Table.Th>状态</Table.Th>
                  <Table.Th>创建时间</Table.Th>
                  <Table.Th />
                </Table.Tr>
              </Table.Thead>
              <Table.Tbody>
                {challenges.map((challenge) => (
                  <Table.Tr key={challenge.id}>
                    <Table.Td>{challenge.id}</Table.Td>
                    <Table.Td>
                      <Text fw={700} lineClamp={1}>
                        {challenge.title}
                      </Text>
                    </Table.Td>
                    <Table.Td>{challenge.gameTitle ?? `赛事 #${challenge.gameId}`}</Table.Td>
                    <Table.Td>
                      <Badge color={challenge.osType === 'Windows' ? 'blue' : 'green'} variant="light">
                        {challenge.osType}
                      </Badge>
                    </Table.Td>
                    <Table.Td>{challenge.checkpointCount}</Table.Td>
                    <Table.Td>
                      <Badge
                        color={challenge.isEnabled ? 'green' : 'yellow'}
                        variant="light"
                        className="yy-status-badge"
                      >
                        {challenge.isEnabled ? '已发布' : '草稿'}
                      </Badge>
                    </Table.Td>
                    <Table.Td>{new Date(challenge.createdAt).toLocaleDateString('zh-CN')}</Table.Td>
                    <Table.Td>
                      <Group gap="xs" justify="right" wrap="nowrap">
                        <Tooltip label="编辑">
                          <ActionIcon onClick={() => navigate(`/admin/ir-challenges/${challenge.id}/edit`)}>
                            <Icon path={mdiPencilOutline} size={0.86} />
                          </ActionIcon>
                        </Tooltip>
                        <Tooltip label="删除">
                          <ActionIcon color="red" variant="subtle" onClick={() => setDeleteTarget(challenge)}>
                            <Icon path={mdiDeleteOutline} size={0.86} />
                          </ActionIcon>
                        </Tooltip>
                      </Group>
                    </Table.Td>
                  </Table.Tr>
                ))}
              </Table.Tbody>
            </Table>
          </ScrollArea>
          {!challenges.length && !loading && <Empty description="当前没有匹配的应急响应题目" />}
        </YinyuTableShell>
      </Stack>

      <Modal opened={!!deleteTarget} onClose={() => setDeleteTarget(null)} title="确认删除">
        <YinyuModalBody p="md">
          <Text className="yy-readable-text">确定要删除「{deleteTarget?.title}」吗？</Text>
          <Group justify="flex-end" mt="md">
            <Button variant="default" onClick={() => setDeleteTarget(null)}>
              取消
            </Button>
            <Button color="red" loading={deleting} onClick={handleDelete}>
              确认删除
            </Button>
          </Group>
        </YinyuModalBody>
      </Modal>
    </AdminPage>
  )
}
