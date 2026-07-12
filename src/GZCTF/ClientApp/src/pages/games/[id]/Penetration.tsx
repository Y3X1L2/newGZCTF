import { Button, Group, PasswordInput, Progress, ScrollArea, Stack, Text, Title } from '@mantine/core'
import { useModals } from '@mantine/modals'
import { showNotification } from '@mantine/notifications'
import * as signalR from '@microsoft/signalr'
import { mdiCheck, mdiFlagOutline, mdiRefresh, mdiShieldSearch, mdiVpn } from '@mdi/js'
import { Icon } from '@mdi/react'
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useParams } from 'react-router'
import { Markdown } from '@Components/MarkdownRenderer'
import { WithGameTab } from '@Components/WithGameTab'
import { WithNavBar } from '@Components/WithNavbar'
import { WithRole } from '@Components/WithRole'
import { YinyuPanel, YinyuRouteLoader } from '@Components/yinyu/YinyuUI'
import { YinyuStatusText } from '@Components/yinyu/YinyuReactBits'
import { encryptApiData } from '@Utils/Crypto'
import { showErrorMsg } from '@Utils/Shared'
import { useConfig } from '@Hooks/useConfig'
import { Role } from '@Api'
import {
  PenetrationWorkspaceModel,
  PenetrationWorkspaceObjectiveModel,
  PenetrationWorkspaceUpdateModel,
  penetrationPlayerApi,
} from '../../../Api/PenetrationApi'
import { TeamLabRuntimeStatus } from '../../../Api/TeamLabApi'

type Task = { objective: PenetrationWorkspaceObjectiveModel; index: number; locked: boolean }

const statusLabel: Record<TeamLabRuntimeStatus, string> = {
  [TeamLabRuntimeStatus.Pending]: '等待部署',
  [TeamLabRuntimeStatus.Planning]: '规划中',
  [TeamLabRuntimeStatus.Scheduled]: '排队中',
  [TeamLabRuntimeStatus.Deploying]: '部署中',
  [TeamLabRuntimeStatus.Probing]: '检查中',
  [TeamLabRuntimeStatus.Running]: '运行中',
  [TeamLabRuntimeStatus.Failed]: '异常',
  [TeamLabRuntimeStatus.CleanupPending]: '待清理',
  [TeamLabRuntimeStatus.Stopped]: '已停止',
  [TeamLabRuntimeStatus.Destroying]: '销毁中',
  [TeamLabRuntimeStatus.Destroyed]: '已销毁',
}

const buildTasks = (workspace?: PenetrationWorkspaceModel): Task[] => {
  const objectives = workspace?.objectives ?? []
  const solved = new Set(objectives.filter((item) => item.solved).map((item) => item.key))
  return objectives.map((objective, index) => ({
    objective,
    index: index + 1,
    locked: objective.prerequisiteKeys.some((key) => !solved.has(key)),
  }))
}

const runtimeTone = (status: TeamLabRuntimeStatus) => {
  if (status === TeamLabRuntimeStatus.Running) return 'success' as const
  if (status === TeamLabRuntimeStatus.Failed) return 'danger' as const
  if ([TeamLabRuntimeStatus.Planning, TeamLabRuntimeStatus.Scheduled, TeamLabRuntimeStatus.Deploying, TeamLabRuntimeStatus.Probing, TeamLabRuntimeStatus.Destroying].includes(status)) return 'warm' as const
  return 'neutral' as const
}

