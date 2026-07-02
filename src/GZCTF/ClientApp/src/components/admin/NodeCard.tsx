import { Badge, Divider, Group, Progress, RingProgress, Stack, Switch, Text, Tooltip } from '@mantine/core'
import { mdiChip, mdiDatabaseOutline, mdiLan, mdiServerNetwork, mdiTimerOutline } from '@mdi/js'
import { Icon } from '@mdi/react'
import dayjs from 'dayjs'
import relativeTime from 'dayjs/plugin/relativeTime'
import { KeyboardEvent, ReactNode } from 'react'
import { YinyuPanel, YinyuStatusPill, YinyuStatusState, YinyuStatusTone } from '@Components/yinyu/YinyuUI'

dayjs.extend(relativeTime)

export interface NodeInfo {
  id: string
  name: string
  hostAddress: string
  status: string | number
  capabilities?: string | number
  cpuLoad: number
  memoryLoad: number
  currentContainers: number
  maxContainers: number
  currentVms: number
  maxVms: number
  usedPorts?: number
  totalPorts?: number
  portPoolStart?: number | null
  portPoolEnd?: number | null
  portPoolMode?: string | null
  lastHeartbeat?: string | null
  isSchedulable: boolean
  isLocal: boolean
  agentPort: number
}

const statusMap: Record<
  string,
  { label: string; color: string; tone: YinyuStatusTone; state: YinyuStatusState; semantic: string }
> = {
  '0': { label: '未知', color: 'gray', tone: 'neutral', state: 'idle', semantic: 'unknown' },
  unknown: { label: '未知', color: 'gray', tone: 'neutral', state: 'idle', semantic: 'unknown' },
  '1': { label: '在线', color: 'green', tone: 'success', state: 'running', semantic: 'online' },
  online: { label: '在线', color: 'green', tone: 'success', state: 'running', semantic: 'online' },
  '2': { label: '离线', color: 'red', tone: 'danger', state: 'alert', semantic: 'offline' },
  offline: { label: '离线', color: 'red', tone: 'danger', state: 'alert', semantic: 'offline' },
  '3': { label: '繁忙', color: 'violet', tone: 'warm', state: 'busy', semantic: 'busy' },
  busy: { label: '繁忙', color: 'violet', tone: 'warm', state: 'busy', semantic: 'busy' },
  '4': { label: '异常', color: 'red', tone: 'danger', state: 'alert', semantic: 'error' },
  error: { label: '异常', color: 'red', tone: 'danger', state: 'alert', semantic: 'error' },
}

function normalizeKey(value: string | number | undefined) {
  return String(value ?? '').toLowerCase()
}

function toPercent(value: number) {
  if (!Number.isFinite(value)) return 0
  return Math.max(0, Math.min(100, value * 100))
}

function ratio(current: number, total: number) {
  if (!Number.isFinite(total) || total <= 0) return 0
  return Math.max(0, Math.min(100, (current / total) * 100))
}

function capabilityLabels(value: string | number | undefined) {
  const key = normalizeKey(value)
  if (!key || key === '0' || key === 'none') return ['None']
  if (key.includes('docker') || key === '1') return key.includes('kvm') || key === '3' ? ['Docker', 'KVM'] : ['Docker']
  if (key.includes('kvm') || key === '2') return ['KVM']
  if (key === '3') return ['Docker', 'KVM']
  return [String(value)]
}

function capabilityMeta(item: string) {
  const key = item.toLowerCase()
  if (key === 'docker') return { color: 'teal', semantic: 'docker' }
  if (key === 'kvm') return { color: 'violet', semantic: 'kvm' }
  return { color: 'gray', semantic: 'neutral' }
}

function pressureColor(value: number) {
  if (value >= 90) return 'red'
  if (value >= 70) return 'orange'
  return 'teal'
}

function portPoolName(mode?: string | null) {
  if (mode === 'nginx') return '公网转发池'
  if (mode === 'docker') return 'Docker 端口池'
  if (mode === 'docker-random') return 'Docker 随机端口'
  if (mode === 'nginx-unconfigured') return '公网转发未配置'
  return '端口模式'
}

function MetricLine({
  label,
  current,
  total,
  color,
  valueLabel,
}: {
  label: string
  current: number
  total: number
  color: string
  valueLabel?: string
}) {
  const value = ratio(current, total)

  return (
    <Stack gap={4}>
      <Group justify="space-between" gap="xs">
        <Text size="xs" c="dimmed">
          {label}
        </Text>
        <Text size="xs" fw={600}>
          {valueLabel ?? `${current}/${total}`}
        </Text>
      </Group>
      <Progress value={value} color={color} size="xs" radius="xs" />
    </Stack>
  )
}

