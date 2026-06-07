import {
  Badge,
  Button,
  FileInput,
  Group,
  Paper,
  PasswordInput,
  ScrollArea,
  Select,
  SimpleGrid,
  Stack,
  Table,
  Text,
  Title,
} from '@mantine/core'
import { showNotification } from '@mantine/notifications'
import {
  mdiCheck,
  mdiChartLine,
  mdiFlagOutline,
  mdiPackageUp,
  mdiRefresh,
  mdiRestore,
  mdiServerNetwork,
  mdiShieldCheckOutline,
  mdiTimerSand,
} from '@mdi/js'
import { Icon } from '@mdi/react'
import * as signalR from '@microsoft/signalr'
import dayjs from 'dayjs'
import { FC, useCallback, useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useParams } from 'react-router'
import {
  AwdpEmptyTableRow,
  AwdpEndpointText,
  AwdpInstanceStateBadge,
  AwdpMetricTile,
  AwdpSectionTitle,
  AwdpStatusBadge,
  AwdpStatusLike,
  awdpStatusColor,
} from '@Components/Awdp/AwdpWidgets'
import { WithGameTab } from '@Components/WithGameTab'
import { WithNavBar } from '@Components/WithNavbar'
import { WithRole } from '@Components/WithRole'
import { encryptApiData } from '@Utils/Crypto'
import { showErrorMsg } from '@Utils/Shared'
import { useConfig } from '@Hooks/useConfig'
import { AwdpChallengeStatus, Role } from '@Api'
import {
  AwdpAttackLogItem,
  AwdpGameStatusModel,
  AwdpPatchStatusItem,
  AwdpScoreboardItem,
  AwdpTeamServiceStatus,
  awdpPlayerApi,
} from '../../../Api/AwdpApi'

