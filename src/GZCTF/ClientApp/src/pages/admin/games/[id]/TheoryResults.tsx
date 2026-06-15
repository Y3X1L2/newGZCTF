import { Badge, Button, Group, Stack, Table, Text, Title } from '@mantine/core'
import { showNotification } from '@mantine/notifications'
import { mdiCheck, mdiRefresh } from '@mdi/js'
import { Icon } from '@mdi/react'
import dayjs from 'dayjs'
import { FC, useEffect, useState } from 'react'
import { useParams } from 'react-router'
import { Empty } from '@Components/Empty'
import { WithGameEditTab } from '@Components/admin/WithGameEditTab'
import { YinyuTableShell } from '@Components/yinyu/YinyuUI'
import { showErrorMsg } from '@Utils/Shared'
import { theoryAdminApi, TheoryAnswerSheetStatus, TheoryResultsModel } from '../../../../Api/TheoryApi'

const statusBadge = (status: TheoryAnswerSheetStatus) =>
  status === TheoryAnswerSheetStatus.Submitted ? (
    <Badge color="teal" className="yy-semantic-badge" data-semantic="success">
      已提交
    </Badge>
  ) : (
    <Badge color="violet" className="yy-semantic-badge" data-semantic="pending">
      草稿
    </Badge>
  )

const TheoryResults: FC = () => {
  const { id } = useParams()
  const numId = parseInt(id ?? '-1')
  const [results, setResults] = useState<TheoryResultsModel>()
  const [loading, setLoading] = useState(false)

  const fetchResults = async () => {
    if (numId < 0) return
    setLoading(true)
    try {
      const res = await theoryAdminApi.getResults(numId)
      setResults(res.data)
    } catch (err) {
      showErrorMsg(err, (key) => key)
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    fetchResults()
  }, [numId])

  const recalculate = async () => {
    setLoading(true)
    try {
      const res = await theoryAdminApi.recalculateResults(numId)
      setResults(res.data)
      showNotification({ color: 'teal', message: '理论成绩已刷新', icon: <Icon path={mdiCheck} size={1} /> })
    } catch (err) {
      showErrorMsg(err, (key) => key)
    } finally {
      setLoading(false)
    }
  }

  return (
    <WithGameEditTab
      isLoading={!results || loading}
      contentPos="right"
      head={
        <>
          <Button variant="outline" leftSection={<Icon path={mdiRefresh} size={1} />} onClick={fetchResults}>
            刷新
          </Button>
          <Button leftSection={<Icon path={mdiRefresh} size={1} />} onClick={recalculate}>
            重新判分
          </Button>
        </>
      }
    >
      <Stack gap="md">
        <YinyuTableShell p="md">
          <Stack gap="sm">
            <Group justify="space-between">
              <Title order={4}>理论排行榜</Title>
              <Text size="sm" c="dimmed">
                队伍成绩取队内成员最高分
              </Text>
            </Group>
            {results?.scoreboard.length ? (
              <Table>
                <Table.Thead>
                  <Table.Tr>
                    <Table.Th>排名</Table.Th>
                    <Table.Th>队伍</Table.Th>
                    <Table.Th>最高分成员</Table.Th>
                    <Table.Th>分数</Table.Th>
                    <Table.Th>提交时间</Table.Th>
                  </Table.Tr>
                </Table.Thead>
                <Table.Tbody>
                  {results.scoreboard.map((item) => (
                    <Table.Tr key={item.teamId}>
                      <Table.Td>{item.rank}</Table.Td>
                      <Table.Td>{item.teamName}</Table.Td>
                      <Table.Td>{item.userName ?? '-'}</Table.Td>
                      <Table.Td fw="bold">
                        {item.score} / {item.maxScore}
                      </Table.Td>
                      <Table.Td>
                        {item.submittedAt ? dayjs(item.submittedAt).format('YYYY-MM-DD HH:mm:ss') : '-'}
                      </Table.Td>
                    </Table.Tr>
                  ))}
                </Table.Tbody>
              </Table>
            ) : (
              <Empty description="暂无已审核队伍或提交记录。" />
            )}
          </Stack>
        </YinyuTableShell>

        <YinyuTableShell p="md">
          <Stack gap="sm">
            <Title order={4}>个人答卷</Title>
            {results?.submissions.length ? (
              <Table>
                <Table.Thead>
                  <Table.Tr>
                    <Table.Th>队伍</Table.Th>
                    <Table.Th>用户</Table.Th>
                    <Table.Th>状态</Table.Th>
                    <Table.Th>分数</Table.Th>
                    <Table.Th>更新时间</Table.Th>
                    <Table.Th>提交时间</Table.Th>
                  </Table.Tr>
                </Table.Thead>
                <Table.Tbody>
                  {results.submissions.map((item) => (
                    <Table.Tr key={item.id}>
                      <Table.Td>{item.teamName}</Table.Td>
                      <Table.Td>{item.userName}</Table.Td>
                      <Table.Td>{statusBadge(item.status)}</Table.Td>
                      <Table.Td fw={item.status === TheoryAnswerSheetStatus.Submitted ? 700 : 400}>
                        {item.status === TheoryAnswerSheetStatus.Submitted ? `${item.score} / ${item.maxScore}` : '-'}
                      </Table.Td>
                      <Table.Td>{dayjs(item.updatedAt).format('YYYY-MM-DD HH:mm:ss')}</Table.Td>
                      <Table.Td>
                        {item.submittedAt ? dayjs(item.submittedAt).format('YYYY-MM-DD HH:mm:ss') : '-'}
                      </Table.Td>
                    </Table.Tr>
                  ))}
                </Table.Tbody>
              </Table>
            ) : (
              <Empty description="暂无个人答卷。" />
            )}
          </Stack>
        </YinyuTableShell>
      </Stack>
    </WithGameEditTab>
  )
}

export default TheoryResults
