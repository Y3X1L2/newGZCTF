import { Badge, Button, Group, PasswordInput, Progress, Stack, Text, Title } from '@mantine/core'
import { showNotification } from '@mantine/notifications'
import * as signalR from '@microsoft/signalr'
import { mdiCheck, mdiFlagOutline, mdiLockOutline, mdiMapMarkerPath, mdiRefresh, mdiShieldSearch, mdiTarget } from '@mdi/js'
import { Icon } from '@mdi/react'
import { FC, PointerEvent, useCallback, useEffect, useMemo, useRef, useState } from 'react'
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
  PenetrationAttackEdgeModel,
  PenetrationAttackGraphUpdateModel,
  PenetrationAttackNodeModel,
  PenetrationFogState,
  PenetrationRuntimeStatus,
  PenetrationWorkspaceModel,
  PenetrationWorkspaceNodeModel,
  PenetrationWorkspaceScoreItemModel,
  penetrationPlayerApi,
} from '../../../Api/PenetrationApi'

const statusLabel = (status: PenetrationRuntimeStatus) =>
  status === PenetrationRuntimeStatus.Running
    ? '运行中'
    : status === PenetrationRuntimeStatus.Failed
      ? '异常'
      : status === PenetrationRuntimeStatus.Stopped
        ? '已停止'
        : '重建中'

const fogLabel: Record<PenetrationFogState, string> = {
  [PenetrationFogState.Hidden]: '黑雾覆盖',
  [PenetrationFogState.Revealed]: '已侦察',
  [PenetrationFogState.Accessible]: '可攻击',
  [PenetrationFogState.Completed]: '已突破',
}

const fogTone = (state: PenetrationFogState): 'success' | 'neutral' | 'warm' =>
  state === PenetrationFogState.Completed || state === PenetrationFogState.Accessible
    ? 'success'
    : state === PenetrationFogState.Revealed
      ? 'warm'
      : 'neutral'

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
            {item.isCheckpoint ? (
              <Badge color="lime" variant="light">
                解锁检查点
              </Badge>
            ) : null}
            {locked ? (
              <Badge color="yellow" variant="light">
                前置锁定
              </Badge>
            ) : null}
          </Group>
          <Title order={4}>{item.title}</Title>
          <Text className="yy-readable-text" size="sm">
            {node.name} / {fogLabel[node.fogState]}
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
          ? `需要先完成 ${(item.prerequisiteItemKeys?.length || item.prerequisiteItemIds.length)} 个前置得分项`
          : item.maxAttempts > 0
            ? `已提交 ${item.attempts}/${item.maxAttempts} 次`
            : `已提交 ${item.attempts} 次`}
      </Text>
    </Stack>
  </YinyuPanel>
)