const Awd: FC = () => {
  const { id } = useParams()
  const gameId = parseInt(id ?? '-1')
  const { t } = useTranslation()
  const { config } = useConfig()
  const awd = (key: string, defaultValue: string, options?: Record<string, unknown>) =>
    t(`game.awd.${key}`, { defaultValue, ...options })
  const statusLabel = (value?: AwdpStatusLike) => (value ? awd(`status_labels.${value}`, String(value)) : undefined)

  const [status, setStatus] = useState<AwdpGameStatusModel>()
  const [instances, setInstances] = useState<AwdpTeamServiceStatus[]>([])
  const [scoreboard, setScoreboard] = useState<AwdpScoreboardItem[]>([])
  const [attackLogs, setAttackLogs] = useState<AwdpAttackLogItem[]>([])
  const [patchStatus, setPatchStatus] = useState<AwdpPatchStatusItem[]>([])
  const [flag, setFlag] = useState('')
  const [patchServiceId, setPatchServiceId] = useState<string | null>(null)
  const [patchFile, setPatchFile] = useState<File | null>(null)
  const [loading, setLoading] = useState(false)

  const serviceOptions = useMemo(
    () => patchStatus.map((item) => ({ value: item.serviceId.toString(), label: item.serviceName })),
    [patchStatus]
  )
  const myTeamId = instances[0]?.teamId
  const myScore = scoreboard.find((item) => item.teamId === myTeamId)
  const runningInstances = instances.filter((item) => item.isRunning).length
  const remainingResets = instances.reduce((sum, item) => sum + item.remainingResetCount, 0)
  const remainingRecoveries = instances.reduce((sum, item) => sum + item.remainingRecoveryCount, 0)
  const defendedServices = patchStatus.filter((item) => item.defenseStatus === AwdpChallengeStatus.Defended).length
  const roundStart = status?.roundStartTime ? dayjs(status.roundStartTime).format('YYYY-MM-DD HH:mm:ss') : '-'
  const runningText = awd('running_status', 'Running')
  const stoppedText = awd('stopped', 'Stopped')

  const load = useCallback(
    async (showSpinner = true) => {
      if (gameId <= 0) return
      if (showSpinner) {
        setLoading(true)
      }

      try {
        const [statusRes, instanceRes, scoreRes, logRes, patchRes] = await Promise.all([
          awdpPlayerApi.getStatus(gameId),
          awdpPlayerApi.getInstances(gameId),
          awdpPlayerApi.getScoreboard(gameId),
          awdpPlayerApi.getAttackLogs(gameId, 30, 0),
          awdpPlayerApi.getPatchStatus(gameId),
        ])
        setStatus(statusRes.data)
        setInstances(instanceRes.data)
        setScoreboard(scoreRes.data)
        setAttackLogs(logRes.data.data)
        setPatchStatus(patchRes.data)
        setPatchServiceId((current) => current ?? patchRes.data[0]?.serviceId.toString() ?? null)
      } catch (e) {
        if (showSpinner) {
          showErrorMsg(e, t)
        } else {
          console.error(e)
        }
      } finally {
        if (showSpinner) {
          setLoading(false)
        }
      }
    },
    [gameId, t]
  )

  useEffect(() => {
    void load()
  }, [load])

  useEffect(() => {
    setPatchServiceId((current) => {
      if (patchStatus.length === 0) return null
      if (current && patchStatus.some((item) => item.serviceId.toString() === current)) return current
      return patchStatus[0].serviceId.toString()
    })
  }, [patchStatus])

  useEffect(() => {
    if (gameId <= 0) return

    const connection = new signalR.HubConnectionBuilder()
      .withUrl(`/hub/monitor?game=${gameId}`)
      .withHubProtocol(new signalR.JsonHubProtocol())
      .withAutomaticReconnect()
      .configureLogging(signalR.LogLevel.None)
      .build()

    connection.serverTimeoutInMilliseconds = 60 * 1000 * 60 * 2

    connection.on('ReceivedAwdpRoundChange', (nextStatus: AwdpGameStatusModel) => {
      setStatus(nextStatus)
      void load(false)
    })
    connection.on('ReceivedAwdpServiceStatusChange', () => {
      void load(false)
    })
    connection.on('ReceivedAwdpPatchResult', () => {
      void load(false)
    })
    connection.onreconnected(() => {
      void load(false)
    })

    void connection.start().catch((err) => {
      console.error(err)
    })

    return () => {
      void connection.stop().catch((err) => {
        console.error(err)
      })
    }
  }, [gameId, load])

  const submitFlag = async () => {
    if (!flag.trim()) return
    setLoading(true)
    try {
      const encrypted = await encryptApiData(t, flag.trim(), config.apiPublicKey)
      const res = await awdpPlayerApi.submitFlag(gameId, encrypted)
      showNotification({
        color: 'teal',
        message: `${res.data.serviceName} +${res.data.points}`,
        icon: <Icon path={mdiCheck} size={1} />,
      })
      setFlag('')
      await load()
    } catch (e) {
      showErrorMsg(e, t)
    } finally {
      setLoading(false)
    }
  }

  const submitPatch = async () => {
    if (!patchServiceId || !patchFile) return
    setLoading(true)
    try {
      const res = await awdpPlayerApi.submitPatch(gameId, Number(patchServiceId), patchFile)
      showNotification({
        color: awdpStatusColor(res.data.finalStatus),
        message: `${res.data.serviceName}: ${statusLabel(res.data.finalStatus) ?? res.data.finalStatus}`,
        icon: <Icon path={mdiShieldCheckOutline} size={1} />,
      })
      setPatchFile(null)
      await load()
    } catch (e) {
      showErrorMsg(e, t)
    } finally {
      setLoading(false)
    }
  }

  const instanceAction = async (action: () => Promise<unknown>, message: string) => {
    setLoading(true)
    try {
      await action()
      showNotification({ color: 'teal', message, icon: <Icon path={mdiCheck} size={1} /> })
      await load()
    } catch (e) {
      showErrorMsg(e, t)
    } finally {
      setLoading(false)
    }
  }

  return (
    <WithNavBar width="90%" minWidth={0}>
      <WithRole requiredRole={Role.User}>
        <WithGameTab>
          <Stack gap="md">
            <Paper p="md" shadow="xs" radius="md" withBorder>
              <Group justify="space-between" align="flex-start" wrap="wrap">
                <Stack gap={2}>
                  <Title order={4}>{awd('round', 'Round {{round}}', { round: status?.currentRound ?? 0 })}</Title>
                  <Text size="sm" c="dimmed">
                    {awd('round_started', 'Started at {{time}}', { time: roundStart })}
                  </Text>
                </Stack>
                <Group>
                  <AwdpStatusBadge
                    status={status?.status}
                    fallback={awd('idle', 'Idle')}
                    label={statusLabel(status?.status)}
                  />
                  <Button
                    leftSection={<Icon path={mdiRefresh} size={1} />}
                    variant="outline"
                    loading={loading}
                    onClick={() => void load()}
                  >
                    {awd('refresh', 'Refresh')}
                  </Button>
                </Group>
              </Group>
              <SimpleGrid cols={{ base: 1, xs: 2, md: 5 }} mt="md">
                <AwdpMetricTile
                  icon={mdiTimerSand}
                  color={awdpStatusColor(status?.status)}
                  label={awd('phase_status', 'Phase')}
                  value={statusLabel(status?.status) ?? awd('idle', 'Idle')}
                  sub={awd('phase_minutes', '{{attack}}+{{patch}} min', {
                    attack: status?.attackPhaseMinutes ?? 0,
                    patch: status?.patchPhaseMinutes ?? 0,
                  })}
                />
                <AwdpMetricTile
                  icon={mdiChartLine}
                  color="teal"
                  label={awd('my_score', 'My AWDP score')}
                  value={myScore?.awdpScore ?? 0}
                  sub={myScore ? `#${myScore.rank}` : '-'}
                />
                <AwdpMetricTile
                  icon={mdiServerNetwork}
                  color={runningInstances === instances.length && instances.length > 0 ? 'teal' : 'yellow'}
                  label={awd('running_instances', 'Running instances')}
                  value={`${runningInstances}/${instances.length}`}
                  sub={instances[0]?.teamName ?? '-'}
                />
                <AwdpMetricTile
                  icon={mdiRefresh}
                  color="cyan"
                  label={awd('remaining_resets', 'Remaining resets')}
                  value={remainingResets}
                  sub={awd('recoveries_left', '{{count}} recoveries', { count: remainingRecoveries })}
                />
                <AwdpMetricTile
                  icon={mdiShieldCheckOutline}
                  color="violet"
                  label={awd('defended_services', 'Defended services')}
                  value={`${defendedServices}/${patchStatus.length}`}
                  sub={awd('patch_status', 'Patch Status')}
                />
              </SimpleGrid>
            </Paper>

            <SimpleGrid cols={{ base: 1, md: 2 }}>
              <Paper p="md" shadow="xs" radius="md" withBorder>
                <Stack>
                  <AwdpSectionTitle title={awd('flag', 'Flag')} />
                  <Group align="flex-end" wrap="wrap">
                    <PasswordInput
                      label={awd('flag', 'Flag')}
                      value={flag}
                      onChange={(e) => setFlag(e.currentTarget.value)}
                      onKeyDown={(e) => {
                        if (e.key === 'Enter') void submitFlag()
                      }}
                      style={{ flex: '1 1 16rem' }}
                    />
                    <Button
                      leftSection={<Icon path={mdiFlagOutline} size={1} />}
                      loading={loading}
                      disabled={!flag.trim()}
                      onClick={submitFlag}
                    >
                      {awd('submit', 'Submit')}
                    </Button>
                  </Group>
                </Stack>
              </Paper>

              <Paper p="md" shadow="xs" radius="md" withBorder>
                <Stack>
                  <AwdpSectionTitle title={awd('patch', 'Patch')} />
                  <Group align="flex-end" wrap="wrap">
                    <Select
                      label={awd('service', 'Service')}
                      data={serviceOptions}
                      value={patchServiceId}
                      onChange={setPatchServiceId}
                      style={{ flex: '1 1 12rem' }}
                    />
                    <FileInput
                      label={awd('archive', 'Archive')}
                      value={patchFile}
                      onChange={setPatchFile}
                      accept=".tar.gz,.tgz"
                      style={{ flex: '2 1 16rem' }}
                    />
                    <Button
                      leftSection={<Icon path={mdiPackageUp} size={1} />}
                      loading={loading}
                      disabled={!patchServiceId || !patchFile}
                      onClick={submitPatch}
                    >
                      {awd('upload', 'Upload')}
                    </Button>
                  </Group>
                </Stack>
              </Paper>
            </SimpleGrid>

            <Paper p="md" shadow="xs" radius="md" withBorder>
              <AwdpSectionTitle
                title={awd('instances', 'Instances')}
                extra={<Badge variant="light">{`${runningInstances}/${instances.length}`}</Badge>}
              />
              <ScrollArea offsetScrollbars>
                <Table highlightOnHover verticalSpacing="sm" miw={860}>
                  <Table.Thead>
                    <Table.Tr>
                      <Table.Th>{awd('service', 'Service')}</Table.Th>
                      <Table.Th>{awd('endpoint', 'Endpoint')}</Table.Th>
                      <Table.Th>{awd('status', 'Status')}</Table.Th>
                      <Table.Th>{awd('checker', 'Checker')}</Table.Th>
                      <Table.Th>{awd('resets', 'Resets')}</Table.Th>
                      <Table.Th>{awd('recoveries', 'Recoveries')}</Table.Th>
                      <Table.Th />
                    </Table.Tr>
                  </Table.Thead>
                  <Table.Tbody>
                    {instances.length === 0 ? (
                      <AwdpEmptyTableRow colSpan={7} text={awd('no_instances', 'No instances')} />
                    ) : (
                      instances.map((item) => (
                        <Table.Tr key={item.instanceId}>
                          <Table.Td>{item.serviceName}</Table.Td>
                          <Table.Td>
                            <AwdpEndpointText ip={item.ipAddress} port={item.port} />
                          </Table.Td>
                          <Table.Td>
                            <AwdpInstanceStateBadge
                              running={item.isRunning}
                              runningText={runningText}
                              stoppedText={stoppedText}
                            />
                          </Table.Td>
                          <Table.Td>
                            <AwdpStatusBadge
                              status={item.lastCheckerStatus}
                              label={statusLabel(item.lastCheckerStatus)}
                            />
                          </Table.Td>
                          <Table.Td>{item.remainingResetCount}</Table.Td>
                          <Table.Td>{item.remainingRecoveryCount}</Table.Td>
                          <Table.Td>
                            <Group justify="right" gap="xs" wrap="nowrap">
                              <Button
                                size="xs"
                                variant="outline"
                                disabled={loading}
                                leftSection={<Icon path={mdiRefresh} size={0.75} />}
                                onClick={() =>
                                  instanceAction(
                                    () => awdpPlayerApi.resetInstance(item.instanceId),
                                    awd('instance_reset', 'Instance reset.')
                                  )
                                }
                              >
                                {awd('reset', 'Reset')}
                              </Button>
                              <Button
                                size="xs"
                                variant="outline"
                                disabled={loading}
                                leftSection={<Icon path={mdiRestore} size={0.75} />}
                                onClick={() =>
                                  instanceAction(
                                    () => awdpPlayerApi.recoverInstance(item.instanceId),
                                    awd('instance_recovered', 'Instance recovered.')
                                  )
                                }
                              >
                                {awd('recover', 'Recover')}
                              </Button>
                            </Group>
                          </Table.Td>
                        </Table.Tr>
                      ))
                    )}
                  </Table.Tbody>
                </Table>
              </ScrollArea>
            </Paper>

            <SimpleGrid cols={{ base: 1, md: 2 }}>
              <Paper p="md" shadow="xs" radius="md" withBorder>
                <AwdpSectionTitle
                  title={awd('scoreboard', 'AWDP Scoreboard')}
                  extra={
                    myScore && <Badge color="teal" variant="light">{`${myScore.teamName} #${myScore.rank}`}</Badge>
                  }
                />
                <ScrollArea h="20rem" offsetScrollbars>
                  <Table highlightOnHover verticalSpacing="sm" miw={720}>
                    <Table.Thead>
                      <Table.Tr>
                        <Table.Th>#</Table.Th>
                        <Table.Th>{awd('team', 'Team')}</Table.Th>
                        <Table.Th>{awd('attack', 'Attack')}</Table.Th>
                        <Table.Th>SLA</Table.Th>
                        <Table.Th>{awd('patch', 'Patch')}</Table.Th>
                        <Table.Th>{awd('penalty', 'Penalty')}</Table.Th>
                        <Table.Th>{awd('total', 'Total')}</Table.Th>
                      </Table.Tr>
                    </Table.Thead>
                    <Table.Tbody>
                      {scoreboard.length === 0 ? (
                        <AwdpEmptyTableRow colSpan={7} text={awd('no_scoreboard', 'No scoreboard data')} />
                      ) : (
                        scoreboard.map((row) => {
                          const isSelf = row.teamId === myTeamId

                          return (
                            <Table.Tr
                              key={row.teamId}
                              style={isSelf ? { backgroundColor: 'var(--mantine-color-teal-light)' } : undefined}
                            >
                              <Table.Td>{row.rank}</Table.Td>
                              <Table.Td>
                                <Group gap="xs" wrap="nowrap">
                                  <Text fw={isSelf ? 700 : 400} truncate>
                                    {row.teamName}
                                  </Text>
                                  {isSelf && (
                                    <Badge color="teal" variant="light" size="xs">
                                      {awd('me', 'Me')}
                                    </Badge>
                                  )}
                                </Group>
                              </Table.Td>
                              <Table.Td>
                                <Text c="teal" fw={600}>
                                  {row.attackScore}
                                </Text>
                              </Table.Td>
                              <Table.Td>{row.slaScore}</Table.Td>
                              <Table.Td>{row.patchScore}</Table.Td>
                              <Table.Td>{row.penaltyScore}</Table.Td>
                              <Table.Td>
                                <Text fw={700}>{row.awdpScore}</Text>
                              </Table.Td>
                            </Table.Tr>
                          )
                        })
                      )}
                    </Table.Tbody>
                  </Table>
                </ScrollArea>
              </Paper>

              <Paper p="md" shadow="xs" radius="md" withBorder>
                <AwdpSectionTitle
                  title={awd('patch_status', 'Patch Status')}
                  extra={<Badge color="violet" variant="light">{`${defendedServices}/${patchStatus.length}`}</Badge>}
                />
                <ScrollArea h="20rem" offsetScrollbars>
                  <Table highlightOnHover verticalSpacing="sm" miw={760}>
                    <Table.Thead>
                      <Table.Tr>
                        <Table.Th>{awd('service', 'Service')}</Table.Th>
                        <Table.Th>{awd('attack', 'Attack')}</Table.Th>
                        <Table.Th>{awd('defense', 'Defense')}</Table.Th>
                        <Table.Th>{awd('result', 'Result')}</Table.Th>
                        <Table.Th>{awd('time', 'Time')}</Table.Th>
                      </Table.Tr>
                    </Table.Thead>
                    <Table.Tbody>
                      {patchStatus.length === 0 ? (
                        <AwdpEmptyTableRow colSpan={5} text={awd('no_patch_status', 'No patch status')} />
                      ) : (
                        patchStatus.map((item) => (
                          <Table.Tr key={item.serviceId}>
                            <Table.Td>{item.serviceName}</Table.Td>
                            <Table.Td>
                              <AwdpStatusBadge status={item.attackStatus} label={statusLabel(item.attackStatus)} />
                            </Table.Td>
                            <Table.Td>
                              <AwdpStatusBadge status={item.defenseStatus} label={statusLabel(item.defenseStatus)} />
                            </Table.Td>
                            <Table.Td>
                              {item.lastPatchResult ? (
                                <AwdpStatusBadge
                                  status={item.lastPatchResult}
                                  label={statusLabel(item.lastPatchResult)}
                                />
                              ) : (
                                <Text size="sm" c="dimmed">
                                  -
                                </Text>
                              )}
                            </Table.Td>
                            <Table.Td>
                              <Text size="sm" c="dimmed" style={{ fontFamily: 'var(--mantine-font-family-monospace)' }}>
                                {item.lastPatchTime ? dayjs(item.lastPatchTime).format('YYYY-MM-DD HH:mm:ss') : '-'}
                              </Text>
                            </Table.Td>
                          </Table.Tr>
                        ))
                      )}
                    </Table.Tbody>
                  </Table>
                </ScrollArea>
              </Paper>
            </SimpleGrid>

            <Paper p="md" shadow="xs" radius="md" withBorder>
              <AwdpSectionTitle
                title={awd('attack_logs', 'Attack Logs')}
                extra={<Badge variant="light">{attackLogs.length}</Badge>}
              />
              <ScrollArea h="18rem" offsetScrollbars>
                <Table highlightOnHover verticalSpacing="sm" miw={780}>
                  <Table.Thead>
                    <Table.Tr>
                      <Table.Th>{awd('time', 'Time')}</Table.Th>
                      <Table.Th>{awd('attacker_team', 'Attacker')}</Table.Th>
                      <Table.Th>{awd('victim_team', 'Victim')}</Table.Th>
                      <Table.Th>{awd('service', 'Service')}</Table.Th>
                      <Table.Th>{awd('points', 'Points')}</Table.Th>
                    </Table.Tr>
                  </Table.Thead>
                  <Table.Tbody>
                    {attackLogs.length === 0 ? (
                      <AwdpEmptyTableRow colSpan={5} text={awd('no_attack_logs', 'No attack logs')} />
                    ) : (
                      attackLogs.map((log, idx) => (
                        <Table.Tr key={`${log.time}-${idx}`}>
                          <Table.Td>
                            <Text size="sm" c="dimmed" style={{ fontFamily: 'var(--mantine-font-family-monospace)' }}>
                              {dayjs(log.time).format('YYYY-MM-DD HH:mm:ss')}
                            </Text>
                          </Table.Td>
                          <Table.Td>{log.attackerTeam}</Table.Td>
                          <Table.Td>{log.victimTeam}</Table.Td>
                          <Table.Td>{log.serviceName}</Table.Td>
                          <Table.Td>
                            <Text c="teal" fw={700}>
                              +{log.points}
                            </Text>
                          </Table.Td>
                        </Table.Tr>
                      ))
                    )}
                  </Table.Tbody>
                </Table>
              </ScrollArea>
            </Paper>
          </Stack>
        </WithGameTab>
      </WithRole>
    </WithNavBar>
  )
}

export default Awd
