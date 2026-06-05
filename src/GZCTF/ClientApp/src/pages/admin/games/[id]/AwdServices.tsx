import {
  Button,
  Card,
  Group,
  Modal,
  ModalProps,
  NumberInput,
  ScrollArea,
  Stack,
  Table,
  Text,
  TextInput,
  Title,
} from '@mantine/core'
import { useInputState } from '@mantine/hooks'
import { useModals } from '@mantine/modals'
import { notifications, showNotification } from '@mantine/notifications'
import {
  mdiCheck,
  mdiDeleteOutline,
  mdiPencilOutline,
  mdiPlay,
  mdiPlus,
  mdiRefresh,
  mdiSwordCross,
} from '@mdi/js'
import { Icon } from '@mdi/react'
import { FC, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useParams } from 'react-router'
import { WithGameEditTab } from '@Components/admin/WithGameEditTab'
import { showErrorMsg } from '@Utils/Shared'
import {
  awdAdminApi,
  AwdServiceCreateModel,
  AwdServiceViewModel,
  TeamServiceStatus,
  AwdGameStatusModel,
} from '../../../../Api/AwdApi'
import { AwdRoundStatus } from '../../../../Api/AwdApi'

interface AwdServiceModalProps extends ModalProps {
  service?: AwdServiceViewModel
  onServiceSubmit: (data: AwdServiceCreateModel) => void
}

const AwdServiceModal: FC<AwdServiceModalProps> = (props) => {
  const { service, onServiceSubmit, ...modalProps } = props
  const { t } = useTranslation()
  const [disabled, setDisabled] = useState(false)

  const [name, setName] = useInputState(service?.name ?? '')
  const [imageName, setImageName] = useInputState(service?.imageName ?? '')
  const [exposePort, setExposePort] = useState<number | string>(service?.exposePort ?? 80)
  const [attackPoints, setAttackPoints] = useState<number | string>(service?.attackPoints ?? 100)
  const [slaPoints, setSlaPoints] = useState<number | string>(service?.slaPoints ?? 100)
  const [roundDurationMinutes, setRoundDurationMinutes] = useState<number | string>(
    service?.roundDurationMinutes ?? 5
  )
  const [totalRounds, setTotalRounds] = useState<number | string>(service?.totalRounds ?? 50)
  const [checkerScript, setCheckerScript] = useInputState('')
  const [checkerEntrypoint, setCheckerEntrypoint] = useInputState('')

  const handleSubmit = () => {
    if (!name.trim() || !imageName.trim()) return
    setDisabled(true)
    onServiceSubmit({
      name: name.trim(),
      imageName: imageName.trim(),
      exposePort: typeof exposePort === 'string' ? parseInt(exposePort) || 80 : exposePort,
      attackPoints: typeof attackPoints === 'string' ? parseInt(attackPoints) || 0 : attackPoints,
      slaPoints: typeof slaPoints === 'string' ? parseInt(slaPoints) || 0 : slaPoints,
      roundDurationMinutes:
        typeof roundDurationMinutes === 'string'
          ? parseInt(roundDurationMinutes) || 5
          : roundDurationMinutes,
      totalRounds: typeof totalRounds === 'string' ? parseInt(totalRounds) || 50 : totalRounds,
      checkerScript: checkerScript.trim() || null,
      checkerEntrypoint: checkerEntrypoint.trim() || null,
    })
    setDisabled(false)
  }

  return (
    <Modal {...modalProps} title={service ? t('admin.awd.edit_service') : t('admin.awd.new_service')}>
      <Stack>
        <TextInput label={t('admin.awd.service_name')} required placeholder="Service Name" value={name} onChange={setName} />
        <TextInput
          label={t('admin.awd.container_image')}
          required
          placeholder="e.g. nginx:latest"
          value={imageName}
          onChange={setImageName}
        />
        <NumberInput label={t('admin.awd.expose_port')} value={exposePort} onChange={setExposePort} min={1} max={65535} />
        <Group grow>
          <NumberInput label={t('admin.awd.attack_points')} value={attackPoints} onChange={setAttackPoints} min={0} />
          <NumberInput label={t('admin.awd.sla_points')} value={slaPoints} onChange={setSlaPoints} min={0} />
        </Group>
        <Group grow>
          <NumberInput
            label={t('admin.awd.round_duration')}
            value={roundDurationMinutes}
            onChange={setRoundDurationMinutes}
            min={1}
          />
          <NumberInput label={t('admin.awd.total_rounds')} value={totalRounds} onChange={setTotalRounds} min={1} />
        </Group>
        <TextInput
          label={t('admin.awd.checker_script')}
          placeholder="checker.py"
          value={checkerScript}
          onChange={setCheckerScript}
        />
        <TextInput
          label={t('admin.awd.checker_entrypoint')}
          placeholder="python3 checker.py"
          value={checkerEntrypoint}
          onChange={setCheckerEntrypoint}
        />
        <Button fullWidth disabled={disabled} onClick={handleSubmit}>
          {service ? t('admin.awd.save_changes') : t('admin.awd.create_service')}
        </Button>
      </Stack>
    </Modal>
  )
}

