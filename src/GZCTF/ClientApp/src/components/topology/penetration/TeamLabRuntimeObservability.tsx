import { Button, Group, NumberInput, Select, Stack, Table, Text, Title } from '@mantine/core'
import { showNotification } from '@mantine/notifications'
import { mdiDownload, mdiDownloadNetworkOutline, mdiRefresh, mdiStop } from '@mdi/js'
import { Icon } from '@mdi/react'
import { FC, useMemo, useState } from 'react'
import { showErrorMsg } from '@Utils/Shared'
import { YinyuStatusPill, YinyuTableShell } from '@Components/yinyu/YinyuUI'
import {
  PenetrationDeploymentEventLevel,
  PenetrationDeploymentEventModel,
  PenetrationEnforcementMode,
  PenetrationRuntimeStatus,
  PenetrationSubmissionLogModel,
  PenetrationTeamEnvironmentModel,
  PenetrationRouteStatus,
  TeamLabRuntimeStatus,
  TeamLabTrafficCaptureStatus,
  penetrationAdminApi,
} from '@Api/PenetrationApi'

type Props = {
  gameId: number
  maxResetCount: number
  environments: PenetrationTeamEnvironmentModel[]
  submissions: PenetrationSubmissionLogModel[]
  deploymentEvents: PenetrationDeploymentEventModel[]
  deploymentEventTotal: number
  deploymentEventPage: number
  onRefresh: () => Promise<void> | void
  onLoadDeploymentEvents: (page: number) => Promise<void> | void
  onCleanupTeam: (env: PenetrationTeamEnvironmentModel) => void
  onRestartRuntimeNode: (runtimeNodeId: number, teamName: string, nodeName: string) => void
}

const runtimeStatusLabels: Record<PenetrationRuntimeStatus, string> = {
  [PenetrationRuntimeStatus.Pending]: '等待部署',
  [PenetrationRuntimeStatus.CreatingNetworks]: '创建网络',
  [PenetrationRuntimeStatus.CreatingContainers]: '创建容器',
  [PenetrationRuntimeStatus.Running]: '运行中',
  [PenetrationRuntimeStatus.Stopped]: '已停止',
  [PenetrationRuntimeStatus.Failed]: '部署失败',
  [PenetrationRuntimeStatus.CleanupPending]: '待清理',
  [PenetrationRuntimeStatus.Orphaned]: '资源孤儿',
  [PenetrationRuntimeStatus.ManualCleanupRequired]: '需人工清理',
}

const teamLabStatusLabels: Record<TeamLabRuntimeStatus, string> = {
  [TeamLabRuntimeStatus.Pending]: '等待',
  [TeamLabRuntimeStatus.Planning]: '规划中',
  [TeamLabRuntimeStatus.Scheduled]: '已调度',
  [TeamLabRuntimeStatus.Deploying]: '部署中',
  [TeamLabRuntimeStatus.Probing]: '探测中',
  [TeamLabRuntimeStatus.Running]: '运行中',
  [TeamLabRuntimeStatus.Failed]: '失败',
  [TeamLabRuntimeStatus.CleanupPending]: '待清理',
  [TeamLabRuntimeStatus.Stopped]: '已停止',
  [TeamLabRuntimeStatus.Destroying]: '销毁中',
  [TeamLabRuntimeStatus.Destroyed]: '已销毁',
}

const captureStatusLabels: Record<TeamLabTrafficCaptureStatus, string> = {
  [TeamLabTrafficCaptureStatus.Pending]: '等待',
  [TeamLabTrafficCaptureStatus.Running]: '采集中',
  [TeamLabTrafficCaptureStatus.Stopping]: '停止中',
  [TeamLabTrafficCaptureStatus.Completed]: '已完成',
  [TeamLabTrafficCaptureStatus.Failed]: '失败',
  [TeamLabTrafficCaptureStatus.Expired]: '已过期',
}

const routeStatusLabels: Record<PenetrationRouteStatus, string> = {
  [PenetrationRouteStatus.HintOnly]: '提示路径',
  [PenetrationRouteStatus.RoutePlanned]: '路由可部署',
  [PenetrationRouteStatus.RouteApplied]: '路由已应用',
  [PenetrationRouteStatus.RouteFailed]: '路由失败',
  [PenetrationRouteStatus.Unsupported]: '暂不支持',
}

const deploymentEventLabel: Record<PenetrationDeploymentEventLevel, string> = {
  [PenetrationDeploymentEventLevel.Info]: '信息',
  [PenetrationDeploymentEventLevel.Success]: '成功',
  [PenetrationDeploymentEventLevel.Warning]: '警告',
  [PenetrationDeploymentEventLevel.Error]: '失败',
}