const PenetrationPage = () => {
  const { id } = useParams()
  const gameId = Number(id ?? -1)
  const { config } = useConfig()
  const modals = useModals()
  const [workspace, setWorkspace] = useState<PenetrationWorkspaceModel>()
  const [selectedId, setSelectedId] = useState<number>()
  const [flags, setFlags] = useState<Record<number, string>>({})
  const [loading, setLoading] = useState(false)
  const [errorText, setErrorText] = useState<string>()
  const teamIdRef = useRef<number | undefined>(undefined)

  const tasks = useMemo(() => buildTasks(workspace), [workspace])
  const selected = useMemo(() => tasks.find((task) => task.objective.id === selectedId) ?? tasks.find((task) => !task.objective.solved) ?? tasks[0], [selectedId, tasks])
  const solvedCount = tasks.filter((task) => task.objective.solved).length
  const progress = tasks.length ? Math.round((solvedCount / tasks.length) * 100) : 0
  const remainingReset = workspace ? Math.max(0, workspace.maxResetCount - workspace.resetCount) : 0

  const load = useCallback(async (silent = false) => {
    if (gameId <= 0) return
    if (!silent) setLoading(true)
    try {
      const response = await penetrationPlayerApi.getWorkspace(gameId)
      teamIdRef.current = response.data.teamId
      setWorkspace(response.data)
      setSelectedId((current) => current && response.data.objectives.some((item) => item.id === current)
        ? current
        : response.data.objectives.find((item) => !item.solved)?.id ?? response.data.objectives[0]?.id)
      setErrorText(undefined)
    } catch {
      if (!silent) setErrorText('渗透演练环境尚未部署，或当前队伍暂时没有访问权限。')
    } finally {
      if (!silent) setLoading(false)
    }
  }, [gameId])

  useEffect(() => { void load() }, [load])

  useEffect(() => {
    if (gameId <= 0) return
    const connection = new signalR.HubConnectionBuilder()
      .withUrl(`/hub/user?game=${gameId}`)
      .withHubProtocol(new signalR.JsonHubProtocol())
      .withAutomaticReconnect()
      .configureLogging(signalR.LogLevel.None)
      .build()
    connection.on('ReceivedPenetrationWorkspaceUpdate', (update: PenetrationWorkspaceUpdateModel) => {
      if (update.gameId === gameId && (!teamIdRef.current || update.teamId === teamIdRef.current)) void load(true)
    })
    connection.onreconnected(() => void load(true))
    void connection.start()
    return () => { void connection.stop() }
  }, [gameId, load])

  const submit = async () => {
    if (!selected) return
    const raw = flags[selected.objective.id]?.trim()
    if (!raw) return
    setLoading(true)
    try {
      const encrypted = await encryptApiData((key) => key, raw, config.apiPublicKey)
      const response = await penetrationPlayerApi.submit(gameId, selected.objective.id, encrypted)
      showNotification({
        color: response.data.accepted ? 'teal' : 'yellow',
        message: response.data.accepted ? `提交成功 +${response.data.score}` : response.data.message,
        icon: <Icon path={response.data.accepted ? mdiCheck : mdiShieldSearch} size={0.85} />,
      })
      setFlags((current) => ({ ...current, [selected.objective.id]: '' }))
      await load(true)
    } catch (error) { showErrorMsg(error, (key) => key) } finally { setLoading(false) }
  }

  const downloadVpn = async () => {
    setLoading(true)
    try {
      const response = await penetrationPlayerApi.createAccessGrant(gameId)
      if (!response.data.configurationDownloadUrl) throw new Error('VPN configuration is unavailable.')
      window.location.assign(response.data.configurationDownloadUrl)
    } catch (error) { showErrorMsg(error, (key) => key) } finally { setLoading(false) }
  }

  const executeReset = async () => {
    setLoading(true)
    try {
      await penetrationPlayerApi.reset(gameId)
      showNotification({ color: 'teal', message: '环境已进入重置队列。', icon: <Icon path={mdiRefresh} size={0.85} /> })
      await load(true)
    } catch (error) { showErrorMsg(error, (key) => key) } finally { setLoading(false) }
  }

  const confirmReset = () => modals.openConfirmModal({
    title: '确认重置渗透环境',
    children: <Text size="sm">本队当前环境会被完整销毁并以新代次重建。</Text>,
    labels: { confirm: '重置', cancel: '取消' },
    confirmProps: { color: 'yellow' },
    onConfirm: () => void executeReset(),
  })

  return (
    <WithNavBar minWidth={0} width="min(100%, calc(100vw - 7.25rem))">
      <WithRole requiredRole={Role.User}>
        <WithGameTab>
          <Stack gap="md" className="yy-pentest-player yy-pentest-ctf-page">
            {loading && !workspace ? <YinyuPanel p="xl"><YinyuRouteLoader title="渗透演练加载中" /></YinyuPanel> : null}
            {errorText && !workspace ? <YinyuPanel p="xl"><Stack><Title order={2}>渗透环境暂不可用</Title><Text>{errorText}</Text><Button leftSection={<Icon path={mdiRefresh} size={0.8} />} onClick={() => void load()}>重新读取</Button></Stack></YinyuPanel> : null}
            {workspace ? <>
              <YinyuPanel p="md" className="yy-pentest-player-header yy-pentest-ctf-header">
                <Group justify="space-between" wrap="wrap">
                  <Stack gap={4}>
                    <Group><Title order={2}>渗透题目</Title><YinyuStatusText tone={runtimeTone(workspace.status)}>{statusLabel[workspace.status]}</YinyuStatusText></Group>
                    <Text>{workspace.teamName} / 已完成 {solvedCount}/{tasks.length}</Text>
                  </Stack>
                  <Group>
                    <Button variant="light" leftSection={<Icon path={mdiVpn} size={0.8} />} disabled={loading || workspace.status !== TeamLabRuntimeStatus.Running} onClick={() => void downloadVpn()}>下载 VPN 配置</Button>
                    <Button variant="light" leftSection={<Icon path={mdiRefresh} size={0.8} />} disabled={loading || remainingReset <= 0} onClick={confirmReset}>重置环境 ({remainingReset})</Button>
                  </Group>
                </Group>
                <Progress value={progress} mt="sm" size="sm" radius="xl" className="yy-pentest-progress yy-pentest-ctf-progress" />
              </YinyuPanel>

              <div className="yy-pentest-ctf-layout">
                <YinyuPanel p="md" className="yy-pentest-question-panel">
                  <Group justify="space-between" mb="sm"><Title order={3}>题目</Title><Text size="sm">{tasks.length} 题</Text></Group>
                  <ScrollArea className="yy-pentest-question-scroll" type="hover" offsetScrollbars>
                    <div className="yy-pentest-question-grid">
                      {tasks.map((task) => (
                        <button key={task.objective.id} type="button" className="yy-pentest-question-card" data-active={selected?.objective.id === task.objective.id || undefined} data-solved={task.objective.solved || undefined} onClick={() => setSelectedId(task.objective.id)}>
                          <span className="yy-pentest-question-index">{String(task.index).padStart(2, '0')}</span>
                          <span className="yy-pentest-question-main"><strong>{task.objective.title}</strong><small>{task.objective.assetKey}</small></span>
                          <YinyuStatusText tone={task.objective.solved ? 'success' : task.locked ? 'warm' : 'neutral'}>{task.objective.solved ? '已完成' : task.locked ? '未解锁' : `${task.objective.score} pts`}</YinyuStatusText>
                        </button>
                      ))}
                    </div>
                  </ScrollArea>
                </YinyuPanel>

                <YinyuPanel p="lg" className="yy-pentest-detail-panel">
                  {selected ? <Stack gap="lg" h="100%">
                    <Group justify="space-between"><Stack gap={4}><Text size="sm">题目 {String(selected.index).padStart(2, '0')}</Text><Title order={2}>{selected.objective.title}</Title></Stack><YinyuStatusText tone={selected.objective.solved ? 'success' : selected.locked ? 'warm' : 'neutral'}>{selected.objective.solved ? '已完成' : selected.locked ? '前置未完成' : `${selected.objective.score} pts`}</YinyuStatusText></Group>
                    <div className="yy-pentest-description">{selected.objective.description ? <Markdown source={selected.objective.description} /> : <Text c="dimmed">暂无题目说明</Text>}</div>
                    <Stack gap="sm" mt="auto" className="yy-pentest-submit-zone">
                      <Group align="flex-end" wrap="nowrap">
                        <PasswordInput label="Flag" placeholder="flag{...}" leftSection={<Icon path={mdiFlagOutline} size={0.75} />} value={flags[selected.objective.id] ?? ''} onChange={(event) => setFlags((current) => ({ ...current, [selected.objective.id]: event.currentTarget.value }))} disabled={selected.objective.solved || selected.locked || loading} style={{ flex: 1 }} />
                        <Button disabled={selected.objective.solved || selected.locked || loading || !(flags[selected.objective.id] ?? '').trim()} onClick={() => void submit()}>提交</Button>
                      </Group>
                      <Text size="xs">已尝试 {selected.objective.attempts}{selected.objective.maxAttempts ? ` / ${selected.objective.maxAttempts}` : ''}</Text>
                    </Stack>
                  </Stack> : <Text c="dimmed">暂无得分目标</Text>}
                </YinyuPanel>
              </div>
            </> : null}
          </Stack>
        </WithGameTab>
      </WithRole>
    </WithNavBar>
  )
}

export default PenetrationPage
