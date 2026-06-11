import { Table, Text, Badge } from '@mantine/core'
import { useState, useEffect } from 'react'
import { scenarioHub, LeaderboardEntry } from '../../services/scenarioHub'

interface LeaderboardProps {
  challengeId: number
}

export default function Leaderboard({ challengeId }: LeaderboardProps) {
  const [entries, setEntries] = useState<LeaderboardEntry[]>([])
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    fetch(`/api/v1/scenarios/${challengeId}/leaderboard`)
      .then((r) => r.json())
      .then((data) => {
        setEntries(data.entries ?? data)
        setLoading(false)
      })
      .catch(() => setLoading(false))

    scenarioHub.onLeaderboardUpdated((payload) => {
      if (payload.challengeId === challengeId) setEntries(payload.entries)
    })
    return () => {
      scenarioHub.offLeaderboardUpdated(() => {})
    }
  }, [challengeId])

  if (loading) return <Text>加载排行榜...</Text>

  return (
    <Table striped highlightOnHover data-testid="leaderboard-table">
      <Table.Thead>
        <Table.Tr>
          <Table.Th>排名</Table.Th>
          <Table.Th>队伍</Table.Th>
          <Table.Th>总分</Table.Th>
          <Table.Th data-testid="col-score-Flag">Flag</Table.Th>
          <Table.Th data-testid="col-score-Writeup">Writeup</Table.Th>
          <Table.Th data-testid="col-score-IP">IP</Table.Th>
        </Table.Tr>
      </Table.Thead>
      <Table.Tbody>
        {entries.map((entry, i) => (
          <Table.Tr key={entry.userId} data-testid={`leaderboard-row-${i}`}>
            <Table.Td>
              <Badge color={entry.rank <= 3 ? 'gold' : 'gray'} data-testid="rank">
                {entry.rank}
              </Badge>
            </Table.Td>
            <Table.Td>{entry.userName}</Table.Td>
            <Table.Td>
              <Text fw={700}>{entry.totalScore}</Text>
            </Table.Td>
            <Table.Td>{entry.detailScores?.Flag ?? '-'}</Table.Td>
            <Table.Td>{entry.detailScores?.Writeup ?? '-'}</Table.Td>
            <Table.Td>{entry.detailScores?.IP ?? '-'}</Table.Td>
          </Table.Tr>
        ))}
      </Table.Tbody>
    </Table>
  )
}