const AttackMap: FC<{
  graphNodes: PenetrationAttackNodeModel[]
  graphEdges: PenetrationAttackEdgeModel[]
  selectedKey?: string
  onSelect: (node: PenetrationAttackNodeModel) => void
}> = ({ graphNodes, graphEdges, selectedKey, onSelect }) => {
  const visibleDepths = [...new Set(graphNodes.map((node) => Math.max(0, node.depth)))].sort((a, b) => a - b)
  const nodeByKey = useMemo(() => new Map(graphNodes.map((node) => [node.topologyKey, node])), [graphNodes])
  const visibleEdges = useMemo(
    () => graphEdges
      .map((edge) => ({
        edge,
        source: nodeByKey.get(edge.sourceNodeKey),
        target: nodeByKey.get(edge.targetNodeKey),
      }))
      .filter((item): item is { edge: PenetrationAttackEdgeModel; source: PenetrationAttackNodeModel; target: PenetrationAttackNodeModel } =>
        Boolean(item.source && item.target &&
          item.source.status !== PenetrationFogState.Hidden &&
          item.target.status !== PenetrationFogState.Hidden)),
    [graphEdges, nodeByKey]
  )
  const [view, setView] = useState({ x: 0, y: 0, scale: 1 })
  const [drag, setDrag] = useState<{ pointerId: number; x: number; y: number; originX: number; originY: number }>()

  const onPointerDown = (event: PointerEvent<HTMLDivElement>) => {
    if ((event.target as HTMLElement).closest('button, input, textarea, [role="button"]')) return
    event.currentTarget.setPointerCapture(event.pointerId)
    setDrag({ pointerId: event.pointerId, x: event.clientX, y: event.clientY, originX: view.x, originY: view.y })
  }

  const onPointerMove = (event: PointerEvent<HTMLDivElement>) => {
    if (!drag || drag.pointerId !== event.pointerId) return
    setView((current) => ({
      ...current,
      x: drag.originX + event.clientX - drag.x,
      y: drag.originY + event.clientY - drag.y,
    }))
  }

  const stopDrag = () => setDrag(undefined)

  const zoom = (delta: number) => {
    setView((current) => ({
      ...current,
      scale: Math.min(1.45, Math.max(0.72, Number((current.scale + delta).toFixed(2)))),
    }))
  }

  return (
    <YinyuPanel p="md" className="yy-pentest-attack-map">
      <Stack gap="md">
        <Group justify="space-between" wrap="wrap">
          <Group gap="xs">
            <Icon path={mdiMapMarkerPath} size={1} />
            <Title order={3}>攻击图</Title>
          </Group>
          <Text className="yy-readable-text" size="sm">
            完成检查点后，黑雾会沿允许的攻击路径逐步驱散；未解锁区域不会暴露真实节点、IP 或内部说明。
          </Text>
          <Group gap="xs">
            <Button size="xs" variant="light" onClick={() => zoom(-0.08)}>
              缩小
            </Button>
            <Button size="xs" variant="light" onClick={() => zoom(0.08)}>
              放大
            </Button>
            <Button size="xs" variant="subtle" onClick={() => setView({ x: 0, y: 0, scale: 1 })}>
              重置视角
            </Button>
          </Group>
        </Group>
        <div
          className="yy-pentest-fog-map"
          onPointerDown={onPointerDown}
          onPointerMove={onPointerMove}
          onPointerUp={stopDrag}
          onPointerCancel={stopDrag}
          onWheel={(event) => {
            event.preventDefault()
            zoom(event.deltaY > 0 ? -0.06 : 0.06)
          }}
        >
          <div className="yy-pentest-fog-board" style={{ transform: `translate(${view.x}px, ${view.y}px) scale(${view.scale})` }}>
            {visibleDepths.map((depth) => (
              <div key={depth} className="yy-pentest-fog-column">
                <Text className="yy-readable-text yy-pentest-depth-label" size="xs">
                  深度 {depth}
                </Text>
                {graphNodes
                  .filter((node) => Math.max(0, node.depth) === depth)
                  .map((node) => {
                    const hidden = node.status === PenetrationFogState.Hidden
                    const active = selectedKey === node.topologyKey
                    return (
                      <button
                        key={node.topologyKey}
                        type="button"
                        aria-hidden={hidden}
                        tabIndex={hidden ? -1 : 0}
                        disabled={hidden}
                        data-state={node.status}
                        data-active={active || undefined}
                        className="yy-pentest-fog-node"
                        onClick={() => onSelect(node)}
                      >
                        <span className="yy-pentest-fog-node-icon">
                          <Icon path={hidden ? mdiLockOutline : node.isEntry ? mdiTarget : mdiShieldSearch} size={0.92} />
                        </span>
                        <span className="yy-pentest-fog-node-main">
                          <strong>{node.displayName}</strong>
                          <small>{fogLabel[node.status]}</small>
                        </span>
                        {!hidden ? (
                          <span className="yy-pentest-fog-node-progress">
                            {node.scoreSummary.solved}/{node.scoreSummary.total}
                          </span>
                        ) : null}
                      </button>
                    )
                  })}
              </div>
            ))}
            <div className="yy-pentest-mini-map" aria-hidden="true">
              {visibleDepths.map((depth) => (
                <i key={depth} style={{ height: `${Math.max(1, graphNodes.filter((node) => Math.max(0, node.depth) === depth).length) * 0.42}rem` }} />
              ))}
            </div>
          </div>
        </div>
        {visibleEdges.length ? (
          <div className="yy-pentest-path-strip" aria-label="已发现攻击路径">
            {visibleEdges.map(({ edge, source, target }) => (
              <button
                key={edge.id}
                type="button"
                className="yy-pentest-path-chip"
                data-state={edge.status}
                onClick={() => onSelect(target)}
              >
                <span>{source.displayName}</span>
                <i>{edge.label || '攻击路径'}</i>
                <strong>{target.displayName}</strong>
              </button>
            ))}
          </div>
        ) : null}
      </Stack>
    </YinyuPanel>
  )
}

