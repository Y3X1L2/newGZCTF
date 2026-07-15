import { Badge, Code, Divider, Drawer, Group, ScrollArea, SimpleGrid, Stack, Text } from '@mantine/core'
import dayjs from 'dayjs'
import useSWR from 'swr'
import { fetchCorrelationSummary } from './api'
import { OperationalEventItem } from './types'

function value(value: unknown) {
  return value === null || value === undefined || value === '' ? '-' : String(value)
}

function Field({ label, children }: { label: string; children: unknown }) {
  return (
    <Stack gap={2}>
      <Text size="xs" c="dimmed">{label}</Text>
      <Text size="sm" ff="monospace" style={{ overflowWrap: 'anywhere' }}>{value(children)}</Text>
    </Stack>
  )
}

export function OperationalEventDetailDrawer({
  item,
  onClose,
}: {
  item: OperationalEventItem | null
  onClose: () => void
}) {
  const correlationUrl = item
    ? `/api/admin/operations/correlations/${encodeURIComponent(item.event.correlationId)}`
    : null
  const { data: summary } = useSWR(correlationUrl, fetchCorrelationSummary)
  const event = item?.event

  return (
    <Drawer opened={Boolean(item)} onClose={onClose} title="事件详情" position="right" size="lg">
      {item && event ? (
        <ScrollArea h="calc(100vh - 90px)" offsetScrollbars scrollbarSize={4}>
          <Stack gap="md" pr="sm">
            <Group justify="space-between" align="flex-start">
              <Stack gap={3}>
                <Text fw={800}>{event.eventCode}</Text>
                <Text size="sm">{event.message}</Text>
              </Stack>
              <Badge variant="light">{String(event.outcome)}</Badge>
            </Group>

            <SimpleGrid cols={2} spacing="sm">
              <Field label="时间">{dayjs(event.occurredAt).format('YYYY-MM-DD HH:mm:ss.SSS')}</Field>
              <Field label="领域">{item.domain}</Field>
              <Field label="Correlation">{event.correlationId}</Field>
              <Field label="Trace">{event.traceId}</Field>
              <Field label="节点">{item.labels.workerNode ?? event.workerNodeId}</Field>
              <Field label="部署任务">{item.labels.deploymentTicket ?? event.deploymentTicketId}</Field>
              <Field label="主体">{item.labels.subject ?? event.subjectId}</Field>
              <Field label="资源">{item.labels.resource ?? event.resourceId}</Field>
              <Field label="错误分类">{event.errorCategory}</Field>
              <Field label="错误码">{event.errorCode}</Field>
            </SimpleGrid>

            {summary ? (
              <>
                <Divider label="关联链路" />
                <SimpleGrid cols={2} spacing="sm">
                  <Field label="事件数">{summary.eventCount}</Field>
                  <Field label="最终结果">{summary.outcome}</Field>
                  <Field label="开始">{dayjs(summary.startedAt).format('YYYY-MM-DD HH:mm:ss')}</Field>
                  <Field label="结束">{dayjs(summary.completedAt).format('YYYY-MM-DD HH:mm:ss')}</Field>
                  <Field label="涉及领域">{summary.domains.join(', ')}</Field>
                  <Field label="涉及节点">{summary.workerNodes.join(', ')}</Field>
                </SimpleGrid>
              </>
            ) : null}

            {event.detail && Object.keys(event.detail).length > 0 ? (
              <>
                <Divider label="结构化明细" />
                <Code block>{JSON.stringify(event.detail, null, 2)}</Code>
              </>
            ) : null}
          </Stack>
        </ScrollArea>
      ) : null}
    </Drawer>
  )
}
