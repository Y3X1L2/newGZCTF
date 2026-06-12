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

interface ScenarioSummary {
  id: number
  title: string
  gameId: number
  gameTitle: string
  stageCount: number
  status: string
  isEnabled: boolean
  createdAt: string
}

const readList = (data: unknown): ScenarioSummary[] => {
  if (Array.isArray(data)) return data as ScenarioSummary[]
  const source = data as { data?: ScenarioSummary[]; items?: ScenarioSummary[] }
  return source.data ?? source.items ?? []
}

export default function ScenarioList() {
  const navigate = useNavigate()
  const [scenarios, setScenarios] = useState<ScenarioSummary[]>([])
  const [loading, setLoading] = useState(true)
  const [search, setSearch] = useState('')
  const [deleteTarget, setDeleteTarget] = useState<ScenarioSummary | null>(null)
  const [deleting, setDeleting] = useState(false)

  const loadScenarios = useCallback(async () => {
    setLoading(true)
    try {
      const res = await fetch(`/api/v1/scenarios?search=${encodeURIComponent(search)}`)
      if (res.ok) setScenarios(readList(await res.json()))
    } finally {
      setLoading(false)
    }
  }, [search])

  useEffect(() => {
    loadScenarios()
  }, [loadScenarios])

  const stats = useMemo(
    () => ({
      total: scenarios.length,
      enabled: scenarios.filter((item) => item.isEnabled).length,
      draft: scenarios.filter((item) => !item.isEnabled).length,
    }),
    [scenarios]
  )

  const handleDelete = async () => {
    if (!deleteTarget) return

    setDeleting(true)
    try {
      const res = await fetch(`/api/v1/scenarios/${deleteTarget.id}`, { method: 'DELETE' })
      if (!res.ok) throw new Error('Delete failed')

      notifications.show({ title: '已删除', message: `场景「${deleteTarget.title}」已删除`, color: 'green' })
      setScenarios((items) => items.filter((item) => item.id !== deleteTarget.id))
      setDeleteTarget(null)
    } catch {
      notifications.show({ title: '删除失败', message: '请确认场景处于草稿状态后重试', color: 'red' })
    } finally {
      setDeleting(false)
    }
  }

  return (
    <AdminPage isLoading={loading && !scenarios.length}>
      <Stack gap="lg" w="100%">
        <Group justify="space-between" align="flex-start">
          <Stack gap={2}>
            <Title order={2}>场景管理</Title>
            <Text size="sm" className="yy-readable-text">
              管理多阶段场景、评分权重与环境模板绑定。
            </Text>
          </Stack>
          <Group wrap="nowrap" style={{ overflowX: 'auto' }}>
            <Button variant="default" leftSection={<Icon path={mdiRefresh} size={0.82} />} onClick={loadScenarios}>
              刷新
            </Button>
            <Button leftSection={<Icon path={mdiPlus} size={0.82} />} onClick={() => navigate('/admin/scenarios/new')}>
              创建新场景
            </Button>
          </Group>
        </Group>

        <Group grow>
          <YinyuMetricTile label="场景总数" value={stats.total} tone="neutral" />
          <YinyuMetricTile label="已发布" value={stats.enabled} tone="success" />
          <YinyuMetricTile label="草稿" value={stats.draft} tone="warm" />
        </Group>

        <YinyuPanel p="md">
          <TextInput
            leftSection={<Icon path={mdiMagnify} size={0.76} />}
            placeholder="搜索场景标题、赛事或状态"
            value={search}
            onChange={(event) => setSearch(event.currentTarget.value)}
          />
        </YinyuPanel>

        <YinyuTableShell p="xs" w="100%">
          <ScrollArea offsetScrollbars scrollbarSize={4}>
            <Table className={tableClasses.table} striped highlightOnHover miw={860}>
              <Table.Thead>
                <Table.Tr>
                  <Table.Th>ID</Table.Th>
                  <Table.Th>标题</Table.Th>
                  <Table.Th>赛事</Table.Th>
                  <Table.Th>阶段数</Table.Th>
                  <Table.Th>状态</Table.Th>
                  <Table.Th>创建时间</Table.Th>
                  <Table.Th />
                </Table.Tr>
              </Table.Thead>
              <Table.Tbody>
                {scenarios.map((scenario) => (
                  <Table.Tr key={scenario.id}>
                    <Table.Td>{scenario.id}</Table.Td>
                    <Table.Td>
                      <Text fw={700} lineClamp={1}>
                        {scenario.title}
                      </Text>
                    </Table.Td>
                    <Table.Td>{scenario.gameTitle ?? `赛事 #${scenario.gameId}`}</Table.Td>
                    <Table.Td>{scenario.stageCount}</Table.Td>
                    <Table.Td>
                      <Badge
                        color={scenario.isEnabled ? 'green' : 'yellow'}
                        variant="light"
                        className="yy-status-badge"
                      >
                        {scenario.isEnabled ? '已发布' : scenario.status}
                      </Badge>
                    </Table.Td>
                    <Table.Td>{new Date(scenario.createdAt).toLocaleDateString('zh-CN')}</Table.Td>
                    <Table.Td>
                      <Group gap="xs" justify="right" wrap="nowrap">
                        <Tooltip label="编辑">
                          <ActionIcon onClick={() => navigate(`/admin/scenarios/${scenario.id}/edit`)}>
                            <Icon path={mdiPencilOutline} size={0.86} />
                          </ActionIcon>
                        </Tooltip>
                        {!scenario.isEnabled && (
                          <Tooltip label="删除">
                            <ActionIcon color="red" variant="subtle" onClick={() => setDeleteTarget(scenario)}>
                              <Icon path={mdiDeleteOutline} size={0.86} />
                            </ActionIcon>
                          </Tooltip>
                        )}
                      </Group>
                    </Table.Td>
                  </Table.Tr>
                ))}
              </Table.Tbody>
            </Table>
          </ScrollArea>
          {!scenarios.length && !loading && <Empty description="当前没有匹配的场景" />}
        </YinyuTableShell>
      </Stack>

      <Modal opened={!!deleteTarget} onClose={() => setDeleteTarget(null)} title="确认删除">
        <YinyuModalBody p="md">
          <Text className="yy-readable-text">确定要删除场景「{deleteTarget?.title}」吗？此操作不可撤销。</Text>
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