const AwdServices: FC = () => {
  const { id } = useParams()
  const numId = parseInt(id ?? '-1')
  const { t } = useTranslation()
  const modals = useModals()

  const [services, setServices] = useState<AwdServiceViewModel[]>([])
  const [instances, setInstances] = useState<TeamServiceStatus[]>([])
  const [status, setStatus] = useState<AwdGameStatusModel | undefined>()
  const [loading, setLoading] = useState(false)
  const [modalOpened, setModalOpened] = useState(false)
  const [editingService, setEditingService] = useState<AwdServiceViewModel | undefined>()

  const fetchServices = async () => {
    if (numId < 0) return
    try {
      const res = await awdAdminApi.getServices(numId)
      setServices(res.data ?? [])
    } catch (err) {
      showErrorMsg(err, t)
    }
  }

  const fetchInstances = async () => {
    if (numId < 0) return
    try {
      const res = await awdAdminApi.getInstances(numId)
      setInstances(res.data ?? [])
    } catch (err) {
      showErrorMsg(err, t)
    }
  }

  const fetchStatus = async () => {
    if (numId < 0) return
    try {
      const res = await awdAdminApi.getGameStatus(numId)
      setStatus(res.data)
    } catch (err) {
      showErrorMsg(err, t)
    }
  }

  const fetchAll = async () => {
    setLoading(true)
    await Promise.all([fetchServices(), fetchInstances(), fetchStatus()])
    setLoading(false)
  }

  useEffect(() => {
    fetchAll()
  }, [numId])

  const onCreateService = async (data: AwdServiceCreateModel) => {
    setLoading(true)
    try {
      await awdAdminApi.createService(numId, data)
      showNotification({
        color: 'teal',
        message: t('admin.awd.service_created'),
        icon: <Icon path={mdiCheck} size={1} />,
      })
      setModalOpened(false)
      fetchServices()
    } catch (err) {
      showErrorMsg(err, t)
    } finally {
      setLoading(false)
    }
  }

  const onUpdateService = async (serviceId: number, data: AwdServiceCreateModel) => {
    setLoading(true)
    try {
      await awdAdminApi.updateService(serviceId, data)
      showNotification({
        color: 'teal',
        message: t('admin.awd.service_updated'),
        icon: <Icon path={mdiCheck} size={1} />,
      })
      setModalOpened(false)
      setEditingService(undefined)
      fetchServices()
    } catch (err) {
      showErrorMsg(err, t)
    } finally {
      setLoading(false)
    }
  }

  const onDeleteService = (service: AwdServiceViewModel) => {
    modals.openConfirmModal({
      title: t('admin.awd.confirm_delete'),
      children: <Text size="sm">{t('admin.awd.confirm_delete_msg', { name: service.name })}</Text>,
      onConfirm: async () => {
        setLoading(true)
        try {
          await awdAdminApi.deleteService(service.id)
          showNotification({
            color: 'teal',
            message: t('admin.awd.service_deleted'),
            icon: <Icon path={mdiCheck} size={1} />,
          })
          fetchServices()
        } catch (err) {
          showErrorMsg(err, t)
        } finally {
          setLoading(false)
        }
      },
      confirmProps: { color: 'red' },
    })
  }

  const onStartGame = async () => {
    setLoading(true)
    try {
      await awdAdminApi.startGame(numId)
      showNotification({
        color: 'teal',
        message: t('admin.awd.game_started'),
        icon: <Icon path={mdiCheck} size={1} />,
      })
      fetchStatus()
    } catch (err) {
      showErrorMsg(err, t)
    } finally {
      setLoading(false)
    }
  }

  const onStopGame = async () => {
    setLoading(true)
    try {
      await awdAdminApi.stopGame(numId)
      showNotification({
        color: 'teal',
        message: t('admin.awd.game_stopped'),
        icon: <Icon path={mdiCheck} size={1} />,
      })
      fetchStatus()
    } catch (err) {
      showErrorMsg(err, t)
    } finally {
      setLoading(false)
    }
  }

  const onResetInstance = (instanceId: number) => {
    modals.openConfirmModal({
      title: t('admin.awd.confirm_reset'),
      children: <Text size="sm">{t('admin.awd.confirm_reset_msg')}</Text>,
      onConfirm: async () => {
        setLoading(true)
        try {
          await awdAdminApi.resetInstance(instanceId)
          showNotification({
            color: 'teal',
            message: t('admin.awd.instance_reset'),
            icon: <Icon path={mdiCheck} size={1} />,
          })
          fetchInstances()
        } catch (err) {
          showErrorMsg(err, t)
        } finally {
          setLoading(false)
        }
      },
      confirmProps: { color: 'orange' },
    })
  }

  const isRunning = status?.status === AwdRoundStatus.Running

  return (
    <WithGameEditTab
      headProps={{ justify: 'apart' }}
      isLoading={loading && services.length === 0}
      head={
        <Group justify="right">
          <Button
            leftSection={<Icon path={mdiRefresh} size={1} />}
            disabled={loading}
            onClick={fetchAll}
            variant="outline"
          >
            {t('admin.awd.refresh')}
          </Button>
          {isRunning ? (
            <Button
              leftSection={<Icon path={mdiDeleteOutline} size={1} />}
              disabled={loading}
              onClick={onStopGame}
              color="red"
              variant="outline"
            >
              {t('admin.awd.stop_game')}
            </Button>
          ) : (
            <Button
              leftSection={<Icon path={mdiPlay} size={1} />}
              disabled={loading || services.length === 0}
              onClick={onStartGame}
              color="teal"
            >
              {t('admin.awd.start_game')}
            </Button>
          )}
          <Button
            mr="18px"
            leftSection={<Icon path={mdiPlus} size={1} />}
            onClick={() => {
              setEditingService(undefined)
              setModalOpened(true)
            }}
          >
            {t('admin.awd.new_service')}
          </Button>
        </Group>
      }
    >
      <ScrollArea h="calc(100vh - 180px)" pos="relative" offsetScrollbars type="auto">
        <Stack gap="md">
          <Card withBorder>
            <Group justify="space-between">
              <Stack gap={0}>
                <Text fw="bold" size="lg">
                  {t('admin.awd.game_status')}
                </Text>
                <Text size="sm" c="dimmed">
                  {t('admin.awd.current_round')}: {status?.currentRound ?? 0} /{' '}
                  {services[0]?.totalRounds ?? '-'}
                </Text>
              </Stack>
              <Text fw="bold" size="xl" c={isRunning ? 'teal' : 'dimmed'}>
                {isRunning ? t('admin.awd.running') : status?.status === AwdRoundStatus.Finished ? t('admin.awd.finished') : t('admin.awd.preparing')}
              </Text>
            </Group>
          </Card>

          <Title order={4}>{t('admin.awd.service_list')}</Title>
          {services.length === 0 ? (
            <Text c="dimmed" ta="center">
              {t('admin.awd.no_services')}
            </Text>
          ) : (
            <Table striped highlightOnHover>
              <Table.Thead>
                <Table.Tr>
                  <Table.Th>{t('admin.awd.name')}</Table.Th>
                  <Table.Th>{t('admin.awd.image')}</Table.Th>
                  <Table.Th>{t('admin.awd.port')}</Table.Th>
                  <Table.Th>{t('admin.awd.attack_score')}</Table.Th>
                  <Table.Th>{t('admin.awd.sla_score')}</Table.Th>
                  <Table.Th>{t('admin.awd.round_duration_col')}</Table.Th>
                  <Table.Th>{t('admin.awd.total_rounds_col')}</Table.Th>
                  <Table.Th>{t('admin.awd.actions')}</Table.Th>
                </Table.Tr>
              </Table.Thead>
              <Table.Tbody>
                {services.map((service) => (
                  <Table.Tr key={service.id}>
                    <Table.Td>{service.name}</Table.Td>
                    <Table.Td>
                      <Text size="sm" ff="mono">
                        {service.imageName}
                      </Text>
                    </Table.Td>
                    <Table.Td>{service.exposePort}</Table.Td>
                    <Table.Td>{service.attackPoints}</Table.Td>
                    <Table.Td>{service.slaPoints}</Table.Td>
                    <Table.Td>{service.roundDurationMinutes} min</Table.Td>
                    <Table.Td>{service.totalRounds}</Table.Td>
                    <Table.Td>
                      <Group gap="xs">
                        <Button
                          variant="subtle"
                          size="compact-sm"
                          leftSection={<Icon path={mdiPencilOutline} size={0.8} />}
                          onClick={() => {
                            setEditingService(service)
                            setModalOpened(true)
                          }}
                        >
                          {t('admin.awd.edit')}
                        </Button>
                        <Button
                          variant="subtle"
                          size="compact-sm"
                          color="red"
                          leftSection={<Icon path={mdiDeleteOutline} size={0.8} />}
                          onClick={() => onDeleteService(service)}
                        >
                          {t('admin.awd.delete')}
                        </Button>
                      </Group>
                    </Table.Td>
                  </Table.Tr>
                ))}
              </Table.Tbody>
            </Table>
          )}

          <Title order={4}>{t('admin.awd.instance_status')}</Title>
          {instances.length === 0 ? (
            <Text c="dimmed" ta="center">
              {t('admin.awd.no_instances')}
            </Text>
          ) : (
            <Table striped highlightOnHover>
              <Table.Thead>
                <Table.Tr>
                  <Table.Th>{t('admin.awd.team')}</Table.Th>
                  <Table.Th>{t('admin.awd.address')}</Table.Th>
                  <Table.Th>{t('admin.awd.status')}</Table.Th>
                  <Table.Th>{t('admin.awd.actions')}</Table.Th>
                </Table.Tr>
              </Table.Thead>
              <Table.Tbody>
                {instances.map((instance) => (
                  <Table.Tr key={instance.teamId}>
                    <Table.Td>{instance.teamName}</Table.Td>
                    <Table.Td>
                      <Text size="sm" ff="mono">
                        {instance.ipAddress}
                        {instance.port ? `:${instance.port}` : ''}
                      </Text>
                    </Table.Td>
                    <Table.Td>
                      <Text c={instance.isRunning ? 'teal' : 'red'}>
                        {instance.isRunning ? t('admin.awd.running_status') : t('admin.awd.stopped')}
                      </Text>
                    </Table.Td>
                    <Table.Td>
                      <Button
                        variant="subtle"
                        size="compact-sm"
                        color="orange"
                        disabled={!instance.instanceId}
                        onClick={() => instance.instanceId && onResetInstance(instance.instanceId)}
                      >
                        {t('admin.awd.reset')}
                      </Button>
                    </Table.Td>
                  </Table.Tr>
                ))}
              </Table.Tbody>
            </Table>
          )}
        </Stack>
      </ScrollArea>

      <AwdServiceModal
        opened={modalOpened}
        onClose={() => {
          setModalOpened(false)
          setEditingService(undefined)
        }}
        service={editingService}
        onServiceSubmit={(data) => {
          if (editingService) {
            onUpdateService(editingService.id, data)
          } else {
            onCreateService(data)
          }
        }}
      />
    </WithGameEditTab>
  )
}

export default AwdServices
