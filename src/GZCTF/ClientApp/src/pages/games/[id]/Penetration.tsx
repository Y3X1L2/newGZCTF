import { Badge, Button, Group, PasswordInput, SimpleGrid, Stack, Text, Title } from '@mantine/core'
import { showNotification } from '@mantine/notifications'
import { mdiCheck, mdiFlagOutline, mdiRefresh, mdiServerNetwork, mdiShieldSearch, mdiTarget } from '@mdi/js'
import { Icon } from '@mdi/react'
import { FC, useCallback, useEffect, useMemo, useState } from 'react'
import { useParams } from 'react-router'
import { WithGameTab } from '@Components/WithGameTab'
import { WithNavBar } from '@Components/WithNavbar'
import { WithRole } from '@Components/WithRole'
import { YinyuPanel, YinyuRouteLoader, YinyuStatusPill } from '@Components/yinyu/YinyuUI'
import { encryptApiData } from '@Utils/Crypto'
import { showErrorMsg } from '@Utils/Shared'
import { useConfig } from '@Hooks/useConfig'
import { Role } from '@Api'
import {
  PenetrationRuntimeStatus,
  PenetrationWorkspaceModel,
  PenetrationWorkspaceNodeModel,
  PenetrationWorkspaceScoreItemModel,
  PenetrationZoneType,
  penetrationPlayerApi,
} from '../../../Api/PenetrationApi'

const statusLabel = (status: PenetrationRuntimeStatus) =>
  status === PenetrationRuntimeStatus.Running
    ? '运行中'
    : status === PenetrationRuntimeStatus.Failed
      ? '异常'
      : status === PenetrationRuntimeStatus.Stopped
        ? '已停止'
        : '等待部署'

const zoneLabel: Record<PenetrationZoneType, string> = {
  [PenetrationZoneType.Public]: '公网',
  [PenetrationZoneType.Dmz]: 'DMZ',
  [PenetrationZoneType.Business]: '业务区',
  [PenetrationZoneType.Data]: '数据区',
  [PenetrationZoneType.Operations]: '运维区',
  [PenetrationZoneType.Management]: '管理区',
  [PenetrationZoneType.Custom]: '自定义',
}

const TaskCard: FC<{
  node: PenetrationWorkspaceNodeModel
  item: PenetrationWorkspaceScoreItemModel
  locked: boolean
  value: string
  disabled: boolean
  onChange: (value: string) => void
  onSubmit: () => void
}> = ({ node, item, locked, value, disabled, onChange, onSubmit }) => (
  <YinyuPanel p="md" className="yy-pentest-task-card yy-pentest-work-task">
    <Stack gap="sm">
      <Group justify="space-between" align="flex-start" wrap="nowrap">
        <Stack gap={4}>
          <Group gap="xs">
            <Badge variant="light">{item.category || '综合'}</Badge>
            <Badge color={item.solved ? 'teal' : 'gray'} variant="light">
              {item.solved ? '已完成' : `${item.score} 分`}
            </Badge>
            {locked ? (
              <Badge color="yellow" variant="light">
                前置锁定
              </Badge>
            ) : null}
          </Group>
          <Title order={4}>{item.title}</Title>
          <Text className="yy-readable-text" size="sm">
            {node.name} / {node.interfaces.map((item) => `${item.name}:${item.previewIp || item.staticIp || '-'}`).join('  ')}
          </Text>
        </Stack>
        <YinyuStatusPill tone={item.solved ? 'success' : 'neutral'}>{item.solved ? 'Accepted' : 'Open'}</YinyuStatusPill>
      </Group>
      {item.description ? <Text className="yy-readable-text">{item.description}</Text> : null}
      <Group align="flex-end" wrap="nowrap">
        <PasswordInput
          label="提交 Flag"
          value={value}
          disabled={disabled || item.solved || locked}
          onChange={(event) => onChange(event.currentTarget.value)}
          style={{ flex: 1 }}
        />
        <Button leftSection={<Icon path={mdiFlagOutline} size={0.85} />} disabled={disabled || item.solved || locked || !value.trim()} onClick={onSubmit}>
          提交
        </Button>
      </Group>
      <Text className="yy-readable-text" size="xs">
        {locked
          ? `需要先完成 ${item.prerequisiteItemIds.length} 个前置得分项`
          : item.maxAttempts > 0
            ? `已提交 ${item.attempts}/${item.maxAttempts} 次`
            : `已提交 ${item.attempts} 次`}
      </Text>
    </Stack>
  </YinyuPanel>
)

