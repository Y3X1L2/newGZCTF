import { Badge, Button, Group, Modal, NumberInput, ScrollArea, Stack, Table, Text, Textarea, Title } from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { useEffect, useState } from 'react'
import { Empty } from '@Components/Empty'
import { WithNavBar } from '@Components/WithNavbar'
import { WithRole } from '@Components/WithRole'
import { YinyuModalBody, YinyuPanel, YinyuTableShell } from '@Components/yinyu/YinyuUI'
import { Role } from '@Api'

interface PendingSubmission {
  id: string
  userId: string
  userName: string
  challengeTitle: string
  submissionType: string
  content: { text?: string; format?: string }
  submittedAt: string
}

export default function SubmissionReview() {
  const [submissions, setSubmissions] = useState<PendingSubmission[]>([])
  const [loading, setLoading] = useState(true)
  const [selected, setSelected] = useState<PendingSubmission | null>(null)
  const [score, setScore] = useState<number>(5)
  const [comment, setComment] = useState('')
  const [reviewing, setReviewing] = useState(false)

  const loadPending = async () => {
    setLoading(true)
    try {
      const res = await fetch('/api/v1/submissions/pending-review?submissionType=Writeup')
      if (res.ok) {
        const data = await res.json()
        setSubmissions(data.items ?? data)
      }
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    loadPending()
  }, [])

  const handleReview = async () => {
    if (!selected) return

    setReviewing(true)
    try {
      await fetch(`/api/v1/submissions/${selected.id}/review`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ score, maxScore: 10, comment }),
      })
      notifications.show({ title: '评审完成', message: '评分已提交', color: 'green' })
      setSubmissions((items) => items.filter((item) => item.id !== selected.id))
      setSelected(null)
      setScore(5)
      setComment('')
    } catch {
      notifications.show({ title: '评审失败', message: '请重试', color: 'red' })
    } finally {
      setReviewing(false)
    }
  }

  return (
    <WithNavBar width="90%" minWidth={960}>
      <WithRole requiredRole={Role.Admin}>
        <Stack gap="lg" w="100%" py="md">
          <Stack gap={2}>
            <Title order={2}>人工评审</Title>
            <Text size="sm" className="yy-readable-text">
              审核 Writeup 等人工评分提交，并写入最终评分。
            </Text>
          </Stack>

          <YinyuTableShell p="xs">
            <ScrollArea offsetScrollbars scrollbarSize={4}>
              <Table striped highlightOnHover data-testid="pending-reviews" miw={820}>
                <Table.Thead>
                  <Table.Tr>
                    <Table.Th>选手</Table.Th>
                    <Table.Th>题目</Table.Th>
                    <Table.Th>类型</Table.Th>
                    <Table.Th>提交时间</Table.Th>
                    <Table.Th>操作</Table.Th>
                  </Table.Tr>
                </Table.Thead>
                <Table.Tbody>
                  {submissions.map((submission) => (
                    <Table.Tr key={submission.id} data-testid="review-item">
                      <Table.Td>{submission.userName}</Table.Td>
                      <Table.Td>{submission.challengeTitle}</Table.Td>
                      <Table.Td>
                        <Badge className="yy-status-badge">{submission.submissionType}</Badge>
                      </Table.Td>
                      <Table.Td>{new Date(submission.submittedAt).toLocaleString('zh-CN')}</Table.Td>
                      <Table.Td>
                        <Button size="xs" onClick={() => setSelected(submission)}>
                          评审
                        </Button>
                      </Table.Td>
                    </Table.Tr>
                  ))}
                </Table.Tbody>
              </Table>
            </ScrollArea>
            {!loading && !submissions.length && <Empty description="暂无待评审提交" />}
            {loading && (
              <Text className="yy-readable-text" ta="center" py="xl">
                正在加载待评审列表...
              </Text>
            )}
          </YinyuTableShell>
        </Stack>

        <Modal opened={!!selected} onClose={() => setSelected(null)} title="评审提交" size="lg">
          {selected && (
            <YinyuModalBody p="md">
              <Stack gap="md">
                <Text fw={700}>
                  选手: {selected.userName} | 题目: {selected.challengeTitle}
                </Text>
                <YinyuPanel data-testid="submission-content" p="md" mah={400} style={{ overflow: 'auto' }}>
                  {selected.content?.text ? (
                    <div dangerouslySetInnerHTML={{ __html: selected.content.text }} />
                  ) : (
                    <Text className="yy-readable-text">(文件提交)</Text>
                  )}
                </YinyuPanel>
                <NumberInput
                  data-testid="review-score"
                  label="评分 (1-10)"
                  value={score}
                  min={1}
                  max={10}
                  onChange={(value) => setScore(Number(value) || 1)}
                />
                <Textarea
                  data-testid="review-comment"
                  label="评审意见"
                  value={comment}
                  onChange={(event) => setComment(event.currentTarget.value)}
                />
                <Group justify="flex-end">
                  <Button variant="default" onClick={() => setSelected(null)}>
                    取消
                  </Button>
                  <Button data-testid="submit-review" loading={reviewing} onClick={handleReview}>
                    提交评审
                  </Button>
                </Group>
              </Stack>
            </YinyuModalBody>
          )}
        </Modal>
      </WithRole>
    </WithNavBar>
  )
}
