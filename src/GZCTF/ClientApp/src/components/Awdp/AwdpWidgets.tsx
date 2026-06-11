import { Group, Stack, Table, Text, ThemeIcon, Title } from '@mantine/core'
import { mdiServerOff } from '@mdi/js'
import { Icon } from '@mdi/react'
import { FC, ReactNode } from 'react'
import { YinyuPanel, YinyuStatusPill, YinyuStatusState, YinyuStatusTone } from '@Components/yinyu/YinyuUI'
import { AwdpChallengeStatus, AwdpPatchStatus, AwdpRoundStatus, CheckerStatus } from '@Api'

export type AwdpStatusLike = CheckerStatus | AwdpRoundStatus | AwdpPatchStatus | AwdpChallengeStatus | null | undefined

export const awdpStatusColor = (status?: AwdpStatusLike) => {
  switch (status) {
    case CheckerStatus.OK:
    case AwdpPatchStatus.ExpFailed:
    case AwdpChallengeStatus.Attacked:
    case AwdpChallengeStatus.Defended:
      return 'teal'
    case CheckerStatus.Mumble:
    case CheckerStatus.Skipped:
    case AwdpRoundStatus.PatchPhase:
    case AwdpPatchStatus.Pending:
    case AwdpPatchStatus.Unsupported:
    case AwdpChallengeStatus.Undefended:
      return 'yellow'
    case AwdpRoundStatus.AttackPhase:
      return 'blue'
    case AwdpRoundStatus.Finished:
    case AwdpChallengeStatus.Unattacked:
      return 'gray'
    default:
      return 'red'
  }
}

const awdpStatusTone = (status?: AwdpStatusLike): YinyuStatusTone => {
  switch (status) {
    case CheckerStatus.OK:
    case AwdpPatchStatus.ExpFailed:
    case AwdpChallengeStatus.Attacked:
    case AwdpChallengeStatus.Defended:
      return 'success'
    case CheckerStatus.Mumble:
    case CheckerStatus.Skipped:
    case AwdpRoundStatus.PatchPhase:
    case AwdpPatchStatus.Pending:
    case AwdpPatchStatus.Unsupported:
    case AwdpChallengeStatus.Undefended:
      return 'warm'
    case AwdpRoundStatus.AttackPhase:
      return 'success'
    case AwdpRoundStatus.Finished:
    case AwdpChallengeStatus.Unattacked:
      return 'neutral'
    default:
      return 'danger'
  }
}

const awdpStatusState = (status?: AwdpStatusLike): YinyuStatusState => {
  switch (status) {
    case AwdpRoundStatus.AttackPhase:
    case AwdpRoundStatus.PatchPhase:
    case AwdpPatchStatus.Pending:
      return 'running'
    case CheckerStatus.OK:
    case AwdpChallengeStatus.Defended:
      return 'solved'
    case AwdpRoundStatus.Finished:
    case AwdpChallengeStatus.Unattacked:
      return 'idle'
    case CheckerStatus.Mumble:
    case CheckerStatus.Skipped:
    case AwdpChallengeStatus.Undefended:
      return 'busy'
    default:
      return awdpStatusTone(status) === 'danger' ? 'alert' : 'open'
  }
}

export const AwdpSectionTitle: FC<{ title: string; extra?: ReactNode }> = ({ title, extra }) => (
  <Group justify="space-between" mb="sm" align="center" wrap="wrap">
    <Title order={4} style={{ minWidth: 0 }}>
      {title}
    </Title>
    {extra}
  </Group>
)

export const AwdpStatusBadge: FC<{ status: AwdpStatusLike; fallback?: string; label?: ReactNode }> = ({
  status,
  fallback = '-',
  label,
}) => (
  <YinyuStatusPill tone={awdpStatusTone(status)} state={awdpStatusState(status)}>
    {status ? (label ?? status) : fallback}
  </YinyuStatusPill>
)

export const AwdpInstanceStateBadge: FC<{ running: boolean; runningText: string; stoppedText: string }> = ({
  running,
  runningText,
  stoppedText,
}) => (
  <YinyuStatusPill tone={running ? 'success' : 'neutral'} state={running ? 'running' : 'idle'}>
    {running ? runningText : stoppedText}
  </YinyuStatusPill>
)

export const AwdpEndpointText: FC<{ ip?: string | null; port?: number | null }> = ({ ip, port }) => (
  <Text
    size="sm"
    c={ip ? undefined : 'dimmed'}
    truncate
    style={{ fontFamily: ip ? 'var(--mantine-font-family-monospace)' : undefined }}
  >
    {ip ? `${ip}:${port ?? '-'}` : '-'}
  </Text>
)

export const AwdpEmptyTableRow: FC<{ colSpan: number; text: string }> = ({ colSpan, text }) => (
  <Table.Tr>
    <Table.Td colSpan={colSpan}>
      <Stack align="center" gap="xs" py="lg">
        <ThemeIcon variant="light" color="gray" radius="md" size="lg">
          <Icon path={mdiServerOff} size={0.85} />
        </ThemeIcon>
        <Text c="dimmed" size="sm" ta="center">
          {text}
        </Text>
      </Stack>
    </Table.Td>
  </Table.Tr>
)

export const AwdpMetricTile: FC<{
  icon: string
  label: string
  value: ReactNode
  sub?: ReactNode
  color?: string
}> = ({ icon, label, value, sub, color = 'indigo' }) => (
  <YinyuPanel
    p="md"
    h="100%"
    className="yy-metric-tile"
    style={{ borderLeft: `3px solid var(--mantine-color-${color}-6)` }}
  >
    <Group gap="sm" wrap="nowrap" align="center">
      <ThemeIcon variant="light" color={color} radius="sm" size="lg">
        <Icon path={icon} size={0.85} />
      </ThemeIcon>
      <Stack gap={0} miw={0}>
        <Text size="xs" c="dimmed" truncate>
          {label}
        </Text>
        <Text fw={800} lh={1.15} truncate>
          {value}
        </Text>
        {sub && (
          <Text size="xs" c="dimmed" truncate>
            {sub}
          </Text>
        )}
      </Stack>
    </Group>
  </YinyuPanel>
)