export function NodeCard({
  node,
  onToggleSchedulable,
  rightSection,
  selected,
  onSelect,
}: {
  node: NodeInfo
  onToggleSchedulable?: (id: string, val: boolean) => void
  rightSection?: ReactNode
  selected?: boolean
  onSelect?: (node: NodeInfo) => void
}) {
  const status = statusMap[normalizeKey(node.status)] ?? {
    label: String(node.status ?? '未知'),
    color: 'gray',
    tone: 'neutral' as const,
    state: 'idle' as const,
    semantic: 'unknown',
  }
  const heartbeat = node.lastHeartbeat ? dayjs(node.lastHeartbeat) : null
  const heartbeatText = heartbeat?.isValid() ? heartbeat.fromNow() : '无心跳'
  const cpu = toPercent(node.cpuLoad)
  const memory = toPercent(node.memoryLoad)
  const isOffline = normalizeKey(node.status) === 'offline' || normalizeKey(node.status) === '2'
  const containerUsage = ratio(node.currentContainers, node.maxContainers)
  const vmUsage = ratio(node.currentVms, node.maxVms)
  const portUsage = ratio(node.usedPorts ?? 0, node.totalPorts ?? 0)
  const portPoolLabel =
    node.portPoolStart && node.portPoolEnd
      ? `${node.usedPorts ?? 0}/${node.totalPorts ?? 0} (${node.portPoolStart}-${node.portPoolEnd}${
          node.portPoolMode === 'nginx' ? '，全局共享' : ''
        })`
      : node.portPoolMode === 'docker-random'
        ? 'Docker 自动分配'
        : `${node.usedPorts ?? 0}/${node.totalPorts ?? 0}`

  const handleKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    if (!onSelect) return
    if (event.key === 'Enter' || event.key === ' ') {
      event.preventDefault()
      onSelect(node)
    }
  }

  return (
    <div
      role={onSelect ? 'button' : undefined}
      tabIndex={onSelect ? 0 : undefined}
      onClick={() => onSelect?.(node)}
      onKeyDown={handleKeyDown}
      style={{ cursor: onSelect ? 'pointer' : undefined }}
    >
      <YinyuPanel
        p="md"
        cells={42}
        data-testid={`node-card-${node.id}`}
        className={selected ? 'yy-admin-node-card-selected' : undefined}
        style={{
          borderTop: `3px solid var(--mantine-color-${status.color}-6)`,
          opacity: isOffline ? 0.78 : 1,
        }}
      >
        <Stack gap="sm">
        <Group justify="space-between" align="flex-start" wrap="nowrap">
          <Stack gap={2} style={{ minWidth: 0 }}>
            <Group gap={6} wrap="nowrap">
              <Text fw={700} truncate>
                {node.name || node.hostAddress}
              </Text>
              {node.isLocal && (
                <Badge size="sm" variant="light" color="blue" className="yy-semantic-badge" data-semantic="local">
                  本地
                </Badge>
              )}
            </Group>
            <Group gap={6} c="dimmed" wrap="nowrap">
              <Icon path={mdiLan} size={0.62} />
              <Text size="xs" truncate>
                {node.hostAddress}:{node.agentPort}
              </Text>
            </Group>
          </Stack>
          <Group gap={6} wrap="nowrap">
            <YinyuStatusPill tone={status.tone} state={status.state} data-semantic={status.semantic}>
              {status.label}
            </YinyuStatusPill>
            {rightSection}
          </Group>
        </Group>

        <Group gap="xs">
          {capabilityLabels(node.capabilities).map((item) => {
            const meta = capabilityMeta(item)
            return (
              <Badge
                key={item}
                size="sm"
                variant="outline"
                color={meta.color}
                className="yy-semantic-badge"
                data-semantic={meta.semantic}
              >
                {item}
              </Badge>
            )
          })}
        </Group>

        <Group grow align="center">
          <RingProgress
            size={86}
            thickness={8}
            roundCaps
            sections={[{ value: cpu, color: cpu >= 85 ? 'red' : cpu >= 65 ? 'orange' : 'blue' }]}
            label={
              <Text ta="center" size="xs" fw={700}>
                {cpu.toFixed(0)}%
              </Text>
            }
          />
          <Stack gap={8}>
            <MetricLine
              label="内存负载"
              current={Number(memory.toFixed(0))}
              total={100}
              color={pressureColor(memory)}
              valueLabel={`${memory.toFixed(0)}%`}
            />
            <MetricLine
              label="容器容量"
              current={node.currentContainers}
              total={node.maxContainers}
              color={pressureColor(containerUsage)}
            />
            <MetricLine
              label="虚拟机容量"
              current={node.currentVms}
              total={node.maxVms}
              color={pressureColor(vmUsage)}
            />
            {node.portPoolMode && (
              <MetricLine
                label={portPoolName(node.portPoolMode)}
                current={node.usedPorts ?? 0}
                total={Math.max(node.totalPorts ?? 0, 1)}
                color={pressureColor(portUsage)}
                valueLabel={portPoolLabel}
              />
            )}
          </Stack>
        </Group>

        <Divider />

        <Group justify="space-between" align="center">
          <Tooltip
            label={node.lastHeartbeat ? dayjs(node.lastHeartbeat).format('YYYY-MM-DD HH:mm:ss') : '没有收到过心跳'}
          >
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
            <Text size="sm" fw={600}>
              参与调度
            </Text>
          </Group>
          <Switch
            checked={node.isSchedulable}
            disabled={isOffline}
            onChange={(e) => onToggleSchedulable?.(node.id, e.currentTarget.checked)}
          />
        </Group>
        </Stack>
      </YinyuPanel>
    </div>
  )
}
