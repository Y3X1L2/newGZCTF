import {
  Badge,
  Button,
  Divider,
  Group,
  NumberInput,
  ScrollArea,
  Select,
  SimpleGrid,
  Stack,
  Table,
  Text,
  TextInput,
  Textarea,
  Title,
} from '@mantine/core'
import { useModals } from '@mantine/modals'
import { showNotification } from '@mantine/notifications'
import {
  mdiCheck,
  mdiContentSaveOutline,
  mdiDeleteOutline,
  mdiPackageUp,
  mdiPlay,
  mdiPlus,
  mdiRefresh,
  mdiRestore,
  mdiServerNetwork,
  mdiStop,
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
import { WithGameEditTab } from '@Components/admin/WithGameEditTab'
import { YinyuPanel, YinyuTableShell } from '@Components/yinyu/YinyuUI'
import { showErrorMsg } from '@Utils/Shared'
import { AwdpPatchStatus } from '@Api'
import {
  AwdpGameStatusModel,
  AwdpPatchSubmissionViewModel,
  AwdpServiceStatusModel,
  AwdpServiceViewModel,
  awdpAdminApi,
} from '../../../../Api/AwdpApi'

const defaultService = (): Omit<AwdpServiceViewModel, 'id'> => ({
  name: '',
  imageName: '',
  exposePort: 80,
  checkerScript: '',
  checkerEntrypoint: 'python3 checker.py',
  expScript: '',
  expEntrypoint: 'python3 exp.py',
  originalScore: 1000,
  attackPoints: 50,
  slaPoints: 20,
  patchPoints: 100,
  serviceAbnormalPenalty: 200,
  maxAttackPerRound: 3,
  attackPhaseMinutes: 15,
  patchPhaseMinutes: 10,
  totalRounds: 20,
  maxResetCount: 10,
  maxRecoveryCount: 5,
})

const AwdServices: FC = () => {
  const { id } = useParams()
  const gameId = parseInt(id ?? '-1')
  const modals = useModals()

  const [services, setServices] = useState<AwdpServiceViewModel[]>([])
  const [selectedId, setSelectedId] = useState<number | null>()
  const [draft, setDraft] = useState(defaultService())
  const [status, setStatus] = useState<AwdpGameStatusModel>()
  const [instances, setInstances] = useState<AwdpServiceStatusModel[]>([])
  const [patches, setPatches] = useState<AwdpPatchSubmissionViewModel[]>([])
  const [loading, setLoading] = useState(false)
  const { t } = useTranslation()
  const awd = (key: string, defaultValue: string, options?: Record<string, unknown>) =>
    t(`admin.awd.${key}`, { defaultValue, ...options })
  const statusLabel = (value?: AwdpStatusLike) => (value ? awd(`status_labels.${value}`, String(value)) : undefined)

  const selectedService = useMemo(() => services.find((service) => service.id === selectedId), [services, selectedId])
  const serviceFormInvalid =
    !draft.name.trim() || !draft.imageName.trim() || draft.exposePort < 1 || draft.exposePort > 65535
  const instanceCount = instances.reduce((sum, service) => sum + service.teamStatuses.length, 0)
  const runningInstances = instances.reduce(
    (sum, service) => sum + service.teamStatuses.filter((team) => team.isRunning).length,
    0
  )
  const successfulPatches = patches.filter((patch) => patch.finalStatus === AwdpPatchStatus.ExpFailed).length
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
        const [serviceRes, statusRes, instanceRes, patchRes] = await Promise.all([
          awdpAdminApi.getServices(gameId),
          awdpAdminApi.getStatus(gameId),
          awdpAdminApi.getInstances(gameId),
          awdpAdminApi.getPatches(gameId, 50, 0),
        ])
        setServices(serviceRes.data)
        setStatus(statusRes.data)
        setInstances(instanceRes.data)
        setPatches(patchRes.data.data)
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

  useEffect(() => {
    if (selectedService) {
      setDraft({ ...selectedService })
    }
  }, [selectedService])

  useEffect(() => {
    if (loading) return

    if (selectedId === undefined) {
      if (services.length > 0) {
        setSelectedId(services[0].id)
      }
      return
    }

    if (selectedId === null || services.some((service) => service.id === selectedId)) return

    if (services.length > 0) {
      setSelectedId(services[0].id)
    } else {
      setSelectedId(undefined)
      setDraft(defaultService())
    }
  }, [loading, selectedId, services])

  const onSave = async () => {
    if (serviceFormInvalid || loading) return
    setLoading(true)
    try {
      const res = selectedId
        ? await awdpAdminApi.updateService(selectedId, draft)
        : await awdpAdminApi.createService(gameId, draft)
      showNotification({
        color: 'teal',
        message: selectedId
          ? awd('service_updated', 'AWDP service updated.')
          : awd('service_created', 'AWDP service created.'),
        icon: <Icon path={mdiCheck} size={1} />,
      })
      setSelectedId(res.data.id)
      await load()
    } catch (e) {
      showErrorMsg(e, t)
    } finally {
      setLoading(false)
    }
  }

  const onDelete = () => {
    if (!selectedId) return
    modals.openConfirmModal({
      title: awd('confirm_delete', 'Delete AWDP service'),
      children: (
        <Text>
          {awd('confirm_delete_msg', 'Are you sure you want to delete service {{name}}?', {
            name: selectedService?.name,
          })}
        </Text>
      ),
      confirmProps: { color: 'red' },
      onConfirm: async () => {
        setLoading(true)
        try {
          await awdpAdminApi.deleteService(selectedId)
          setSelectedId(undefined)
          await load()
        } catch (e) {
          showErrorMsg(e, t)
        } finally {
          setLoading(false)
        }
      },
    })
  }

  const runAction = async (action: () => Promise<unknown>, message: string) => {
    if (loading) return
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
    <WithGameEditTab
      isLoading={loading}
      contentPos="right"
      head={
        <Group wrap="wrap" justify="flex-end">
          <Button
            leftSection={<Icon path={mdiPlay} size={1} />}
            disabled={loading}
            onClick={() => runAction(() => awdpAdminApi.startGame(gameId), awd('game_started', 'AWDP started.'))}
          >
            {awd('start_game', 'Start')}
          </Button>
          <Button
            leftSection={<Icon path={mdiStop} size={1} />}
            variant="outline"
            disabled={loading}
            onClick={() => runAction(() => awdpAdminApi.stopGame(gameId), awd('game_stopped', 'AWDP stopped.'))}
          >
            {awd('stop_game', 'Stop')}
          </Button>
          <Button
            leftSection={<Icon path={mdiRefresh} size={1} />}
            variant="subtle"
            disabled={loading}
            onClick={() => void load()}
          >
            {awd('refresh', 'Refresh')}
          </Button>
        </Group>
      }
    >
      <Stack gap="md">
        <YinyuPanel p="md">
          <Group justify="space-between" align="flex-start" wrap="wrap">
            <Stack gap={2}>
              <Title order={4}>
                {awd('current_round_with_number', 'Round {{round}}', { round: status?.currentRound ?? 0 })}
              </Title>
              <Text size="sm" c="dimmed">
                {awd('round_started', 'Started at {{time}}', { time: roundStart })}
              </Text>
            </Stack>
            <AwdpStatusBadge
              status={status?.status}
              fallback={awd('idle', 'Idle')}
              label={statusLabel(status?.status)}
            />
          </Group>
          <SimpleGrid cols={{ base: 1, xs: 2, md: 4 }} mt="md">
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
              icon={mdiServerNetwork}
              color={runningInstances === instanceCount && instanceCount > 0 ? 'teal' : 'yellow'}
              label={awd('running_instances', 'Running instances')}
              value={`${runningInstances}/${instanceCount}`}
              sub={awd('configured_services', '{{count}} services', { count: services.length })}
            />
            <AwdpMetricTile
              icon={mdiPackageUp}
              color="violet"
              label={awd('patch_submissions', 'Patch Submissions')}
              value={patches.length}
              sub={awd('successful_patches', '{{count}} verified', { count: successfulPatches })}
            />
            <AwdpMetricTile
              icon={mdiRefresh}
              color="cyan"
              label={awd('service_defaults', 'Service defaults')}
              value={`${draft.attackPhaseMinutes}/${draft.patchPhaseMinutes}`}
              sub={awd('total_rounds_with_count', '{{count}} rounds', { count: draft.totalRounds })}
            />
          </SimpleGrid>
        </YinyuPanel>

        <YinyuPanel p="md">
          <Stack>
            <Group justify="space-between" align="flex-end" wrap="wrap">
              <Select
                label={awd('service', 'Service')}
                value={selectedId?.toString() ?? null}
                data={services.map((service) => ({ value: service.id.toString(), label: service.name }))}
                onChange={(value) => setSelectedId(value ? Number(value) : null)}
                disabled={loading}
                style={{ flex: '1 1 18rem' }}
              />
              <Group gap="xs" wrap="wrap" justify="flex-end">
                <Button
                  leftSection={<Icon path={mdiPlus} size={1} />}
                  variant="outline"
                  disabled={loading}
                  onClick={() => {
                    setSelectedId(null)
                    setDraft(defaultService())
                  }}
                >
                  {awd('new_service', 'New')}
                </Button>
                <Button
                  leftSection={<Icon path={mdiContentSaveOutline} size={1} />}
                  disabled={loading || serviceFormInvalid}
                  onClick={onSave}
                >
                  {awd('save_changes', 'Save')}
                </Button>
                <Button
                  leftSection={<Icon path={mdiDeleteOutline} size={1} />}
                  color="red"
                  variant="outline"
                  disabled={loading || !selectedId}
                  onClick={onDelete}
                >
                  {awd('delete', 'Delete')}
                </Button>
              </Group>
            </Group>

            <Divider label={awd('basic_config', 'Basic config')} labelPosition="left" />
            <SimpleGrid cols={{ base: 1, md: 3 }}>
              <TextInput
                label={awd('service_name', 'Name')}
                required
                value={draft.name}
                onChange={(e) => setDraft({ ...draft, name: e.currentTarget.value })}
              />
              <TextInput
                label={awd('container_image', 'Image')}
                required
                value={draft.imageName}
                onChange={(e) => setDraft({ ...draft, imageName: e.currentTarget.value })}
              />
              <NumberInput
                label={awd('expose_port', 'Expose port')}
                required
                value={draft.exposePort}
                min={1}
                max={65535}
                onChange={(v) => setDraft({ ...draft, exposePort: Number(v) || 80 })}
              />
            </SimpleGrid>

            <Divider label={awd('scoring_config', 'Scoring')} labelPosition="left" />
            <SimpleGrid cols={{ base: 1, md: 3 }}>
              <NumberInput
                label={awd('original_score', 'Original')}
                value={draft.originalScore}
                min={0}
                onChange={(v) => setDraft({ ...draft, originalScore: Number(v) || 0 })}
              />
              <NumberInput
                label={awd('attack_points', 'Attack')}
                value={draft.attackPoints}
                min={0}
                onChange={(v) => setDraft({ ...draft, attackPoints: Number(v) || 0 })}
              />
              <NumberInput
                label={awd('sla_points', 'SLA')}
                value={draft.slaPoints}
                min={0}
                onChange={(v) => setDraft({ ...draft, slaPoints: Number(v) || 0 })}
              />
              <NumberInput
                label={awd('patch_points', 'Patch')}
                value={draft.patchPoints}
                min={0}
                onChange={(v) => setDraft({ ...draft, patchPoints: Number(v) || 0 })}
              />
              <NumberInput
                label={awd('penalty', 'Penalty')}
                value={draft.serviceAbnormalPenalty}
                min={0}
                onChange={(v) => setDraft({ ...draft, serviceAbnormalPenalty: Number(v) || 0 })}
              />
              <NumberInput
                label={awd('max_attacks', 'Max attacks')}
                value={draft.maxAttackPerRound}
                min={1}
                onChange={(v) => setDraft({ ...draft, maxAttackPerRound: Number(v) || 1 })}
              />
            </SimpleGrid>

            <Divider label={awd('round_config', 'Round config')} labelPosition="left" />
            <SimpleGrid cols={{ base: 1, md: 3 }}>
              <NumberInput
                label={awd('attack_minutes', 'Attack minutes')}
                value={draft.attackPhaseMinutes}
                min={1}
                onChange={(v) => setDraft({ ...draft, attackPhaseMinutes: Number(v) || 1 })}
              />
              <NumberInput
                label={awd('patch_minutes', 'Patch minutes')}
                value={draft.patchPhaseMinutes}
                min={1}
                onChange={(v) => setDraft({ ...draft, patchPhaseMinutes: Number(v) || 1 })}
              />
              <NumberInput
                label={awd('total_rounds', 'Rounds')}
                value={draft.totalRounds}
                min={1}
                onChange={(v) => setDraft({ ...draft, totalRounds: Number(v) || 1 })}
              />
              <NumberInput
                label={awd('resets', 'Resets')}
                value={draft.maxResetCount}
                min={0}
                onChange={(v) => setDraft({ ...draft, maxResetCount: Number(v) || 0 })}
              />
              <NumberInput
                label={awd('recoveries', 'Recoveries')}
                value={draft.maxRecoveryCount}
                min={0}
                onChange={(v) => setDraft({ ...draft, maxRecoveryCount: Number(v) || 0 })}
              />
            </SimpleGrid>

            <Divider label={awd('script_config', 'Checker and Exp')} labelPosition="left" />
            <SimpleGrid cols={{ base: 1, md: 2 }}>
              <Stack>
                <TextInput
                  label={awd('checker_entrypoint', 'Checker entrypoint')}
                  value={draft.checkerEntrypoint ?? ''}
                  onChange={(e) => setDraft({ ...draft, checkerEntrypoint: e.currentTarget.value })}
                />
                <Textarea
                  label={awd('checker_script', 'Checker script')}
                  minRows={6}
                  value={draft.checkerScript ?? ''}
                  onChange={(e) => setDraft({ ...draft, checkerScript: e.currentTarget.value })}
                />
              </Stack>
              <Stack>
                <TextInput
                  label={awd('exp_entrypoint', 'Exp entrypoint')}
                  value={draft.expEntrypoint ?? ''}
                  onChange={(e) => setDraft({ ...draft, expEntrypoint: e.currentTarget.value })}
                />
                <Textarea
                  label={awd('exp_script', 'Exp script')}
                  minRows={6}
                  value={draft.expScript ?? ''}
                  onChange={(e) => setDraft({ ...draft, expScript: e.currentTarget.value })}
                />
              </Stack>
            </SimpleGrid>
          </Stack>
        </YinyuPanel>

        <YinyuTableShell p="md">
          <AwdpSectionTitle
            title={awd('instance_status', 'Instances')}
            extra={<Badge variant="light">{`${runningInstances}/${instanceCount}`}</Badge>}
          />
          <ScrollArea offsetScrollbars>
            <Table highlightOnHover verticalSpacing="sm" miw={980}>
              <Table.Thead>
                <Table.Tr>
                  <Table.Th>{awd('service', 'Service')}</Table.Th>
                  <Table.Th>{awd('team', 'Team')}</Table.Th>
                  <Table.Th>{awd('endpoint', 'Endpoint')}</Table.Th>
                  <Table.Th>{awd('status', 'Status')}</Table.Th>
                  <Table.Th>{awd('checker', 'Checker')}</Table.Th>
                  <Table.Th>{awd('resets', 'Resets')}</Table.Th>
                  <Table.Th>{awd('recoveries', 'Recoveries')}</Table.Th>
                  <Table.Th />
                </Table.Tr>
              </Table.Thead>
              <Table.Tbody>
                {instanceCount === 0 ? (
                  <AwdpEmptyTableRow colSpan={8} text={awd('no_instances', 'No instances')} />
                ) : (
                  instances.flatMap((service) =>
                    service.teamStatuses.map((team) => (
                      <Table.Tr key={`${service.serviceId}-${team.teamId}`}>
                        <Table.Td>{service.serviceName}</Table.Td>
                        <Table.Td>{team.teamName}</Table.Td>
                        <Table.Td>
                          <AwdpEndpointText ip={team.ipAddress} port={team.port} />
                        </Table.Td>
                        <Table.Td>
                          <AwdpInstanceStateBadge
                            running={team.isRunning}
                            runningText={runningText}
                            stoppedText={stoppedText}
                          />
                        </Table.Td>
                        <Table.Td>
                          <AwdpStatusBadge
                            status={team.lastCheckerStatus}
                            label={statusLabel(team.lastCheckerStatus)}
                          />
                        </Table.Td>
                        <Table.Td>{team.remainingResetCount}</Table.Td>
                        <Table.Td>{team.remainingRecoveryCount}</Table.Td>
                        <Table.Td>
                          <Group gap="xs" justify="right" wrap="nowrap">
                            <Button
                              size="xs"
                              variant="outline"
                              disabled={loading}
                              leftSection={<Icon path={mdiRefresh} size={0.75} />}
                              onClick={() =>
                                runAction(
                                  () => awdpAdminApi.resetInstance(team.instanceId),
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
                                runAction(
                                  () => awdpAdminApi.recoverInstance(team.instanceId),
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
                  )
                )}
              </Table.Tbody>
            </Table>
          </ScrollArea>
        </YinyuTableShell>

        <YinyuTableShell p="md">
          <AwdpSectionTitle
            title={awd('patch_submissions', 'Patch Submissions')}
            extra={
              <Badge color="violet" variant="light">
                {patches.length}
              </Badge>
            }
          />
          <ScrollArea offsetScrollbars>
            <Table highlightOnHover verticalSpacing="sm" miw={1040}>
              <Table.Thead>
                <Table.Tr>
                  <Table.Th>{awd('round', 'Round')}</Table.Th>
                  <Table.Th>{awd('service', 'Service')}</Table.Th>
                  <Table.Th>{awd('team', 'Team')}</Table.Th>
                  <Table.Th>{awd('checker', 'Checker')}</Table.Th>
                  <Table.Th>{awd('exp', 'Exp')}</Table.Th>
                  <Table.Th>{awd('result', 'Result')}</Table.Th>
                  <Table.Th>{awd('time', 'Time')}</Table.Th>
                  <Table.Th>{awd('message', 'Message')}</Table.Th>
                </Table.Tr>
              </Table.Thead>
              <Table.Tbody>
                {patches.length === 0 ? (
                  <AwdpEmptyTableRow colSpan={8} text={awd('no_patch_submissions', 'No patch submissions')} />
                ) : (
                  patches.map((patch) => (
                    <Table.Tr key={patch.id}>
                      <Table.Td>{patch.roundNumber}</Table.Td>
                      <Table.Td>{patch.serviceName}</Table.Td>
                      <Table.Td>{patch.teamName}</Table.Td>
                      <Table.Td>
                        <AwdpStatusBadge status={patch.checkerResult} label={statusLabel(patch.checkerResult)} />
                      </Table.Td>
                      <Table.Td>
                        <AwdpStatusBadge status={patch.expResult} label={statusLabel(patch.expResult)} />
                      </Table.Td>
                      <Table.Td>
                        <AwdpStatusBadge status={patch.finalStatus} label={statusLabel(patch.finalStatus)} />
                      </Table.Td>
                      <Table.Td>
                        <Text size="sm" c="dimmed" style={{ fontFamily: 'var(--mantine-font-family-monospace)' }}>
                          {dayjs(patch.submittedAt).format('YYYY-MM-DD HH:mm:ss')}
                        </Text>
                      </Table.Td>
                      <Table.Td>
                        <Text size="sm" truncate style={{ maxWidth: '24rem' }}>
                          {patch.message ?? '-'}
                        </Text>
                      </Table.Td>
                    </Table.Tr>
                  ))
                )}
              </Table.Tbody>
            </Table>
          </ScrollArea>
        </YinyuTableShell>
      </Stack>
    </WithGameEditTab>
  )
}

export default AwdServices
