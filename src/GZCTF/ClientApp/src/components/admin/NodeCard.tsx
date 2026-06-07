import { Badge, Card, Divider, Group, Progress, RingProgress, Stack, Switch, Text, Tooltip } from '@mantine/core';
import dayjs from 'dayjs';
import relativeTime from 'dayjs/plugin/relativeTime';
import { ReactNode } from 'react';
import { mdiChip, mdiDatabaseOutline, mdiLan, mdiServerNetwork, mdiTimerOutline } from '@mdi/js';
import { Icon } from '@mdi/react';

dayjs.extend(relativeTime);

export interface NodeInfo {
  id: string;
  name: string;
  hostAddress: string;
  status: string | number;
  capabilities?: string | number;
  cpuLoad: number;
  memoryLoad: number;
  currentContainers: number;
  maxContainers: number;
  currentVms: number;
  maxVms: number;
  lastHeartbeat?: string | null;
  isSchedulable: boolean;
  isLocal: boolean;
  agentPort: number;
}

const statusMap: Record<string, { label: string; color: string }> = {
  '0': { label: '未知', color: 'gray' },
  unknown: { label: '未知', color: 'gray' },
  '1': { label: '在线', color: 'teal' },
  online: { label: '在线', color: 'teal' },
  '2': { label: '离线', color: 'red' },
  offline: { label: '离线', color: 'red' },
  '3': { label: '繁忙', color: 'orange' },
  busy: { label: '繁忙', color: 'orange' },
  '4': { label: '异常', color: 'red' },
  error: { label: '异常', color: 'red' },
};

function normalizeKey(value: string | number | undefined) {
  return String(value ?? '').toLowerCase();
}

function toPercent(value: number) {
  if (!Number.isFinite(value)) return 0;
  return Math.max(0, Math.min(100, value * 100));
}

function ratio(current: number, total: number) {
  if (!Number.isFinite(total) || total <= 0) return 0;
  return Math.max(0, Math.min(100, (current / total) * 100));
}

function capabilityLabels(value: string | number | undefined) {
  const key = normalizeKey(value);
  if (!key || key === '0' || key === 'none') return ['None'];
  if (key.includes('docker') || key === '1') return key.includes('kvm') || key === '3' ? ['Docker', 'KVM'] : ['Docker'];
  if (key.includes('kvm') || key === '2') return ['KVM'];
  if (key === '3') return ['Docker', 'KVM'];
  return [String(value)];
}

function MetricLine({
  label,
  current,
  total,
  color,
  valueLabel,
}: {
  label: string;
  current: number;
  total: number;
  color: string;
  valueLabel?: string;
}) {
  const value = ratio(current, total);

  return (
    <Stack gap={4}>
      <Group justify="space-between" gap="xs">
        <Text size="xs" c="dimmed">{label}</Text>
        <Text size="xs" fw={600}>{valueLabel ?? `${current}/${total}`}</Text>
      </Group>
      <Progress value={value} color={color} size="xs" radius="xs" />
    </Stack>
  );
}

export function NodeCard({
  node,
  onToggleSchedulable,
  rightSection,
}: {
  node: NodeInfo;
  onToggleSchedulable?: (id: string, val: boolean) => void;
  rightSection?: ReactNode;
}) {
  const status = statusMap[normalizeKey(node.status)] ?? { label: String(node.status ?? '未知'), color: 'gray' };
  const heartbeat = node.lastHeartbeat ? dayjs(node.lastHeartbeat) : null;
  const heartbeatText = heartbeat?.isValid() ? heartbeat.fromNow() : '无心跳';
  const cpu = toPercent(node.cpuLoad);
  const memory = toPercent(node.memoryLoad);
  const isOffline = normalizeKey(node.status) === 'offline' || normalizeKey(node.status) === '2';

  return (
    <Card shadow="sm" padding="md" radius="sm" withBorder data-testid={`node-card-${node.id}`}>
      <Stack gap="sm">
        <Group justify="space-between" align="flex-start" wrap="nowrap">
          <Stack gap={2} style={{ minWidth: 0 }}>
            <Group gap={6} wrap="nowrap">
              <Text fw={700} truncate>{node.name || node.hostAddress}</Text>
              {node.isLocal && <Badge size="sm" variant="light" color="blue">本地</Badge>}
            </Group>
            <Group gap={6} c="dimmed" wrap="nowrap">
              <Icon path={mdiLan} size={0.62} />
              <Text size="xs" truncate>{node.hostAddress}:{node.agentPort}</Text>
            </Group>
          </Stack>
          <Group gap={6} wrap="nowrap">
            <Badge color={status.color} variant="light">{status.label}</Badge>
            {rightSection}
          </Group>
        </Group>

        <Group gap="xs">
          {capabilityLabels(node.capabilities).map((item) => (
            <Badge key={item} size="sm" variant="outline" color={item === 'None' ? 'gray' : 'indigo'}>
              {item}
            </Badge>
          ))}
        </Group>

        <Group grow align="center">
          <RingProgress
            size={86}
            thickness={8}
            roundCaps
            sections={[{ value: cpu, color: cpu >= 85 ? 'red' : cpu >= 65 ? 'orange' : 'blue' }]}
            label={<Text ta="center" size="xs" fw={700}>{cpu.toFixed(0)}%</Text>}
          />
          <Stack gap={8}>
            <MetricLine
              label="内存负载"
              current={Number(memory.toFixed(0))}
              total={100}
              color={memory >= 85 ? 'red' : memory >= 65 ? 'orange' : 'grape'}
              valueLabel={`${memory.toFixed(0)}%`}
            />
            <MetricLine label="容器容量" current={node.currentContainers} total={node.maxContainers} color="cyan" />
            <MetricLine label="虚拟机容量" current={node.currentVms} total={node.maxVms} color="violet" />
          </Stack>
        </Group>

        <Divider />

        <Group justify="space-between" align="center">
          <Tooltip label={node.lastHeartbeat ? dayjs(node.lastHeartbeat).format('YYYY-MM-DD HH:mm:ss') : '没有收到过心跳'}>
            <Group gap={6} c={isOffline ? 'red' : 'dimmed'}>
              <Icon path={mdiTimerOutline} size={0.65} />
              <Text size="xs">{heartbeatText}</Text>
            </Group>
          </Tooltip>
          <Group gap={6} c="dimmed">
            <Icon path={node.currentVms > 0 ? mdiServerNetwork : mdiDatabaseOutline} size={0.65} />
            <Text size="xs">{node.currentVms > 0 ? 'VM 节点' : '容器节点'}</Text>
          </Group>
        </Group>

        <Group justify="space-between" align="center">
          <Group gap={6}>
            <Icon path={mdiChip} size={0.7} />
            <Text size="sm" fw={600}>参与调度</Text>
          </Group>
          <Switch
            checked={node.isSchedulable}
            disabled={isOffline}
            onChange={(e) => onToggleSchedulable?.(node.id, e.currentTarget.checked)}
          />
        </Group>
      </Stack>
    </Card>
  );
}