const PenetrationPage: FC = () => {
  const { id } = useParams()
  const gameId = parseInt(id ?? '-1')
  const { config } = useConfig()
  const [workspace, setWorkspace] = useState<PenetrationWorkspaceModel>()
  const [selectedKey, setSelectedKey] = useState<string>()
  const [flags, setFlags] = useState<Record<number, string>>({})
  const [loading, setLoading] = useState(false)
  const [errorText, setErrorText] = useState<string>()
  const teamIdRef = useRef<number | undefined>(undefined)

  const load = useCallback(async (silent = false) => {
    if (gameId <= 0) return
    if (!silent) setLoading(true)
    setErrorText(undefined)
    try {
      const res = await penetrationPlayerApi.getWorkspace(gameId)
      teamIdRef.current = res.data.teamId
      setWorkspace(res.data)
      setSelectedKey((current) => {
        if (current && res.data.attackGraph.nodes.some((node) => node.topologyKey === current && node.status !== PenetrationFogState.Hidden)) {
          return current
        }

        return res.data.attackGraph.nodes.find((node) => node.status === PenetrationFogState.Accessible)?.topologyKey ??
          res.data.attackGraph.nodes.find((node) => node.status !== PenetrationFogState.Hidden)?.topologyKey
      })
    } catch {
      if (!silent) setErrorText('渗透环境尚未部署，或当前队伍暂无访问权限。')
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

    connection.on('ReceivedPenetrationAttackGraphUpdate', (update: PenetrationAttackGraphUpdateModel) => {
      if (update.gameId !== gameId) return
      if (teamIdRef.current && update.teamId !== teamIdRef.current) return

      void load(true)
      if (update.accepted && update.unlockedNodeCount > 0) {
        showNotification({
          color: 'teal',
          message: `新的攻击路径已解锁：${update.unlockedNodeCount} 个模块`,
          icon: <Icon path={mdiMapMarkerPath} size={1} />,
        })
      }
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

  const nodeByKey = useMemo(() => new Map((workspace?.nodes ?? []).map((node) => [node.topologyKey, node])), [workspace?.nodes])
  const tasks = useMemo(
    () => (workspace?.nodes ?? []).flatMap((node) => node.scoreItems.map((item) => ({ node, item }))),
    [workspace?.nodes]
  )
  const solvedItemIds = useMemo(() => new Set(tasks.filter(({ item }) => item.solved).map(({ item }) => item.id)), [tasks])
  const solvedItemKeys = useMemo(() => new Set(tasks.filter(({ item }) => item.solved).map(({ item }) => item.topologyKey).filter(Boolean)), [tasks])
  const selectedGraphNode = workspace?.attackGraph.nodes.find((node) => node.topologyKey === selectedKey)
  const selectedWorkspaceNode = selectedKey ? nodeByKey.get(selectedKey) : undefined
  const selectedTasks = selectedWorkspaceNode?.scoreItems ?? []
  const solvedCount = workspace?.attackGraph.solvedScoreItemCount ?? 0
  const totalCount = workspace?.attackGraph.totalScoreItemCount ?? 0
  const progress = totalCount > 0 ? Math.round((solvedCount / totalCount) * 100) : 0
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
                <YinyuRouteLoader title="渗透演练加载中" description="正在读取队伍环境、入口目标与攻击图状态" />
              </YinyuPanel>
            ) : null}

            {errorText && !workspace ? (
              <YinyuPanel p="xl">
                <Stack gap="xs">
                  <Badge variant="light">Penetration</Badge>
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
                <YinyuPanel p="md" className="yy-pentest-player-header">
                  <Group justify="space-between" align="center" wrap="wrap">
                    <Stack gap={6}>
                      <Group gap="xs">
                        <Badge variant="light">Black-box Penetration</Badge>
                        <YinyuStatusPill tone={workspace.status === PenetrationRuntimeStatus.Running ? 'success' : 'warm'}>
                          {statusLabel(workspace.status)}
                        </YinyuStatusPill>
                      </Group>
                      <Title order={2}>题目攻击图</Title>
                      <Text className="yy-readable-text">
                        {workspace.teamName} / 已突破 {workspace.attackGraph.completedNodeCount}/{workspace.attackGraph.totalNodeCount} 个模块 / 得分项 {solvedCount}/{totalCount}
                      </Text>
                      <Progress value={progress} size="sm" radius="xl" className="yy-pentest-progress" />
                    </Stack>
                    <Button leftSection={<Icon path={mdiRefresh} size={0.85} />} variant="light" disabled={loading || remainingReset <= 0} onClick={reset}>
                      重置环境（剩余 {remainingReset}）
                    </Button>
                  </Group>
                </YinyuPanel>

                <div className="yy-pentest-attack-layout">
                  <Stack gap="md" className="yy-pentest-attack-side">
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
                                  从这里开始侦察，完成检查点后解锁后续模块。
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
                        <Title order={4}>迷雾状态</Title>
                        {Object.values(PenetrationFogState).map((state) => (
                          <Group key={state} justify="space-between" className="yy-pentest-fog-legend">
                            <YinyuStatusPill tone={fogTone(state)}>{fogLabel[state]}</YinyuStatusPill>
                            <Text className="yy-readable-text" size="xs">
                              {workspace.attackGraph.nodes.filter((node) => node.status === state).length}
                            </Text>
                          </Group>
                        ))}
                      </Stack>
                    </YinyuPanel>
                  </Stack>

                  <AttackMap
                    graphNodes={workspace.attackGraph.nodes}
                    graphEdges={workspace.attackGraph.edges}
                    selectedKey={selectedKey}
                    onSelect={(node) => {
                      if (node.status !== PenetrationFogState.Hidden) {
                        setSelectedKey(node.topologyKey)
                      }
                    }}
                  />

                  <Stack gap="md" className="yy-pentest-attack-detail">
                    <YinyuPanel p="md">
                      <Stack gap="sm">
                        <Group justify="space-between" wrap="wrap">
                          <Stack gap={2}>
                            <Title order={4}>{selectedGraphNode?.displayName ?? '选择一个模块'}</Title>
                            <Text className="yy-readable-text" size="sm">
                              {selectedGraphNode?.description ?? '点击已驱散的模块查看任务状态。'}
                            </Text>
                          </Stack>
                          {selectedGraphNode ? (
                            <YinyuStatusPill tone={fogTone(selectedGraphNode.status)}>{fogLabel[selectedGraphNode.status]}</YinyuStatusPill>
                          ) : null}
                        </Group>
                        {selectedGraphNode ? (
                          <div className="yy-pentest-node-summary">
                            <span>任务 {selectedGraphNode.scoreSummary.solved}/{selectedGraphNode.scoreSummary.total}</span>
                            <span>检查点 {selectedGraphNode.scoreSummary.checkpointSolved}/{selectedGraphNode.scoreSummary.checkpointTotal}</span>
                            <span>分值 {selectedGraphNode.scoreSummary.solvedScore}/{selectedGraphNode.scoreSummary.totalScore}</span>
                          </div>
                        ) : null}
                      </Stack>
                    </YinyuPanel>

                    {selectedTasks.length ? (
                      selectedTasks.map((item) => {
                        const locked = item.prerequisiteItemKeys?.length
                          ? item.prerequisiteItemKeys.some((key) => !solvedItemKeys.has(key))
                          : item.prerequisiteItemIds.some((itemId) => !solvedItemIds.has(itemId))
                        return selectedWorkspaceNode ? (
                          <TaskCard
                            key={item.id}
                            node={selectedWorkspaceNode}
                            item={item}
                            locked={locked}
                            value={flags[item.id] ?? ''}
                            disabled={loading || selectedWorkspaceNode.fogState === PenetrationFogState.Revealed}
                            onChange={(value) => setFlags((current) => ({ ...current, [item.id]: value }))}
                            onSubmit={() => submit(item.id)}
                          />
                        ) : null
                      })
                    ) : (
                      <YinyuPanel p="md" className="yy-pentest-task-card">
                        <Stack gap="xs">
                          <Title order={4}>任务暂未开放</Title>
                          <Text className="yy-readable-text">
                            该模块还在迷雾中，或只是侦察到轮廓。完成前置检查点后，这里会显示可提交的任务。
                          </Text>
                        </Stack>
                      </YinyuPanel>
                    )}
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
