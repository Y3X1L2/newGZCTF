import { useState, useEffect } from 'react';
import { Table, Button, Modal, Text, Textarea, NumberInput, Group, Badge } from '@mantine/core';
import { notifications } from '@mantine/notifications';
import MarkdownRenderer from '../../components/MarkdownRenderer';

interface PendingSubmission {
  id: string;
  userId: string;
  userName: string;
  challengeTitle: string;
  submissionType: string;
  content: { text?: string; format?: string };
  submittedAt: string;
}

export default function SubmissionReview() {
  const [submissions, setSubmissions] = useState<PendingSubmission[]>([]);
  const [loading, setLoading] = useState(true);
  const [selected, setSelected] = useState<PendingSubmission | null>(null);
  const [score, setScore] = useState<number>(5);
  const [comment, setComment] = useState('');
  const [reviewing, setReviewing] = useState(false);

  const loadPending = async () => {
    setLoading(true);
    try {
      const res = await fetch('/api/v1/submissions/pending-review?submissionType=Writeup');
      if (res.ok) {
        const data = await res.json();
        setSubmissions(data.items ?? data);
      }
    } finally { setLoading(false); }
  };

  useEffect(() => { loadPending(); }, []);

  const handleReview = async () => {
    if (!selected) return;
    setReviewing(true);
    try {
      await fetch(`/api/v1/submissions/${selected.id}/review`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ accepted: true, score, comment }),
      });
      notifications.show({ title: '评审完成', message: '评分已提交', color: 'green' });
      setSubmissions(submissions.filter(s => s.id !== selected.id));
      setSelected(null);
      setScore(5);
      setComment('');
    } catch {
      notifications.show({ title: '评审失败', message: '请重试', color: 'red' });
    } finally { setReviewing(false); }
  };

  if (loading) return <Text>加载待评审列表...</Text>;

  return (
    <div style={{ maxWidth: 1000, margin: '0 auto', padding: '1rem' }}>
      <h2>人工评审</h2>
      <Table striped highlightOnHover data-testid="pending-reviews">
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
          {submissions.map(sub => (
            <Table.Tr key={sub.id} data-testid="review-item">
              <Table.Td>{sub.userName}</Table.Td>
              <Table.Td>{sub.challengeTitle}</Table.Td>
              <Table.Td><Badge>{sub.submissionType}</Badge></Table.Td>
              <Table.Td>{new Date(sub.submittedAt).toLocaleString('zh-CN')}</Table.Td>
              <Table.Td>
                <Button size="xs" onClick={() => setSelected(sub)}>评审</Button>
              </Table.Td>
            </Table.Tr>
          ))}
        </Table.Tbody>
      </Table>

      <Modal opened={!!selected} onClose={() => setSelected(null)} title="评审提交" size="lg">
        {selected && (
          <div>
            <Text fw={500} mb="sm">选手: {selected.userName} | 题目: {selected.challengeTitle}</Text>
            <div data-testid="submission-content"
              style={{ background: '#f8f9fa', padding: '1rem', borderRadius: '4px', maxHeight: 400, overflow: 'auto' }}>
              {selected.content?.text ? (
                <MarkdownRenderer content={selected.content.text ?? ''} />
              ) : (
                <Text c="dimmed">(文件提交)</Text>
              )}
            </div>
            <NumberInput data-testid="review-score" label="评分 (1-10)" mt="md"
              value={score} min={1} max={10} onChange={v => setScore(Number(v) || 1)} />
            <Textarea data-testid="review-comment" label="评审意见" mt="sm"
              value={comment} onChange={e => setComment(e.currentTarget.value)} />
            <Group justify="flex-end" mt="md">
              <Button variant="default" onClick={() => setSelected(null)}>取消</Button>
              <Button data-testid="submit-review" loading={reviewing} onClick={handleReview}>提交评审</Button>
            </Group>
          </div>
        )}
      </Modal>
    </div>
  );
}
