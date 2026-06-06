import {
  ActionIcon,
  Badge,
  Button,
  Card,
  CopyButton,
  Grid,
  Group,
  NumberInput,
  Stack,
  Table,
  Text,
  TextInput,
  Title,
  Tooltip,
} from '@mantine/core'
import { showNotification } from '@mantine/notifications'
import {
  mdiCheck,
  mdiContentCopy,
  mdiFlagOutline,
  mdiRefresh,
  mdiShieldOff,
  mdiSwordCross,
} from '@mdi/js'
import { Icon } from '@mdi/react'
import dayjs from 'dayjs'
import duration from 'dayjs/plugin/duration'
import { FC, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useParams } from 'react-router'
import { GameProgress } from '@Components/GameProgress'
import { WithGameTab } from '@Components/WithGameTab'
import { WithNavBar } from '@Components/WithNavbar'
import { showErrorMsg } from '@Utils/Shared'
import { useGame } from '@Hooks/useGame'
import {
  awdPlayerApi,
  AwdAttackLogItem,
  AwdGameStatusModel,
  AwdScoreboardItem,
  TeamServiceStatus,
} from '../../../Api/AwdApi'
import { AwdRoundStatus } from '../../../Api/AwdApi'

dayjs.extend(duration)

const AwdStatusCard: FC<{ status?: AwdGameStatusModel }> = ({ status }) => {
  const { t } = useTranslation()
  const [now, setNow] = useState(dayjs())

  useEffect(() => {
    const interval = setInterval(() => setNow(dayjs()), 1000)
    return () => clearInterval(interval)
  }, [])

  if (!status) return null

  const roundEnd = dayjs(status.roundStartTime).add(status.roundDurationMinutes, 'minute')
  const remaining = dayjs.duration(roundEnd.diff(now))
  const isRunning = status.status === AwdRoundStatus.Running && remaining.asSeconds() > 0

  return (
    <Card withBorder>
      <Stack gap="xs">
        <Group justify="space-between">
          <Text fw="bold" size="lg">
            {t('game.awd.round', { current: status.currentRound })}
          </Text>
          <Badge color={isRunning ? 'teal' : 'gray'}>
            {isRunning ? t('game.awd.round_running') : t('game.awd.round_waiting')}
          </Badge>
        </Group>
        <GameProgress
          percentage={
            isRunning
              ? Math.min(
                  100,
                  (remaining.asMilliseconds() /
                    (status.roundDurationMinutes * 60 * 1000)) *
                    100
                )
              : 0
          }
        />
        <Text ta="center" fw="bold">
          {isRunning
            ? `${Math.floor(remaining.asMinutes())}:${remaining.format('ss')}`
            : t('game.awd.round_waiting')}
        </Text>
      </Stack>
    </Card>
  )
}

const AwdInstanceCard: FC<{ instance: TeamServiceStatus }> = ({ instance }) => {
  const { t } = useTranslation()

  const address = instance.port
    ? `${instance.ipAddress}:${instance.port}`
    : instance.ipAddress

  return (
    <Card withBorder>
      <Stack gap="xs">
        <Group justify="space-between">
          <Text fw="bold">{instance.teamName}</Text>
          <Badge color={instance.isRunning ? 'teal' : 'red'}>
            {instance.isRunning ? t('game.awd.service_up') : t('game.awd.service_down')}
          </Badge>
        </Group>
        <Group gap="xs">
          <Text size="sm" c="dimmed">
            {t('game.awd.address')}:
          </Text>
          <Text size="sm" ff="mono">
            {address ?? t('game.awd.no_address')}
          </Text>
          {address && (
            <CopyButton value={address}>
              {({ copied }) => (
                <ActionIcon
                  color={copied ? 'teal' : 'gray'}
                  variant="subtle"
                >
                  <Icon path={copied ? mdiCheck : mdiContentCopy} size={0.8} />
                </ActionIcon>
              )}
            </CopyButton>
          )}
        </Group>
      </Stack>
    </Card>
  )
}

