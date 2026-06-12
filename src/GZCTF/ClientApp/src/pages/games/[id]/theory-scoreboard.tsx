import { Badge, Group, Stack, Table, Text, Title } from '@mantine/core'
import dayjs from 'dayjs'
import { FC, useEffect, useState } from 'react'
import { useParams } from 'react-router'
import { Empty } from '@Components/Empty'
import { WithGameTab } from '@Components/WithGameTab'
import { WithNavBar } from '@Components/WithNavbar'
import { YinyuStatePage, YinyuTableShell } from '@Components/yinyu/YinyuUI'
import { showErrorMsg } from '@Utils/Shared'
import { theoryPlayerApi, TheoryScoreboardItemModel } from '../../../Api/TheoryApi'

const TheoryScoreboard: FC = () => {
  const { id } = useParams()
  const numId = parseInt(id ?? '-1')
  const [items, setItems] = useState<TheoryScoreboardItemModel[]>()
  const [loading, setLoading] = useState(false)

  const fetchScoreboard = async () => {
    if (numId < 0) return
    setLoading(true)
    try {
      const res = await theoryPlayerApi.getScoreboard(numId)
      setItems(res.data ?? [])
    } catch (err) {
      showErrorMsg(err, (key) => key)
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    fetchScoreboard()
    const interval = window.setInterval(fetchScoreboard, 30000)
    return () => window.clearInterval(interval)
  }, [numId])

  return (
    <WithNavBar minWidth={0} width="min(100%, calc(100vw - 7.25rem))">
      <WithGameTab>
        {loading && !items ? (
          <YinyuStatePage tone="neutral" p="xl" className="yy-theory-loading">
            <Stack gap="xs">
              <Badge variant="light">Theory</Badge>
              <Title order={2}>理论榜单加载中</Title>
              <Text className="yy-readable-text">正在读取队伍得分与最高分成员记录。</Text>
            </Stack>
          </YinyuStatePage>
        ) : null}
        <YinyuTableShell p="md" className="admin-panel large yy-theory-scoreboard">
          <Stack gap="sm">
            <Group justify="space-between" className="yy-theory-scoreboard-head">
              <Title order={3}>理论排行榜</Title>
              <Text size="sm" className="yy-readable-text">
                队伍成绩取队内成员最高分
              </Text>
            </Group>
            {items?.length ? (
              <Table>
                <Table.Thead>
                  <Table.Tr>
                    <Table.Th>排名</Table.Th>
                    <Table.Th>队伍</Table.Th>
                    <Table.Th>分数</Table.Th>
                    <Table.Th>最高分成员</Table.Th>
                    <Table.Th>提交时间</Table.Th>
                  </Table.Tr>
                </Table.Thead>
                <Table.Tbody>
                  {items.map((item) => (
                    <Table.Tr key={item.teamId}>
                      <Table.Td>
                        <Badge variant="light">{item.rank}</Badge>
                      </Table.Td>
                      <Table.Td>{item.teamName}</Table.Td>
                      <Table.Td fw="bold">
                        {item.score} / {item.maxScore}
                      </Table.Td>
                      <Table.Td>{item.userName ?? '-'}</Table.Td>
                      <Table.Td>
                        {item.submittedAt ? dayjs(item.submittedAt).format('YYYY-MM-DD HH:mm:ss') : '-'}
                      </Table.Td>
                    </Table.Tr>
                  ))}
                </Table.Tbody>
              </Table>
            ) : (
              <Empty description="暂无理论考试成绩。" />
            )}
          </Stack>
        </YinyuTableShell>
      </WithGameTab>
    </WithNavBar>
  )
}

export default TheoryScoreboard