const runtimeStatusTone = (status: PenetrationRuntimeStatus | TeamLabRuntimeStatus | TeamLabTrafficCaptureStatus) => {
  if (status === PenetrationRuntimeStatus.Running || status === TeamLabRuntimeStatus.Running || status === TeamLabTrafficCaptureStatus.Completed) return 'success'
  if (status === PenetrationRuntimeStatus.Failed || status === PenetrationRuntimeStatus.ManualCleanupRequired || status === TeamLabRuntimeStatus.Failed || status === TeamLabTrafficCaptureStatus.Failed) return 'danger'
  if (
    status === PenetrationRuntimeStatus.CreatingNetworks ||
    status === PenetrationRuntimeStatus.CreatingContainers ||
    status === PenetrationRuntimeStatus.CleanupPending ||
    status === TeamLabRuntimeStatus.Deploying ||
    status === TeamLabRuntimeStatus.Probing ||
    status === TeamLabRuntimeStatus.Destroying ||
    status === TeamLabTrafficCaptureStatus.Running ||
    status === TeamLabTrafficCaptureStatus.Stopping
  ) return 'warm'
  return 'neutral'
}

const runtimeStatusState = (status: PenetrationRuntimeStatus | TeamLabRuntimeStatus | TeamLabTrafficCaptureStatus) => {
  if (status === PenetrationRuntimeStatus.Running || status === TeamLabRuntimeStatus.Running || status === TeamLabTrafficCaptureStatus.Running) return 'running'
  if (runtimeStatusTone(status) === 'danger') return 'alert'
  if (runtimeStatusTone(status) === 'warm') return 'busy'
  return 'idle'
}

const routeStatusTone = (status: PenetrationRouteStatus) => {
  if (status === PenetrationRouteStatus.RouteApplied || status === PenetrationRouteStatus.RoutePlanned) return 'success'
  if (status === PenetrationRouteStatus.RouteFailed || status === PenetrationRouteStatus.Unsupported) return 'danger'
  return 'neutral'
}

const deploymentEventTone = (level: PenetrationDeploymentEventLevel) => {
  if (level === PenetrationDeploymentEventLevel.Success) return 'success'
  if (level === PenetrationDeploymentEventLevel.Error) return 'danger'
  if (level === PenetrationDeploymentEventLevel.Warning) return 'warm'
  return 'neutral'
}

const enforcementLabel = (mode: PenetrationEnforcementMode) => {
  if (mode === PenetrationEnforcementMode.RuntimeRoute) return '运行期网络路由'
  if (mode === PenetrationEnforcementMode.Both) return '提示 + 运行期路由'
  return '提示路径'
}

const needsManualCleanup = (status: PenetrationRuntimeStatus) =>
  status === PenetrationRuntimeStatus.CleanupPending ||
  status === PenetrationRuntimeStatus.Orphaned ||
  status === PenetrationRuntimeStatus.ManualCleanupRequired

const formatDateTime = (value?: number | null) => value ? new Date(value).toLocaleString() : '-'
const shortText = (value?: string | null, length = 12) => {
  if (!value) return '-'
  return value.length <= length ? value : `${value.slice(0, length)}...`
}

const formatBytes = (value?: number | null) => {
  const bytes = Number(value ?? 0)
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
  if (bytes < 1024 * 1024 * 1024) return `${(bytes / 1024 / 1024).toFixed(1)} MB`
  return `${(bytes / 1024 / 1024 / 1024).toFixed(2)} GB`
}

const parseRuntimeInterfaces = (summary?: string | null): { interfaceName?: string; networkName?: string; ipAddress?: string; cidr?: string; isPrimary?: boolean }[] => {
  if (!summary) return []
  try {
    const value = JSON.parse(summary)
    return Array.isArray(value) ? value : []
  } catch {
    return []
  }
}

