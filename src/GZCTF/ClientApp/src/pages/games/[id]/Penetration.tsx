import { Button, Group, PasswordInput, Progress, ScrollArea, Stack, Text, Title, Textarea } from '@mantine/core'
import { useModals } from '@mantine/modals'
import { showNotification } from '@mantine/notifications'
import * as signalR from '@microsoft/signalr'
import { mdiCheck, mdiContentCopy, mdiDownload, mdiFlagOutline, mdiRefresh, mdiShieldSearch, mdiVpn } from '@mdi/js'
import { Icon } from '@mdi/react'
import { FC, useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useParams } from 'react-router'
import { Markdown } from '@Components/MarkdownRenderer'
import { WithGameTab } from '@Components/WithGameTab'
import { WithNavBar } from '@Components/WithNavbar'
import { WithRole } from '@Components/WithRole'
import { YinyuPanel, YinyuRouteLoader } from '@Components/yinyu/YinyuUI'
import { YinyuStatusText } from '@Components/yinyu/YinyuReactBits'
import { encryptApiData } from '@Utils/Crypto'
import { copyText, showErrorMsg } from '@Utils/Shared'
import { useConfig } from '@Hooks/useConfig'
import { Role } from '@Api'
import {
  PenetrationRuntimeStatus,
  PenetrationWorkspaceUpdateModel,
  PenetrationWorkspaceModel,
  PenetrationWorkspaceNodeModel,
  PenetrationWorkspaceScoreItemModel,
  TeamLabVpnConfigModel,
  penetrationPlayerApi,
} from '../../../Api/PenetrationApi'

type StatusTone = 'success' | 'warm' | 'danger' | 'neutral'

type PentestTask = {
  node: PenetrationWorkspaceNodeModel
  item: PenetrationWorkspaceScoreItemModel
  index: number
  locked: boolean
}

const statusLabel: Record<PenetrationRuntimeStatus, string> = {
  [PenetrationRuntimeStatus.Pending]: '等待部署',
  [PenetrationRuntimeStatus.Running]: '运行中',
  [PenetrationRuntimeStatus.Stopped]: '已停止',
  [PenetrationRuntimeStatus.Failed]: '异常',
  [PenetrationRuntimeStatus.CreatingNetworks]: '创建网络中',
  [PenetrationRuntimeStatus.CreatingContainers]: '创建容器中',
  [PenetrationRuntimeStatus.CleanupPending]: '清理中',
  [PenetrationRuntimeStatus.Orphaned]: '存在残留',
  [PenetrationRuntimeStatus.ManualCleanupRequired]: '需人工清理',
}

const runtimeTone = (status?: PenetrationRuntimeStatus): StatusTone => {
  if (status === PenetrationRuntimeStatus.Running) return 'success'
  if (
    status === PenetrationRuntimeStatus.Failed ||
    status === PenetrationRuntimeStatus.Orphaned ||
    status === PenetrationRuntimeStatus.ManualCleanupRequired
  ) {
    return 'danger'
  }
  if (
    status === PenetrationRuntimeStatus.Pending ||
    status === PenetrationRuntimeStatus.CreatingNetworks ||
    status === PenetrationRuntimeStatus.CreatingContainers ||
    status === PenetrationRuntimeStatus.CleanupPending
  ) {
    return 'warm'
  }
  return 'neutral'
}

const taskStatus = (task?: PentestTask) => {
  if (!task) return { label: '未选择', tone: 'neutral' as StatusTone }
  if (task.item.solved) return { label: '已解出', tone: 'success' as StatusTone }
  if (task.locked) return { label: '前置未完成', tone: 'warm' as StatusTone }
  return { label: '可提交', tone: 'success' as StatusTone }
}

const buildTasks = (workspace?: PenetrationWorkspaceModel): PentestTask[] => {
  const nodes = workspace?.nodes ?? []
  const allItems = nodes.flatMap((node) => node.scoreItems.map((item) => ({ node, item })))
  const solvedIds = new Set(allItems.filter(({ item }) => item.solved).map(({ item }) => item.id))
  const solvedKeys = new Set(allItems.filter(({ item }) => item.solved).map(({ item }) => item.topologyKey).filter(Boolean))

  return allItems.map(({ node, item }, index) => {
    const locked = item.prerequisiteItemKeys?.length
      ? item.prerequisiteItemKeys.some((key) => !solvedKeys.has(key))
      : item.prerequisiteItemIds.some((itemId) => !solvedIds.has(itemId))

    return { node, item, index: index + 1, locked }
  })
}

