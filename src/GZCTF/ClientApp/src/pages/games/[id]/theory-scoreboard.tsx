import { Badge, Card, Group, Stack, Table, Text, Title } from '@mantine/core'
import dayjs from 'dayjs'
import { FC, useEffect, useState } from 'react'
import { useParams } from 'react-router'
import { Empty } from '@Components/Empty'
import { WithGameTab } from '@Components/WithGameTab'
import { WithNavBar } from '@Components/WithNavbar'
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
    <WithNavBar minWidth={0} isLoading={loading || !items} withFooter>
      <WithGameTab>
        <Card withBorder radius="sm">
          <Stack gap="sm">
            <Group justify="space-between">
              <Title order={3}>理论排行榜</Title>
              <Text size="sm" c="dimmed">
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
                      <Table.Td>{item.submittedAt ? dayjs(item.submittedAt).format('YYYY-MM-DD HH:mm:ss') : '-'}</Table.Td>
                    </Table.Tr>
                  ))}
                </Table.Tbody>
              </Table>
            ) : (
              <Empty description="暂无理论考试成绩。" />
            )}
          </Stack>
        </Card>
      </WithGameTab>
    </WithNavBar>
  )
}

export default TheoryScoreboard