export const TeamLabRuntimeObservability: FC<Props> = ({
  gameId,
  maxResetCount,
  environments,
  submissions,
  deploymentEvents,
  deploymentEventTotal,
  deploymentEventPage,
  onRefresh,
  onLoadDeploymentEvents,
  onCleanupTeam,
  onRestartRuntimeNode,
}) => {
  const [captureTeamId, setCaptureTeamId] = useState<number | null>(null)
  const [captureNetworkKey, setCaptureNetworkKey] = useState<string | null>(null)
  const [captureSeconds, setCaptureSeconds] = useState(300)
  const [captureMaxMb, setCaptureMaxMb] = useState(256)
  const [captureBusy, setCaptureBusy] = useState(false)
  const [flowBusy, setFlowBusy] = useState(false)
  const runtimeNodeRows = useMemo(
    () => environments.flatMap((env) => env.runtimeNodes.map((node) => ({ env, node }))),
    [environments]
  )
  const runtimeRouteRows = useMemo(
    () => environments.flatMap((env) => (env.runtimeRoutes ?? []).map((route) => ({ env, route }))),
    [environments]
  )
  const shardRows = useMemo(
    () => environments.flatMap((env) => (env.teamLabShards ?? []).map((shard) => ({ env, shard }))),
    [environments]
  )
  const networkRows = useMemo(
    () => environments.flatMap((env) => (env.teamLabNetworks ?? []).map((network) => ({ env, network }))),
    [environments]
  )
  const assetRows = useMemo(
    () => environments.flatMap((env) => (env.teamLabAssets ?? []).map((asset) => ({ env, asset }))),
    [environments]
  )
  const captureRows = useMemo(
    () => environments.flatMap((env) => (env.teamLabCaptureJobs ?? []).map((job) => ({ env, job }))),
    [environments]
  )
  const flowRows = useMemo(
    () => environments.flatMap((env) => (env.teamLabTrafficFlows ?? []).map((flow) => ({ env, flow }))),
    [environments]
  )
  const selectedCaptureEnv = environments.find((env) => env.teamId === captureTeamId) ?? environments[0]
  const selectedCaptureNetworks = selectedCaptureEnv?.teamLabNetworks ?? []

  const startCapture = async () => {
    const effectiveNetworkKey = captureNetworkKey ?? selectedCaptureNetworks[0]?.topologyKey
    if (!selectedCaptureEnv || !effectiveNetworkKey) return
    setCaptureBusy(true)
    try {
      const res = await penetrationAdminApi.startTeamLabCapture(gameId, selectedCaptureEnv.teamId, {
        networkTopologyKey: effectiveNetworkKey,
        shardId: null,
        maxSeconds: captureSeconds,
        maxBytes: captureMaxMb * 1024 * 1024,
        retentionSeconds: 86400,
      })
      showNotification({ color: res.data.success ? 'teal' : 'red', message: res.data.message })
      await onRefresh()
    } catch (err) {
      showErrorMsg(err, (key) => key)
    } finally {
      setCaptureBusy(false)
    }
  }

  const stopCapture = async (teamId: number, jobId: number) => {
    setCaptureBusy(true)
    try {
      const res = await penetrationAdminApi.stopTeamLabCapture(gameId, teamId, jobId)
      showNotification({ color: res.data.success ? 'teal' : 'red', message: res.data.message })
      await onRefresh()
    } catch (err) {
      showErrorMsg(err, (key) => key)
    } finally {
      setCaptureBusy(false)
    }
  }

  const refreshCapture = async (teamId: number, jobId: number) => {
    setCaptureBusy(true)
    try {
      const res = await penetrationAdminApi.refreshTeamLabCapture(gameId, teamId, jobId)
      showNotification({ color: res.data.success ? 'teal' : 'red', message: res.data.message })
      await onRefresh()
    } catch (err) {
      showErrorMsg(err, (key) => key)
    } finally {
      setCaptureBusy(false)
    }
  }

  const downloadCapture = (teamId: number, jobId: number) => {
    window.open(penetrationAdminApi.getTeamLabCaptureDownloadUrl(gameId, teamId, jobId), '_blank', 'noopener,noreferrer')
  }

  const refreshFlows = async () => {
    if (!selectedCaptureEnv) return
    setFlowBusy(true)
    try {
      const res = await penetrationAdminApi.refreshTeamLabFlows(gameId, selectedCaptureEnv.teamId)
      showNotification({ color: res.data.success ? 'teal' : 'red', message: res.data.message })
      await onRefresh()
    } catch (err) {
      showErrorMsg(err, (key) => key)
    } finally {
      setFlowBusy(false)
    }
  }

  return (
    <Stack gap="sm">
      <Group justify="space-between" align="flex-start">
        <Stack gap={2}>
          <Title order={4}>发布与运行</Title>
          <Text size="xs" className="yy-readable-text">队伍环境、分片节点、真实网段、资产事实、抓包任务和部署事件。</Text>
        </Stack>
        <Button size="xs" variant="light" leftSection={<Icon path={mdiRefresh} size={0.75} />} onClick={() => void onRefresh()}>
          刷新
        </Button>
      </Group>

      <YinyuTableShell p="xs">
        <Table>
          <Table.Thead>
            <Table.Tr>
              <Table.Th>队伍环境</Table.Th>
              <Table.Th>状态</Table.Th>
              <Table.Th>部署版本</Table.Th>
              <Table.Th>清理与错误</Table.Th>
              <Table.Th>操作</Table.Th>
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {environments.length ? environments.map((env) => (
              <Table.Tr key={env.environmentId}>
                <Table.Td>
                  <Stack gap={2}>
                    <Text fw={900}>{env.teamName}</Text>
                    <Text size="xs" className="yy-readable-text">队伍序号 #{env.teamIndex} · {env.networkPrefix || '未分配网段'}</Text>
                    <Text size="xs" className="yy-readable-text">主节点：{env.workerNodeName ?? '未调度节点'} · 分片 {env.teamLabShards?.length ?? 0}</Text>
                  </Stack>
                </Table.Td>
                <Table.Td>
                  <YinyuStatusPill tone={runtimeStatusTone(env.status)} state={runtimeStatusState(env.status)}>
                    {runtimeStatusLabels[env.status] ?? env.status}
                  </YinyuStatusPill>
                </Table.Td>
                <Table.Td>
                  <Stack gap={2}>
                    <Text size="sm">v{env.publishedVersion} · {env.runtimeNodeCount} 个兼容节点</Text>
                    <Text size="xs" className="yy-readable-text">资产事实 {env.teamLabAssets?.length ?? 0} · 重置 {env.resetCount}/{maxResetCount}</Text>
                    <Text size="xs" className="yy-readable-text">更新：{formatDateTime(env.updatedAt ?? env.createdAt)}</Text>
                  </Stack>
                </Table.Td>
                <Table.Td>
                  <Stack gap={2}>
                    <Text size="sm">{env.cleanupRetryCount > 0 ? `清理已重试 ${env.cleanupRetryCount} 次` : '暂无残留清理任务'}</Text>
                    {env.nextCleanupAt && <Text size="xs" className="yy-readable-text">下次清理：{formatDateTime(env.nextCleanupAt)}</Text>}
                    {env.lastError && <Text size="xs" c="red.3" lineClamp={3}>{env.lastError}</Text>}
                  </Stack>
                </Table.Td>
                <Table.Td>
                  {needsManualCleanup(env.status) ? (
                    <Button size="compact-xs" variant="light" color="red" onClick={() => onCleanupTeam(env)}>
                      重新清理残留
                    </Button>
                  ) : (
                    <Text size="xs" className="yy-readable-text">无待处理操作</Text>
                  )}
                </Table.Td>
              </Table.Tr>
            )) : (
              <Table.Tr><Table.Td colSpan={5}><Text size="sm" className="yy-readable-text">暂无队伍环境。发布并部署后会在这里显示。</Text></Table.Td></Table.Tr>
            )}
          </Table.Tbody>
        </Table>
      </YinyuTableShell>

      <YinyuTableShell p="xs">
        <Group justify="space-between" mb="xs">
          <Text fw={900}>分片 / WorkerNode</Text>
          <Text size="xs" className="yy-readable-text">共 {shardRows.length} 个运行分片</Text>
        </Group>
        <Table>
          <Table.Thead><Table.Tr><Table.Th>队伍 / 节点</Table.Th><Table.Th>状态</Table.Th><Table.Th>网段</Table.Th><Table.Th>资产</Table.Th><Table.Th>路由版本</Table.Th></Table.Tr></Table.Thead>
          <Table.Tbody>
            {shardRows.length ? shardRows.map(({ env, shard }) => (
              <Table.Tr key={`${env.environmentId}-shard-${shard.id}`}>
                <Table.Td>
                  <Stack gap={2}>
                    <Text size="sm" fw={800}>{env.teamName} / {shard.workerNodeName || shortText(shard.workerNodeId)}</Text>
                    <Text size="xs" className="yy-readable-text">{shortText(shard.workerNodeId, 18)}</Text>
                  </Stack>
                </Table.Td>
                <Table.Td>
                  <YinyuStatusPill tone={runtimeStatusTone(shard.status)} state={runtimeStatusState(shard.status)}>
                    {teamLabStatusLabels[shard.status] ?? shard.status}
                  </YinyuStatusPill>
                </Table.Td>
                <Table.Td>{shard.networkKeys.length ? shard.networkKeys.join(', ') : '-'}</Table.Td>
                <Table.Td>{shard.assetKeys.length ? shard.assetKeys.join(', ') : '-'}</Table.Td>
                <Table.Td>
                  <Stack gap={2}>
                    <Text size="xs">v{shard.routeVersion}</Text>
                    {shard.lastError && <Text size="xs" c="red.3" lineClamp={2}>{shard.lastError}</Text>}
                  </Stack>
                </Table.Td>
              </Table.Tr>
            )) : (
              <Table.Tr><Table.Td colSpan={5}><Text size="sm" className="yy-readable-text">暂无分片记录。</Text></Table.Td></Table.Tr>
            )}
          </Table.Tbody>
        </Table>
      </YinyuTableShell>

      <YinyuTableShell p="xs">
        <Group justify="space-between" mb="xs">
          <Text fw={900}>网段 / 资产归属</Text>
          <Text size="xs" className="yy-readable-text">{networkRows.length} 个网段，{assetRows.length} 个资产事实</Text>
        </Group>
        <Table>
          <Table.Thead><Table.Tr><Table.Th>队伍</Table.Th><Table.Th>网段</Table.Th><Table.Th>CIDR / 网关</Table.Th><Table.Th>运行节点</Table.Th><Table.Th>资产</Table.Th></Table.Tr></Table.Thead>
          <Table.Tbody>
            {networkRows.length ? networkRows.map(({ env, network }) => {
              const assets = env.teamLabAssets?.filter((asset) => asset.networkKey === network.topologyKey) ?? []
              return (
                <Table.Tr key={`${env.environmentId}-network-${network.id}`}>
                  <Table.Td>{env.teamName}</Table.Td>
                  <Table.Td>
                    <Stack gap={2}>
                      <Text size="sm" fw={800}>{network.name}</Text>
                      <Text size="xs" className="yy-readable-text">{network.topologyKey} · shard {network.shardId ?? '-'}</Text>
                    </Stack>
                  </Table.Td>
                  <Table.Td>
                    <Text size="xs" className="yy-readable-text">{network.cidr || '-'}</Text>
                    <Text size="xs" className="yy-readable-text">GW {network.gatewayIp || '-'}</Text>
                  </Table.Td>
                  <Table.Td>{network.workerNodeName || shortText(network.workerNodeId, 18)}</Table.Td>
                  <Table.Td>
                    <Stack gap={2}>
                      {assets.length ? assets.map((asset) => (
                        <Text size="xs" className="yy-readable-text" key={asset.id}>
                          {asset.name} · {asset.ipAddress ?? '-'} · {teamLabStatusLabels[asset.status] ?? asset.status}
                        </Text>
                      )) : <Text size="xs" className="yy-readable-text">无资产</Text>}
                    </Stack>
                  </Table.Td>
                </Table.Tr>
              )
            }) : (
              <Table.Tr><Table.Td colSpan={5}><Text size="sm" className="yy-readable-text">暂无网段运行事实。</Text></Table.Td></Table.Tr>
            )}
          </Table.Tbody>
        </Table>
      </YinyuTableShell>

      <YinyuTableShell p="xs">
        <Group justify="space-between" mb="xs" align="flex-end">
          <Stack gap={2}>
            <Text fw={900}>流量抓包</Text>
            <Text size="xs" className="yy-readable-text">抓包点位于 TeamLab 内网侧；PCAP 按需取证，流量元数据可手动刷新。</Text>
          </Stack>
          <Button
            size="compact-xs"
            variant="light"
            leftSection={<Icon path={mdiRefresh} size={0.7} />}
            disabled={flowBusy || !selectedCaptureEnv}
            onClick={() => void refreshFlows()}
          >
            刷新元数据
          </Button>
        </Group>
        <div className="yy-teamlab-capture-form">
          <Select
            label="队伍"
            data={environments.map((env) => ({ value: String(env.teamId), label: env.teamName }))}
            value={String(selectedCaptureEnv?.teamId ?? '')}
            onChange={(value) => {
              const nextTeamId = value ? Number(value) : null
              setCaptureTeamId(nextTeamId)
              const nextEnv = environments.find((env) => env.teamId === nextTeamId)
              setCaptureNetworkKey(nextEnv?.teamLabNetworks?.[0]?.topologyKey ?? null)
            }}
          />
          <Select
            label="网段"
            data={selectedCaptureNetworks.map((network) => ({ value: network.topologyKey, label: `${network.name} (${network.cidr})` }))}
            value={captureNetworkKey ?? selectedCaptureNetworks[0]?.topologyKey ?? null}
            onChange={setCaptureNetworkKey}
          />
          <NumberInput label="秒数" min={10} max={86400} value={captureSeconds} onChange={(value) => setCaptureSeconds(Number(value || 300))} />
          <NumberInput label="上限 MB" min={1} max={10240} value={captureMaxMb} onChange={(value) => setCaptureMaxMb(Number(value || 256))} />
          <Button
            leftSection={<Icon path={mdiDownloadNetworkOutline} size={0.8} />}
            disabled={captureBusy || !selectedCaptureEnv || !(captureNetworkKey ?? selectedCaptureNetworks[0]?.topologyKey)}
            onClick={() => {
              void startCapture()
            }}
          >
            开启抓包
          </Button>
        </div>
        <Table mt="sm">
          <Table.Thead><Table.Tr><Table.Th>队伍 / 范围</Table.Th><Table.Th>状态</Table.Th><Table.Th>大小 / 限制</Table.Th><Table.Th>文件</Table.Th><Table.Th>操作</Table.Th></Table.Tr></Table.Thead>
          <Table.Tbody>
            {captureRows.length ? captureRows.map(({ env, job }) => (
              <Table.Tr key={`${env.environmentId}-capture-${job.id}`}>
                <Table.Td>
                  <Stack gap={2}>
                    <Text size="sm" fw={800}>{env.teamName}</Text>
                    <Text size="xs" className="yy-readable-text">{job.scope} · {job.workerNodeName || shortText(job.workerNodeId, 18)}</Text>
                  </Stack>
                </Table.Td>
                <Table.Td>
                  <YinyuStatusPill tone={runtimeStatusTone(job.status)} state={runtimeStatusState(job.status)}>
                    {captureStatusLabels[job.status] ?? job.status}
                  </YinyuStatusPill>
                </Table.Td>
                <Table.Td>
                  <Text size="xs" className="yy-readable-text">{formatBytes(job.capturedBytes)} / {formatBytes(job.maxBytes)}</Text>
                  <Text size="xs" className="yy-readable-text">{job.maxSeconds}s · 创建 {formatDateTime(job.createdAt)}</Text>
                </Table.Td>
                <Table.Td>
                  <Stack gap={2}>
                    <Text size="xs" className="yy-readable-text" lineClamp={2}>{job.filePath ?? '-'}</Text>
                    {job.lastError && <Text size="xs" c="red.3" lineClamp={2}>{job.lastError}</Text>}
                  </Stack>
                </Table.Td>
                <Table.Td>
                  <Group gap="xs">
                    <Button size="compact-xs" variant="light" leftSection={<Icon path={mdiRefresh} size={0.7} />} disabled={captureBusy} onClick={() => void refreshCapture(env.teamId, job.id)}>
                      刷新
                    </Button>
                    {job.status === TeamLabTrafficCaptureStatus.Running || job.status === TeamLabTrafficCaptureStatus.Stopping ? (
                      <Button size="compact-xs" color="red" variant="light" leftSection={<Icon path={mdiStop} size={0.7} />} disabled={captureBusy} onClick={() => void stopCapture(env.teamId, job.id)}>
                        停止
                      </Button>
                    ) : null}
                    {job.status !== TeamLabTrafficCaptureStatus.Running && (job.filePath || job.capturedBytes > 0) ? (
                      <Button size="compact-xs" variant="light" leftSection={<Icon path={mdiDownload} size={0.7} />} onClick={() => downloadCapture(env.teamId, job.id)}>
                        下载 PCAP
                      </Button>
                    ) : null}
                  </Group>
                </Table.Td>
              </Table.Tr>
            )) : (
              <Table.Tr><Table.Td colSpan={5}><Text size="sm" className="yy-readable-text">暂无抓包任务。</Text></Table.Td></Table.Tr>
            )}
          </Table.Tbody>
        </Table>
        <Table mt="md">
          <Table.Thead>
            <Table.Tr>
              <Table.Th>队伍 / 网段</Table.Th>
              <Table.Th>协议</Table.Th>
              <Table.Th>源</Table.Th>
              <Table.Th>目标</Table.Th>
              <Table.Th>字节</Table.Th>
              <Table.Th>时间</Table.Th>
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {flowRows.length ? flowRows.slice(0, 50).map(({ env, flow }, index) => (
              <Table.Tr key={`${env.environmentId}-flow-${index}-${flow.capturedAt}`}>
                <Table.Td>
                  <Stack gap={2}>
                    <Text size="sm" fw={800}>{env.teamName}</Text>
                    <Text size="xs" className="yy-readable-text">{flow.networkName || `network#${flow.networkId ?? '-'}`} · {flow.workerNodeName || shortText(flow.workerNodeId, 18)}</Text>
                  </Stack>
                </Table.Td>
                <Table.Td><Text size="xs" fw={800}>{flow.protocol}</Text></Table.Td>
                <Table.Td><Text size="xs" className="yy-readable-text">{flow.sourceIp}{flow.sourcePort ? `:${flow.sourcePort}` : ''}</Text></Table.Td>
                <Table.Td><Text size="xs" className="yy-readable-text">{flow.destinationIp}{flow.destinationPort ? `:${flow.destinationPort}` : ''}</Text></Table.Td>
                <Table.Td><Text size="xs" className="yy-readable-text">{formatBytes(flow.bytes)}</Text></Table.Td>
                <Table.Td><Text size="xs" className="yy-readable-text">{formatDateTime(flow.capturedAt)}</Text></Table.Td>
              </Table.Tr>
            )) : (
              <Table.Tr><Table.Td colSpan={6}><Text size="sm" className="yy-readable-text">暂无流量元数据。部署后默认启动采集，点击刷新元数据获取最近样本。</Text></Table.Td></Table.Tr>
            )}
          </Table.Tbody>
        </Table>
      </YinyuTableShell>

      <YinyuTableShell p="xs">
        <Group justify="space-between" mb="xs">
          <Stack gap={0}>
            <Text fw={900}>节点可溯源列表</Text>
            <Text size="xs" className="yy-readable-text">兼容视图保留容器/VM 标识、镜像和网卡摘要。</Text>
          </Stack>
          <Text size="xs" className="yy-readable-text">共 {runtimeNodeRows.length} 个节点记录</Text>
        </Group>
        <Table>
          <Table.Thead><Table.Tr><Table.Th>队伍 / 资产</Table.Th><Table.Th>状态</Table.Th><Table.Th>容器与镜像</Table.Th><Table.Th>内网地址</Table.Th><Table.Th>网卡</Table.Th></Table.Tr></Table.Thead>
          <Table.Tbody>
            {runtimeNodeRows.length ? runtimeNodeRows
              .sort((left, right) =>
                Number(right.node.status === PenetrationRuntimeStatus.Running) -
                Number(left.node.status === PenetrationRuntimeStatus.Running) ||
                left.env.teamName.localeCompare(right.env.teamName) ||
                left.node.nodeName.localeCompare(right.node.nodeName))
              .map(({ env, node }) => {
                const interfaces = parseRuntimeInterfaces(node.interfaceSummary)
                return (
                  <Table.Tr key={`${env.environmentId}-${node.runtimeNodeId}`}>
                    <Table.Td>
                      <Stack gap={2}>
                        <Text fw={800}>{env.teamName} / {node.nodeName}</Text>
                        <Text size="xs" className="yy-readable-text">资产标识：{shortText(node.topologyNodeKey, 18)}</Text>
                        <Text size="xs" className="yy-readable-text">创建：{formatDateTime(node.createdAt)}</Text>
                      </Stack>
                    </Table.Td>
                    <Table.Td>
                      <YinyuStatusPill tone={runtimeStatusTone(node.status)} state={runtimeStatusState(node.status)}>
                        {runtimeStatusLabels[node.status] ?? node.status}
                      </YinyuStatusPill>
                    </Table.Td>
                    <Table.Td>
                      <Stack gap={2}>
                        <Text size="xs" className="yy-readable-text">容器：{shortText(node.containerId)}</Text>
                        <Text size="xs" className="yy-readable-text">状态：{node.containerStatus ?? '-'}</Text>
                        <Text size="xs" className="yy-readable-text" lineClamp={1}>镜像：{node.image ?? '-'}</Text>
                      </Stack>
                    </Table.Td>
                    <Table.Td>
                      <Stack gap={2}>
                        <Text size="xs" className="yy-readable-text">主地址：{node.ipAddress || '-'}</Text>
                        <Text size="xs" className="yy-readable-text">访问方式：队伍 VPN 内网</Text>
                        <Button size="compact-xs" variant="light" onClick={() => onRestartRuntimeNode(node.runtimeNodeId, env.teamName, node.nodeName)}>
                          重建整队环境
                        </Button>
                      </Stack>
                    </Table.Td>
                    <Table.Td>
                      <Stack gap={2}>
                        {interfaces.length ? interfaces.slice(0, 3).map((iface, index) => (
                          <Text size="xs" className="yy-readable-text" lineClamp={1} key={`${node.runtimeNodeId}-${index}`}>
                            {iface.interfaceName ?? `eth${index}`} · {iface.networkName ?? node.networkName} · {iface.ipAddress ?? '-'} / {iface.cidr ?? '-'}{iface.isPrimary ? ' · 主网卡' : ''}
                          </Text>
                        )) : (
                          <Text size="xs" className="yy-readable-text">{node.networkName || '-'} · {node.ipAddress || '-'}</Text>
                        )}
                        {interfaces.length > 3 && <Text size="xs" className="yy-readable-text">另有 {interfaces.length - 3} 块网卡</Text>}
                      </Stack>
                    </Table.Td>
                  </Table.Tr>
                )
              }) : (
              <Table.Tr><Table.Td colSpan={5}><Text size="sm" className="yy-readable-text">暂无运行节点。</Text></Table.Td></Table.Tr>
            )}
          </Table.Tbody>
        </Table>
      </YinyuTableShell>

      <YinyuTableShell p="xs">
        <Table>
          <Table.Thead><Table.Tr><Table.Th>队伍</Table.Th><Table.Th>网络级路由</Table.Th><Table.Th>路径</Table.Th><Table.Th>执行摘要</Table.Th></Table.Tr></Table.Thead>
          <Table.Tbody>
            {runtimeRouteRows.length ? runtimeRouteRows.map(({ env, route }) => (
              <Table.Tr key={`${env.environmentId}-${route.id}`}>
                <Table.Td>{env.teamName}</Table.Td>
                <Table.Td>
                  <Stack gap={2}>
                    <Text size="sm" fw={800}>{route.label}</Text>
                    <YinyuStatusPill tone={routeStatusTone(route.status)} state={route.status === PenetrationRouteStatus.RouteApplied ? 'running' : 'idle'}>
                      {routeStatusLabels[route.status]}
                    </YinyuStatusPill>
                    <Text size="xs" fw={800} c={route.isExecutable ? 'teal.2' : 'dimmed'}>
                      {route.isExecutable ? '网络级路由记录' : '未应用路由'}
                    </Text>
                    <Text size="xs" className="yy-readable-text">{enforcementLabel(route.enforcementMode)}</Text>
                  </Stack>
                </Table.Td>
                <Table.Td>
                  <Text size="xs" className="yy-readable-text">
                    {route.sourceNetworkName ?? '-'} ({route.sourceCidr ?? '-'}) → {route.targetNetworkName ?? '-'} ({route.targetCidr ?? '-'})
                  </Text>
                  {route.routeNodeName && <Text size="xs" className="yy-readable-text">经由：{route.routeNodeName} · 网关：{route.gatewayIp ?? '-'}</Text>}
                  {route.appliedAt && <Text size="xs" className="yy-readable-text">应用：{formatDateTime(route.appliedAt)}</Text>}
                </Table.Td>
                <Table.Td>
                  <Stack gap={2}>
                    <Text size="xs" className="yy-readable-text" lineClamp={2}>{route.message ?? '无执行说明'}</Text>
                    {route.commandSummary && <Text size="xs" className="yy-readable-text" lineClamp={2}>{route.commandSummary}</Text>}
                  </Stack>
                </Table.Td>
              </Table.Tr>
            )) : (
              <Table.Tr><Table.Td colSpan={4}><Text size="sm" className="yy-readable-text">暂无运行期路由记录。发布带有 RuntimeRoute/Both 的路由关系并部署队伍环境后会在这里显示。</Text></Table.Td></Table.Tr>
            )}
          </Table.Tbody>
        </Table>
      </YinyuTableShell>

      <YinyuTableShell p="xs">
        <Table>
          <Table.Thead><Table.Tr><Table.Th>提交队伍</Table.Th><Table.Th>得分项</Table.Th><Table.Th>状态</Table.Th></Table.Tr></Table.Thead>
          <Table.Tbody>{submissions.slice(0, 12).map((item) => <Table.Tr key={item.id}><Table.Td>{item.teamName}</Table.Td><Table.Td>{item.nodeName} / {item.itemTitle}</Table.Td><Table.Td>{item.status}</Table.Td></Table.Tr>)}</Table.Tbody>
        </Table>
      </YinyuTableShell>

      <YinyuTableShell p="xs">
        <Group justify="space-between" mb="xs">
          <Stack gap={0}>
            <Text fw={800}>部署时间线</Text>
            <Text size="xs" className="yy-readable-text">共 {deploymentEventTotal} 条事件，第 {deploymentEventPage} 页</Text>
          </Stack>
          <Group gap="xs">
            <Button size="compact-xs" variant="light" disabled={deploymentEventPage <= 1} onClick={() => void onLoadDeploymentEvents(deploymentEventPage - 1)}>
              上一页
            </Button>
            <Button size="compact-xs" variant="light" disabled={deploymentEventPage * 50 >= deploymentEventTotal} onClick={() => void onLoadDeploymentEvents(deploymentEventPage + 1)}>
              下一页
            </Button>
          </Group>
        </Group>
        <Table>
          <Table.Thead><Table.Tr><Table.Th>队伍</Table.Th><Table.Th>级别</Table.Th><Table.Th>阶段</Table.Th><Table.Th>内容</Table.Th></Table.Tr></Table.Thead>
          <Table.Tbody>
            {deploymentEvents.length > 0 ? deploymentEvents.map((event) => (
              <Table.Tr key={`${event.environmentId}-${event.id}`}>
                <Table.Td>
                  <Stack gap={2}>
                    <Text size="sm">{event.teamName}</Text>
                    <Text size="xs" className="yy-readable-text">{formatDateTime(event.createdAt)}</Text>
                  </Stack>
                </Table.Td>
                <Table.Td>
                  <YinyuStatusPill tone={deploymentEventTone(event.level)} state={event.level === PenetrationDeploymentEventLevel.Error ? 'alert' : 'busy'}>
                    {deploymentEventLabel[event.level]}
                  </YinyuStatusPill>
                </Table.Td>
                <Table.Td>{event.stage}{event.nodeName ? ` / ${event.nodeName}` : ''}</Table.Td>
                <Table.Td>
                  <Stack gap={2}>
                    <Text size="sm">{event.message}</Text>
                    {event.detail && <Text size="xs" className="yy-readable-text" lineClamp={2}>{event.detail}</Text>}
                  </Stack>
                </Table.Td>
              </Table.Tr>
            )) : (
              <Table.Tr><Table.Td colSpan={4}><Text size="sm" className="yy-readable-text">暂无部署事件。</Text></Table.Td></Table.Tr>
            )}
          </Table.Tbody>
        </Table>
      </YinyuTableShell>
    </Stack>
  )
}