const PenetrationPage: FC = () => {
  const { id } = useParams()
  const gameId = parseInt(id ?? '-1')
  const { config } = useConfig()
  const [workspace, setWorkspace] = useState<PenetrationWorkspaceModel>()
  const [flags, setFlags] = useState<Record<number, string>>({})
  const [loading, setLoading] = useState(false)
  const [errorText, setErrorText] = useState<string>()

  const load = useCallback(async () => {
    if (gameId <= 0) return
    setLoading(true)
    setErrorText(undefined)
    try {
      const res = await penetrationPlayerApi.getWorkspace(gameId)
      setWorkspace(res.data)
    } catch {
      setErrorText('渗透环境尚未部署，或当前队伍暂无访问权限。')
    } finally {
      setLoading(false)
    }
  }, [gameId])

  useEffect(() => {
    void load()
  }, [load])

  const tasks = useMemo(
    () => (workspace?.nodes ?? []).flatMap((node) => node.scoreItems.map((item) => ({ node, item }))),
    [workspace?.nodes]
  )
  const solvedItemIds = useMemo(() => new Set(tasks.filter(({ item }) => item.solved).map(({ item }) => item.id)), [tasks])
  const solvedCount = tasks.filter(({ item }) => item.solved).length
  const runningNodes = workspace?.nodes.filter((node) => node.runtimeStatus === PenetrationRuntimeStatus.Running).length ?? 0
  const remainingReset = workspace ? Math.max(0, workspace.maxResetCount - workspace.resetCount) : 0

  const submit = async (scoreItemId: number) => {
    const raw = flags[scoreItemId]?.trim()
    if (!raw) return
    setLoading(true)
    try {
      const encrypted = await encryptApiData((key) => key, raw, config.apiPublicKey)
      const res = await penetrationPlayerApi.submit(gameId, scoreItemId, encrypted)
      showNotification({
        color: res.data.accepted ? 'teal' : 'yellow',
        message: res.data.accepted ? `提交成功 +${res.data.score}` : res.data.message,
        icon: <Icon path={res.data.accepted ? mdiCheck : mdiShieldSearch} size={1} />,
      })
      setFlags((current) => ({ ...current, [scoreItemId]: '' }))
      await load()
    } catch (err) {
      showErrorMsg(err, (key) => key)
    } finally {
      setLoading(false)
    }
  }

  const reset = async () => {
    setLoading(true)
    try {
      const res = await penetrationPlayerApi.reset(gameId)
      showNotification({ color: 'teal', message: res.data.title, icon: <Icon path={mdiRefresh} size={1} /> })
      await load()
    } catch (err) {
      showErrorMsg(err, (key) => key)
    } finally {
      setLoading(false)
    }
  }

  return (
    <WithNavBar minWidth={0} width="min(100%, calc(100vw - 7.25rem))">
      <WithRole requiredRole={Role.User}>
        <WithGameTab>
          <Stack gap="md" className="yy-pentest-player">
            {loading && !workspace ? (
              <YinyuPanel p="xl">
                <YinyuRouteLoader title="渗透演练" description="正在读取队伍环境与任务链" />
              </YinyuPanel>
            ) : null}

            {errorText && !workspace ? (
              <YinyuPanel p="xl">
                <Stack gap="xs">
                  <Badge variant="light">Penetration</Badge>
                  <Title order={2}>渗透环境暂不可用</Title>
                  <Text className="yy-readable-text">{errorText}</Text>
                  <Button leftSection={<Icon path={mdiRefresh} size={0.85} />} onClick={load}>
                    重新读取
                  </Button>
                </Stack>
              </YinyuPanel>
            ) : null}

            {workspace ? (
              <>
                <YinyuPanel p="md" className="yy-pentest-player-header">
                  <Group justify="space-between" align="center" wrap="wrap">
                    <Stack gap={4}>
                      <Group gap="xs">
                        <Badge variant="light">Penetration Workspace</Badge>
                        <YinyuStatusPill tone={workspace.status === PenetrationRuntimeStatus.Running ? 'success' : 'warm'}>
                          {statusLabel(workspace.status)}
                        </YinyuStatusPill>
                      </Group>
                      <Title order={2}>多网段渗透工作台</Title>
                      <Text className="yy-readable-text">
                        {workspace.teamName} / {runningNodes}/{workspace.nodes.length} 个节点运行 / {solvedCount}/{tasks.length} 个得分项完成
                      </Text>
                    </Stack>
                    <Button leftSection={<Icon path={mdiRefresh} size={0.85} />} variant="light" disabled={loading || remainingReset <= 0} onClick={reset}>
                      重置环境（剩余 {remainingReset}）
                    </Button>
                  </Group>
                </YinyuPanel>

                <div className="yy-pentest-workbench">
                  <Stack gap="md" className="yy-pentest-workbench-left">
                    <YinyuPanel p="md">
                      <Stack gap="sm">
                        <Group gap="xs">
                          <Icon path={mdiTarget} size={1} />
                          <Title order={4}>入口目标</Title>
                        </Group>
                        {workspace.entryPoints.length ? (
                          workspace.entryPoints.map((entry) => (
                            <YinyuPanel key={`${entry.nodeId}-${entry.port}`} p="sm" className="yy-pentest-entry">
                              <Stack gap={4}>
                                <Text fw={800}>{entry.nodeName}</Text>
                                <Badge variant="light" size="lg">
                                  {entry.host}:{entry.port}
                                </Badge>
                                <Text className="yy-readable-text" size="xs">
                                  容器端口 {entry.exposePort}
                                </Text>
                              </Stack>
                            </YinyuPanel>
                          ))
                        ) : (
                          <Text className="yy-readable-text">当前队伍暂无已发布入口。</Text>
                        )}
                      </Stack>
                    </YinyuPanel>
                    <YinyuPanel p="md">
                      <Stack gap="sm">
                        <Group gap="xs">
                          <Icon path={mdiServerNetwork} size={1} />
                          <Title order={4}>安全域</Title>
                        </Group>
                        {workspace.networks.map((network) => (
                          <div key={network.id} className="yy-pentest-work-zone">
                            <span>{zoneLabel[network.zoneType]}</span>
                            <strong>{network.name}</strong>
                            <small>{workspace.nodes.filter((node) => node.networkId === network.id).length} 个资产</small>
                          </div>
                        ))}
                      </Stack>
                    </YinyuPanel>
                  </Stack>

                  <Stack gap="md" className="yy-pentest-workbench-center">
                    {workspace.networks.map((network) => {
                      const nodes = workspace.nodes.filter((node) => node.networkId === network.id)
                      return (
                        <YinyuPanel key={network.id} p="md" className="yy-pentest-network-section">
                          <Stack gap="sm">
                            <Group justify="space-between" wrap="wrap">
                              <Group gap="xs">
                                <Badge variant="light">{zoneLabel[network.zoneType]}</Badge>
                                <Title order={4}>{network.name}</Title>
                              </Group>
                              <Text className="yy-readable-text" size="sm">
                                {nodes.length} 个资产
                              </Text>
                            </Group>
                            <SimpleGrid cols={{ base: 1, xl: 2 }} spacing="md">
                              {nodes.flatMap((node) =>
                                node.scoreItems.map((item) => {
                                  const locked = item.prerequisiteItemIds.some((itemId) => !solvedItemIds.has(itemId))
                                  return (
                                    <TaskCard
                                      key={item.id}
                                      node={node}
                                      item={item}
                                      locked={locked}
                                      value={flags[item.id] ?? ''}
                                      disabled={loading}
                                      onChange={(value) => setFlags((current) => ({ ...current, [item.id]: value }))}
                                      onSubmit={() => submit(item.id)}
                                    />
                                  )
                                })
                              )}
                            </SimpleGrid>
                          </Stack>
                        </YinyuPanel>
                      )
                    })}
                  </Stack>

                  <Stack gap="md" className="yy-pentest-workbench-right">
                    <YinyuPanel p="md">
                      <Stack gap="sm">
                        <Title order={4}>资产状态</Title>
                        {workspace.nodes.map((node) => (
                          <div key={node.id} className="yy-pentest-work-node">
                            <Group justify="space-between" wrap="nowrap">
                              <Stack gap={2}>
                                <Text fw={800}>{node.name}</Text>
                                <Text className="yy-readable-text" size="xs">
                                  {node.interfaces.map((item) => `${item.name}:${item.previewIp || item.staticIp || '-'}`).join(' / ')}
                                </Text>
                              </Stack>
                              <YinyuStatusPill tone={node.runtimeStatus === PenetrationRuntimeStatus.Running ? 'success' : 'neutral'}>
                                {statusLabel(node.runtimeStatus)}
                              </YinyuStatusPill>
                            </Group>
                          </div>
                        ))}
                      </Stack>
                    </YinyuPanel>
                    <YinyuPanel p="md">
                      <Stack gap="sm">
                        <Title order={4}>访问路径</Title>
                        {workspace.policies.length ? (
                          workspace.policies.map((policy) => (
                            <div key={policy.id} className="yy-pentest-work-policy">
                              {workspace.nodes.find((node) => node.id === policy.sourceNodeId)?.name ?? '源节点'}
                              <span>{policy.protocol}/{policy.portRange}</span>
                              {workspace.nodes.find((node) => node.id === policy.targetNodeId)?.name ?? '目标节点'}
                            </div>
                          ))
                        ) : (
                          <Text className="yy-readable-text">暂无显式访问路径。</Text>
                        )}
                      </Stack>
                    </YinyuPanel>
                  </Stack>
                </div>
              </>
            ) : null}
          </Stack>
        </WithGameTab>
      </WithRole>
    </WithNavBar>
  )
}

export default PenetrationPage