const hasDescription = (value?: string | null) => Boolean(value?.trim())

const PenetrationPage: FC = () => {
  const { id } = useParams()
  const gameId = parseInt(id ?? '-1')
  const { config } = useConfig()
  const modals = useModals()
  const [workspace, setWorkspace] = useState<PenetrationWorkspaceModel>()
  const [selectedTaskId, setSelectedTaskId] = useState<number>()
  const [flags, setFlags] = useState<Record<number, string>>({})
  const [loading, setLoading] = useState(false)
  const [errorText, setErrorText] = useState<string>()
  const [vpnConfig, setVpnConfig] = useState<TeamLabVpnConfigModel | null>(null)
  const teamIdRef = useRef<number | undefined>(undefined)

  const tasks = useMemo(() => buildTasks(workspace), [workspace])
  const selectedTask = useMemo(
    () => tasks.find((task) => task.item.id === selectedTaskId) ?? tasks.find((task) => !task.item.solved) ?? tasks[0],
    [selectedTaskId, tasks]
  )
  const solvedCount = tasks.filter((task) => task.item.solved).length
  const totalCount = tasks.length
  const progress = totalCount > 0 ? Math.round((solvedCount / totalCount) * 100) : 0
  const remainingReset = workspace ? Math.max(0, workspace.maxResetCount - workspace.resetCount) : 0
  const currentStatus = taskStatus(selectedTask)

  const load = useCallback(async (silent = false) => {
    if (gameId <= 0) return
    if (!silent) setLoading(true)
    setErrorText(undefined)

    try {
      const res = await penetrationPlayerApi.getWorkspace(gameId)
      teamIdRef.current = res.data.teamId
      setWorkspace(res.data)
      try {
        const vpn = await penetrationPlayerApi.getTeamLabVpnConfig(gameId)
        setVpnConfig(vpn.data)
      } catch {
        setVpnConfig(null)
      }
      setSelectedTaskId((current) => {
        const nextTasks = buildTasks(res.data)
        if (current && nextTasks.some((task) => task.item.id === current)) return current
        return nextTasks.find((task) => !task.item.solved)?.item.id ?? nextTasks[0]?.item.id
      })
    } catch {
      if (!silent) setErrorText('渗透演练环境尚未部署，或当前队伍暂时没有访问权限。')
    } finally {
      if (!silent) setLoading(false)
    }
  }, [gameId])

  useEffect(() => {
    void load()
  }, [load])

  useEffect(() => {
    if (gameId <= 0) return

    const connection = new signalR.HubConnectionBuilder()
      .withUrl(`/hub/user?game=${gameId}`)
      .withHubProtocol(new signalR.JsonHubProtocol())
      .withAutomaticReconnect()
      .configureLogging(signalR.LogLevel.None)
      .build()

    connection.serverTimeoutInMilliseconds = 60 * 1000 * 60 * 2

    connection.on('ReceivedPenetrationWorkspaceUpdate', (update: PenetrationWorkspaceUpdateModel) => {
      if (update.gameId !== gameId) return
      if (teamIdRef.current && update.teamId !== teamIdRef.current) return

      void load(true)
    })

    connection.onreconnected(() => {
      void load(true)
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

  const executeReset = async () => {
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

  const reset = () => {
    modals.openConfirmModal({
      title: '确认重置渗透演练环境',
      children: (
        <Text size="sm" className="yy-readable-text">
          重置会销毁并重建本队全部渗透容器和网络，成功后会消耗一次重置次数。正在进行的连接会被断开。
        </Text>
      ),
      labels: { confirm: '重置环境', cancel: '取消' },
      confirmProps: { color: 'yellow' },
      onConfirm: () => void executeReset(),
    })
  }

  const copyVpnConfig = async () => {
    if (!vpnConfig?.configText) return
    const ok = await copyText(vpnConfig.configText)
    showNotification({
      color: ok ? 'teal' : 'red',
      message: ok ? 'VPN 配置已复制。' : '复制失败，请手动选中配置内容。',
      icon: <Icon path={ok ? mdiCheck : mdiShieldSearch} size={1} />,
    })
  }

  const downloadVpnConfig = () => {
    if (!vpnConfig?.configText) return
    const blob = new Blob([vpnConfig.configText], { type: 'text/plain;charset=utf-8' })
    const url = URL.createObjectURL(blob)
    const link = document.createElement('a')
    link.href = url
    link.download = vpnConfig.fileName || `tl-${gameId}-${vpnConfig.teamId}.conf`
    document.body.appendChild(link)
    link.click()
    document.body.removeChild(link)
    URL.revokeObjectURL(url)
  }

  const selectedValue = selectedTask ? (flags[selectedTask.item.id] ?? '') : ''
  const submitDisabled = !selectedTask || selectedTask.item.solved || selectedTask.locked || loading || !selectedValue.trim()

  return (
    <WithNavBar minWidth={0} width="min(100%, calc(100vw - 7.25rem))">
      <WithRole requiredRole={Role.User}>
        <WithGameTab>
          <Stack gap="md" className="yy-pentest-player yy-pentest-ctf-page">
            {loading && !workspace ? (
              <YinyuPanel p="xl">
                <YinyuRouteLoader title="渗透演练加载中" description="正在读取队伍环境和题目列表" />
              </YinyuPanel>
            ) : null}

            {errorText && !workspace ? (
              <YinyuPanel p="xl" className="yy-pentest-empty-panel">
                <Stack gap="xs">
                  <Title order={2}>渗透环境暂不可用</Title>
                  <Text className="yy-readable-text">{errorText}</Text>
                  <Button leftSection={<Icon path={mdiRefresh} size={0.85} />} onClick={() => void load()}>
                    重新读取
                  </Button>
                </Stack>
              </YinyuPanel>
            ) : null}

            {workspace ? (
              <>
                <YinyuPanel p="md" className="yy-pentest-player-header yy-pentest-ctf-header">
                  <Group justify="space-between" align="center" wrap="wrap">
                    <Stack gap={5}>
                      <Group gap="md" align="baseline">
                        <Title order={2}>渗透题目</Title>
                        <YinyuStatusText tone={runtimeTone(workspace.status)}>
                          {statusLabel[workspace.status] ?? workspace.status}
                        </YinyuStatusText>
                      </Group>
                      <Text className="yy-readable-text">
                        {workspace.teamName} / 已完成 {solvedCount}/{totalCount} 题
                      </Text>
                    </Stack>
                    <Stack gap={6} className="yy-pentest-reset-block">
                      <Button
                        leftSection={<Icon path={mdiRefresh} size={0.85} />}
                        variant="light"
                        disabled={loading || remainingReset <= 0}
                        onClick={reset}
                      >
                        重置环境
                      </Button>
                      <Text size="xs" className="yy-readable-text" ta="right">
                        剩余 {remainingReset} 次
                      </Text>
                    </Stack>
                  </Group>
                  <Progress value={progress} size="sm" radius="xl" className="yy-pentest-progress yy-pentest-ctf-progress" />
                </YinyuPanel>

                {vpnConfig ? (
                  <YinyuPanel p="md" className="yy-pentest-vpn-panel">
                    <Group justify="space-between" align="flex-start" wrap="wrap">
                      <Stack gap={4}>
                        <Group gap="xs" align="center">
                          <Icon path={mdiVpn} size={0.9} />
                          <Title order={3}>VPN 接入</Title>
                          <YinyuStatusText tone="success">配置就绪</YinyuStatusText>
                        </Group>
                        <Text className="yy-readable-text" size="sm">
                          {vpnConfig.teamName} / {vpnConfig.endpoint} / {vpnConfig.clientAddress}
                        </Text>
                      </Stack>
                      <Group gap="xs">
                        <Button variant="default" leftSection={<Icon path={mdiContentCopy} size={0.78} />} onClick={() => void copyVpnConfig()}>
                          复制配置
                        </Button>
                        <Button variant="light" leftSection={<Icon path={mdiDownload} size={0.78} />} onClick={downloadVpnConfig}>
                          下载配置
                        </Button>
                      </Group>
                    </Group>
                    <Textarea
                      mt="sm"
                      value={vpnConfig.configText}
                      readOnly
                      autosize
                      minRows={6}
                      maxRows={10}
                      className="yy-pentest-vpn-config"
                    />
                  </YinyuPanel>
                ) : null}

                <div className="yy-pentest-ctf-layout">
                  <YinyuPanel p="md" className="yy-pentest-question-panel">
                    <Group justify="space-between" align="baseline" className="yy-pentest-panel-heading">
                      <Title order={3}>题目</Title>
                      <Text size="sm" className="yy-readable-text">
                        {totalCount} 题
                      </Text>
                    </Group>
                    <ScrollArea className="yy-pentest-question-scroll" type="hover" offsetScrollbars>
                      <div className="yy-pentest-question-grid">
                        {tasks.map((task) => {
                          const status = taskStatus(task)
                          return (
                            <button
                              key={task.item.id}
                              type="button"
                              className="yy-pentest-question-card"
                              data-active={selectedTask?.item.id === task.item.id || undefined}
                              data-solved={task.item.solved || undefined}
                              onClick={() => setSelectedTaskId(task.item.id)}
                            >
                              <span className="yy-pentest-question-index">{String(task.index).padStart(2, '0')}</span>
                              <span className="yy-pentest-question-main">
                                <strong>题目 {String(task.index).padStart(2, '0')}</strong>
                                <small>点击查看题目信息</small>
                              </span>
                              <YinyuStatusText tone={status.tone} className="yy-pentest-question-status">
                                {status.label}
                              </YinyuStatusText>
                            </button>
                          )
                        })}
                      </div>
                    </ScrollArea>
                  </YinyuPanel>

                  <YinyuPanel p="lg" className="yy-pentest-detail-panel">
                    {selectedTask ? (
                      <Stack gap="lg" h="100%">
                        <Group justify="space-between" align="flex-start" wrap="wrap">
                          <Stack gap={6} className="yy-pentest-detail-title">
                            <Text className="yy-readable-text" size="sm">
                              题目 {String(selectedTask.index).padStart(2, '0')}
                            </Text>
                            <Title order={2}>题目 {String(selectedTask.index).padStart(2, '0')}</Title>
                          </Stack>
                          <YinyuStatusText tone={currentStatus.tone}>{currentStatus.label}</YinyuStatusText>
                        </Group>

                        <div className="yy-pentest-description">
                          {hasDescription(selectedTask.item.description) ? (
                            <Markdown source={selectedTask.item.description ?? ''} />
                          ) : (
                            <Text className="yy-readable-text">该题目暂未提供额外说明。</Text>
                          )}
                        </div>

                        <Stack gap="sm" mt="auto" className="yy-pentest-submit-zone">
                          {selectedTask.locked ? (
                            <Text className="yy-readable-text" size="sm">
                              该题目仍有前置得分项未完成，完成前置题目后即可提交。
                            </Text>
                          ) : null}
                          <Group align="flex-end" wrap="nowrap" className="yy-pentest-submit-row">
                            <PasswordInput
                              label="提交 Flag"
                              placeholder="flag{...}"
                              value={selectedValue}
                              disabled={loading || selectedTask.item.solved || selectedTask.locked}
                              onChange={(event) => setFlags((current) => ({ ...current, [selectedTask.item.id]: event.currentTarget.value }))}
                              style={{ flex: 1 }}
                            />
                            <Button
                              leftSection={<Icon path={mdiFlagOutline} size={0.85} />}
                              disabled={submitDisabled}
                              onClick={() => void submit(selectedTask.item.id)}
                            >
                              提交
                            </Button>
                          </Group>
                        </Stack>
                      </Stack>
                    ) : (
                      <Stack gap="xs">
                        <Title order={2}>暂无题目</Title>
                        <Text className="yy-readable-text">当前渗透演练还没有可展示的题目，请联系管理员检查发布配置。</Text>
                      </Stack>
                    )}
                  </YinyuPanel>
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