const AwdScoreboardCard: FC<{ items: AwdScoreboardItem[] }> = ({ items }) => {
  const { t } = useTranslation()

  return (
    <Card withBorder>
      <Stack gap="xs">
        <Text fw="bold">{t('game.tab.scoreboard')}</Text>
        <Table>
          <Table.Thead>
            <Table.Tr>
              <Table.Th>{t('game.label.score_table.rank_total')}</Table.Th>
              <Table.Th>{t('common.label.team')}</Table.Th>
              <Table.Th>{t('game.awd.attack_score')}</Table.Th>
              <Table.Th>{t('game.awd.sla_score')}</Table.Th>
              <Table.Th>{t('game.awd.defense_lost')}</Table.Th>
              <Table.Th>{t('game.label.score_table.score_total')}</Table.Th>
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {items.slice(0, 10).map((item) => (
              <Table.Tr key={item.teamId}>
                <Table.Td>{item.rank}</Table.Td>
                <Table.Td>{item.teamName}</Table.Td>
                <Table.Td>+{item.attackScore}</Table.Td>
                <Table.Td>+{item.slaScore}</Table.Td>
                <Table.Td c="red">-{item.defenseLost}</Table.Td>
                <Table.Td fw="bold">{item.totalScore}</Table.Td>
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
      </Stack>
    </Card>
  )
}

const AwdAttackLogCard: FC<{ logs: AwdAttackLogItem[] }> = ({ logs }) => {
  const { t } = useTranslation()

  return (
    <Card withBorder>
      <Stack gap="xs">
        <Text fw="bold">{t('game.awd.attack_logs')}</Text>
        <Table>
          <Table.Thead>
            <Table.Tr>
              <Table.Th>{t('game.awd.time')}</Table.Th>
              <Table.Th>{t('game.awd.attacker')}</Table.Th>
              <Table.Th>{t('game.awd.victim')}</Table.Th>
              <Table.Th>{t('game.awd.service')}</Table.Th>
              <Table.Th>{t('game.awd.points')}</Table.Th>
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {logs.slice(0, 10).map((log, idx) => (
              <Table.Tr key={idx}>
                <Table.Td>{dayjs(log.time).format('HH:mm:ss')}</Table.Td>
                <Table.Td c="teal">{log.attackerTeam}</Table.Td>
                <Table.Td c="red">{log.victimTeam}</Table.Td>
                <Table.Td>{log.serviceName}</Table.Td>
                <Table.Td fw="bold">+{log.points}</Table.Td>
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
      </Stack>
    </Card>
  )
}

const AwdPage: FC = () => {
  const { id } = useParams()
  const numId = parseInt(id ?? '-1')
  const { game } = useGame(numId)
  const { t } = useTranslation()

  const [status, setStatus] = useState<AwdGameStatusModel | undefined>()
  const [instances, setInstances] = useState<TeamServiceStatus[]>([])
  const [scoreboard, setScoreboard] = useState<AwdScoreboardItem[]>([])
  const [attackLogs, setAttackLogs] = useState<AwdAttackLogItem[]>([])
  const [flag, setFlag] = useState('')
  const [loading, setLoading] = useState(false)

  const fetchData = async () => {
    if (numId < 0) return
    try {
      const [s, i, sb, al] = await Promise.all([
        awdPlayerApi.getGameStatus(numId),
        awdPlayerApi.getMyInstances(numId),
        awdPlayerApi.getScoreboard(numId),
        awdPlayerApi.getAttackLogs(numId, 20),
      ])
      setStatus(s.data)
      setInstances(i.data ?? [])
      setScoreboard(sb.data ?? [])
      setAttackLogs(al.data ?? [])
    } catch (err) {
      showErrorMsg(err, t)
    }
  }

  useEffect(() => {
    fetchData()
    const interval = setInterval(fetchData, 30000)
    return () => clearInterval(interval)
  }, [numId])

  const onSubmitFlag = async () => {
    if (!flag.trim()) return
    setLoading(true)
    try {
      await awdPlayerApi.submitFlag(numId, {
        flag: flag.trim(),
      })
      showNotification({
        color: 'teal',
        message: t('game.awd.flag_submitted'),
      })
      setFlag('')
      fetchData()
    } catch (err) {
      showErrorMsg(err, t)
    } finally {
      setLoading(false)
    }
  }

  return (
    <WithNavBar width="90%">
      <WithGameTab>
        <Stack pb="2rem" gap="md">
          <Grid>
            <Grid.Col span={{ base: 12, md: 4 }}>
              <AwdStatusCard status={status} />
            </Grid.Col>
            <Grid.Col span={{ base: 12, md: 8 }}>
              <Card withBorder>
                <Stack gap="xs">
                  <Text fw="bold">{t('game.awd.submit_flag')}</Text>
                  <Group gap="sm">
                    <TextInput
                      placeholder={t('game.awd.flag_placeholder')}
                      value={flag}
                      onChange={(e) => setFlag(e.currentTarget.value)}
                      style={{ flex: 1 }}
                    />
                    <Button
                      leftSection={<Icon path={mdiFlagOutline} size={1} />}
                      onClick={onSubmitFlag}
                      loading={loading}
                    >
                      {t('game.awd.submit')}
                    </Button>
                  </Group>
                </Stack>
              </Card>
            </Grid.Col>
          </Grid>

          <Title order={4}>{t('game.awd.my_instances')}</Title>
          <Grid>
            {instances.map((instance) => (
              <Grid.Col span={{ base: 12, sm: 6, lg: 4 }} key={instance.teamId}>
                <AwdInstanceCard instance={instance} />
              </Grid.Col>
            ))}
            {instances.length === 0 && (
              <Grid.Col span={12}>
                <Text c="dimmed" ta="center">
                  {t('game.awd.no_instances')}
                </Text>
              </Grid.Col>
            )}
          </Grid>

          <Grid>
            <Grid.Col span={{ base: 12, lg: 6 }}>
              <AwdScoreboardCard items={scoreboard} />
            </Grid.Col>
            <Grid.Col span={{ base: 12, lg: 6 }}>
              <AwdAttackLogCard logs={attackLogs} />
            </Grid.Col>
          </Grid>
        </Stack>
      </WithGameTab>
    </WithNavBar>
  )
}

export default AwdPage
