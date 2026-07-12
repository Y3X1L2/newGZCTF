import {
  ActionIcon,
  Badge,
  Button,
  Checkbox,
  Group,
  NumberInput,
  ScrollArea,
  Select,
  Stack,
  Table,
  Tabs,
  Text,
  TextInput,
  Textarea,
  Title,
  Tooltip,
} from '@mantine/core'
import { showNotification } from '@mantine/notifications'
import {
  mdiCheck,
  mdiContentSaveOutline,
  mdiDeleteOutline,
  mdiLan,
  mdiLinkVariant,
  mdiPlus,
  mdiPublish,
  mdiRefresh,
  mdiServer,
  mdiStop,
} from '@mdi/js'
import { Icon } from '@mdi/react'
import {
  Background,
  BackgroundVariant,
  Controls,
  MiniMap,
  ReactFlow,
  ReactFlowProvider,
  type Edge,
  type Node,
} from '@xyflow/react'
import '@xyflow/react/dist/style.css'
import { useCallback, useEffect, useMemo, useState } from 'react'
import { useParams } from 'react-router'
import { YinyuPanel, YinyuRouteLoader, YinyuTableShell } from '@Components/yinyu/YinyuUI'
import { showErrorMsg } from '@Utils/Shared'
import {
  PenetrationGameLabBindingModel,
  PenetrationObjectiveWriteModel,
  PenetrationRuntimeBindingModel,
  penetrationAdminApi,
} from '../../../../Api/PenetrationApi'
import {
  ImageTemplateLite,
  TeamLabAssetKind,
  TeamLabPlanModel,
  TeamLabReleaseModel,
  TeamLabTopologyAssetModel,
  TeamLabTopologyConnectionModel,
  TeamLabTopologyDetailModel,
  TeamLabTopologyEditorModel,
  TeamLabTopologyNetworkModel,
  TeamLabTopologySummaryModel,
  teamLabAdminApi,
} from '../../../../Api/TeamLabApi'
import { TeamLabRuntimeObservability } from './TeamLabRuntimeObservability'

type Selection = { kind: 'network' | 'asset'; key: string } | null

const emptyEditor = (): TeamLabTopologyEditorModel => ({ networks: {}, assets: {} })

const defaultNetwork = (index: number): TeamLabTopologyNetworkModel => {
  const pools = ['10.60.0.0/16', '172.20.0.0/16', '192.168.0.0/16']
  const key = `network-${crypto.randomUUID().slice(0, 8)}`
  return {
    key,
    name: index === 0 ? '入口网段' : `网段 ${index + 1}`,
    addressPool: { poolCidr: pools[index] ?? `10.${60 + index}.0.0/16`, runtimePrefixLength: 28 },
    isEntry: index === 0,
    orderIndex: index,
  }
}

const defaultObjective = (assetKey: string, index: number): PenetrationObjectiveWriteModel => ({
  key: `objective-${crypto.randomUUID().slice(0, 8)}`,
  assetKey,
  title: `目标 ${index + 1}`,
  description: '',
  category: 'General',
  score: 100,
  dynamic: true,
  flagTemplate: 'flag{[TEAM_HASH]}',
  maxAttempts: 0,
  visible: true,
  checkpoint: false,
  prerequisiteKeys: [],
  orderIndex: index,
})

const normalizeImageType = (value: unknown) => String(value ?? '').toLowerCase()

const PenetrationAdminPage = () => {
  const { id } = useParams()
  const gameId = Number(id ?? -1)
  const [loading, setLoading] = useState(true)
  const [busy, setBusy] = useState(false)
  const [topologies, setTopologies] = useState<TeamLabTopologySummaryModel[]>([])
  const [binding, setBinding] = useState<PenetrationGameLabBindingModel | null>(null)
  const [draft, setDraft] = useState<TeamLabTopologyDetailModel | null>(null)
  const [templates, setTemplates] = useState<ImageTemplateLite[]>([])
  const [objectives, setObjectives] = useState<PenetrationObjectiveWriteModel[]>([])
  const [maxResetCount, setMaxResetCount] = useState(3)
  const [releases, setReleases] = useState<TeamLabReleaseModel[]>([])
  const [plan, setPlan] = useState<TeamLabPlanModel | null>(null)
  const [runtimes, setRuntimes] = useState<PenetrationRuntimeBindingModel[]>([])
  const [selection, setSelection] = useState<Selection>(null)
  const [connectionFrom, setConnectionFrom] = useState<string | null>(null)
  const [connectionTo, setConnectionTo] = useState<string | null>(null)
  const [connectionVia, setConnectionVia] = useState<string | null>(null)

  const loadRuntimes = useCallback(async () => {
    if (gameId <= 0) return
    const response = await penetrationAdminApi.getRuntimes(gameId)
    setRuntimes(response.data)
  }, [gameId])

  const loadTopology = useCallback(async (topologyId: string) => {
    const [topologyResponse, releaseResponse] = await Promise.all([
      teamLabAdminApi.getTopology(topologyId),
      teamLabAdminApi.listReleases(topologyId),
    ])
    setDraft(topologyResponse.data)
    setReleases(releaseResponse.data)
    setSelection(null)
  }, [])

  const load = useCallback(async () => {
    if (gameId <= 0) return
    setLoading(true)
    try {
      const [topologyResponse, templateResponse, runtimeResponse] = await Promise.all([
        teamLabAdminApi.listTopologies(),
        fetch('/api/v1/image-templates?pageSize=200').then((response) => response.ok ? response.json() : []),
        penetrationAdminApi.getRuntimes(gameId),
      ])
      setTopologies(topologyResponse.data)
      setTemplates(templateResponse.items ?? templateResponse ?? [])
      setRuntimes(runtimeResponse.data)
      try {
        const bindingResponse = await penetrationAdminApi.getBinding(gameId)
        setBinding(bindingResponse.data)
        setObjectives(bindingResponse.data.objectives.map(({ id: _id, ...objective }) => objective))
        setMaxResetCount(bindingResponse.data.maxResetCount)
        await loadTopology(bindingResponse.data.topologyId)
      } catch {
        setBinding(null)
        setDraft(null)
        setObjectives([])
      }
    } finally {
      setLoading(false)
    }
  }, [gameId, loadTopology])

  useEffect(() => { void load() }, [load])

  const mutateDraft = useCallback((updater: (current: TeamLabTopologyDetailModel) => TeamLabTopologyDetailModel) => {
    setDraft((current) => current ? updater(current) : current)
    setPlan(null)
  }, [])

  const flowNodes = useMemo<Node[]>(() => {
    if (!draft) return []
    const networkNodes = draft.definition.networks.map((network, index) => {
      const position = draft.editor.networks[network.key] ?? { x: 70 + index * 330, y: 70 }
      return {
        id: `network:${network.key}`,
        position,
        data: { label: `${network.name}\n${network.addressPool.poolCidr}` },
        style: { width: 250, minHeight: 72, borderColor: network.isEntry ? '#40c057' : undefined, whiteSpace: 'pre-line' },
      }
    })
    const assetNodes = draft.definition.assets.map((asset, index) => {
      const position = draft.editor.assets[asset.key] ?? { x: 100 + index * 260, y: 250 }
      return {
        id: `asset:${asset.key}`,
        position,
        data: { label: `${asset.name}\n${asset.kind}` },
        style: { width: 210, minHeight: 64, whiteSpace: 'pre-line' },
      }
    })
    return [...networkNodes, ...assetNodes]
  }, [draft])

  const flowEdges = useMemo<Edge[]>(() => {
    if (!draft) return []
    const interfaceEdges = draft.definition.assets.flatMap((asset) => asset.interfaces.map((iface) => ({
      id: `interface:${asset.key}:${iface.key}`,
      source: `asset:${asset.key}`,
      target: `network:${iface.networkKey}`,
      label: iface.primary ? 'primary' : undefined,
    })))
    const routeEdges = draft.definition.connections.map((connection) => ({
      id: `connection:${connection.key}`,
      source: `network:${connection.fromNetworkKey}`,
      target: `network:${connection.toNetworkKey}`,
      label: draft.definition.assets.find((asset) => asset.key === connection.viaAssetKey)?.name ?? connection.viaAssetKey,
      animated: true,
    }))
    return [...interfaceEdges, ...routeEdges]
  }, [draft])

  const createTopology = async () => {
    setBusy(true)
    try {
      const network = defaultNetwork(0)
      const response = await teamLabAdminApi.createTopology({
        name: `比赛 ${gameId} 组网`,
        networks: [network],
        assets: [],
        connections: [],
        editor: { networks: { [network.key]: { x: 80, y: 80 } }, assets: {} },
      })
      await penetrationAdminApi.bindTopology(gameId, response.data.id)
      showNotification({ color: 'teal', message: 'TeamLab 拓扑已创建并绑定。' })
      await load()
    } catch (error) { showErrorMsg(error, (key) => key) } finally { setBusy(false) }
  }

  const bindTopology = async (topologyId: string | null) => {
    if (!topologyId) return
    setBusy(true)
    try {
      const response = await penetrationAdminApi.bindTopology(gameId, topologyId)
      setBinding(response.data)
      await loadTopology(topologyId)
    } catch (error) { showErrorMsg(error, (key) => key) } finally { setBusy(false) }
  }

  const save = async () => {
    if (!draft) return
    setBusy(true)
    try {
      const topology = await teamLabAdminApi.updateTopology(draft.id, {
        revision: draft.revision,
        ...draft.definition,
        editor: draft.editor,
      })
      const savedObjectives = await penetrationAdminApi.replaceObjectives(gameId, maxResetCount, objectives)
      setDraft(topology.data)
      setObjectives(savedObjectives.data.map(({ id: _id, ...objective }) => objective))
      showNotification({ color: 'teal', message: '拓扑与得分目标已保存。', icon: <Icon path={mdiCheck} size={0.8} /> })
    } catch (error) { showErrorMsg(error, (key) => key) } finally { setBusy(false) }
  }

  const validate = async () => {
    if (!draft) return
    setBusy(true)
    try {
      const response = await teamLabAdminApi.validateTopology(draft.id)
      showNotification({
        color: response.data.valid ? 'teal' : 'yellow',
        message: response.data.valid ? '拓扑校验通过。' : response.data.issues.map((issue) => issue.message).join('；'),
      })
    } catch (error) { showErrorMsg(error, (key) => key) } finally { setBusy(false) }
  }

  const publish = async () => {
    if (!draft) return
    setBusy(true)
    try {
      await save()
      const current = await teamLabAdminApi.getTopology(draft.id)
      const response = await teamLabAdminApi.publishTopology(draft.id, current.data.revision)
      await penetrationAdminApi.activateRelease(gameId, response.data.id)
      setBinding((value) => value ? { ...value, activeReleaseId: response.data.id } : value)
      setReleases((value) => [response.data, ...value.filter((item) => item.id !== response.data.id)])
      showNotification({ color: 'teal', message: `已发布 v${response.data.version}。`, icon: <Icon path={mdiPublish} size={0.8} /> })
    } catch (error) { showErrorMsg(error, (key) => key) } finally { setBusy(false) }
  }

  const previewPlan = async () => {
    if (!draft || !binding?.activeReleaseId) return
    setBusy(true)
    try { setPlan((await teamLabAdminApi.plan(draft.id, binding.activeReleaseId)).data) }
    catch (error) { showErrorMsg(error, (key) => key) } finally { setBusy(false) }
  }

  const deploy = async () => {
    setBusy(true)
    try {
      await penetrationAdminApi.deploy(gameId)
      showNotification({ color: 'teal', message: '队伍环境已进入部署队列。' })
      await loadRuntimes()
    } catch (error) { showErrorMsg(error, (key) => key) } finally { setBusy(false) }
  }

  const stop = async () => {
    setBusy(true)
    try { await penetrationAdminApi.stop(gameId); await loadRuntimes() }
    catch (error) { showErrorMsg(error, (key) => key) } finally { setBusy(false) }
  }

  const addNetwork = () => {
    if (!draft) return
    const network = defaultNetwork(draft.definition.networks.length)
    mutateDraft((current) => ({
      ...current,
      definition: { ...current.definition, networks: [...current.definition.networks, network] },
      editor: { ...current.editor, networks: { ...current.editor.networks, [network.key]: { x: 80 + current.definition.networks.length * 300, y: 80 } } },
    }))
    setSelection({ kind: 'network', key: network.key })
  }

  const addAsset = () => {
    if (!draft || !draft.definition.networks.length || !templates.length) return
    const template = templates[0]
    const network = draft.definition.networks[0]
    const asset: TeamLabTopologyAssetModel = {
      key: `asset-${crypto.randomUUID().slice(0, 8)}`,
      name: `资产 ${draft.definition.assets.length + 1}`,
      kind: normalizeImageType(template.imageType).includes('vm') ? TeamLabAssetKind.Vm : TeamLabAssetKind.Docker,
      imageTemplateId: template.id,
      resources: { cpuUnits: 10, memoryMiB: 512, storageMiB: 1024 },
      interfaces: [{ key: 'eth0', networkKey: network.key, hostOffset: 2 + draft.definition.assets.length, primary: true, orderIndex: 0 }],
      routingEnabled: false,
      exposePort: 80,
      environment: {},
      orderIndex: draft.definition.assets.length,
    }
    mutateDraft((current) => ({
      ...current,
      definition: { ...current.definition, assets: [...current.definition.assets, asset] },
      editor: { ...current.editor, assets: { ...current.editor.assets, [asset.key]: { x: 120 + current.definition.assets.length * 250, y: 280 } } },
    }))
    setSelection({ kind: 'asset', key: asset.key })
  }

  const deleteSelection = () => {
    if (!draft || !selection) return
    mutateDraft((current) => {
      if (selection.kind === 'network') {
        const networks = current.definition.networks.filter((item) => item.key !== selection.key)
        const assets = current.definition.assets.map((asset) => ({ ...asset, interfaces: asset.interfaces.filter((iface) => iface.networkKey !== selection.key) }))
        const connections = current.definition.connections.filter((item) => item.fromNetworkKey !== selection.key && item.toNetworkKey !== selection.key)
        return { ...current, definition: { ...current.definition, networks, assets, connections } }
      }
      return {
        ...current,
        definition: {
          ...current.definition,
          assets: current.definition.assets.filter((item) => item.key !== selection.key),
          connections: current.definition.connections.filter((item) => item.viaAssetKey !== selection.key),
        },
      }
    })
    if (selection.kind === 'asset') setObjectives((items) => items.filter((item) => item.assetKey !== selection.key))
    setSelection(null)
  }

  const selectedNetwork = selection?.kind === 'network' ? draft?.definition.networks.find((item) => item.key === selection.key) : undefined
  const selectedAsset = selection?.kind === 'asset' ? draft?.definition.assets.find((item) => item.key === selection.key) : undefined
  const networkOptions = draft?.definition.networks.map((item) => ({ value: item.key, label: item.name })) ?? []
  const routingOptions = draft?.definition.assets.filter((item) => item.routingEnabled).map((item) => ({ value: item.key, label: item.name })) ?? []

  if (loading) return <YinyuPanel p="xl"><YinyuRouteLoader title="TeamLab 加载中" /></YinyuPanel>

  if (!binding || !draft) {
    return (
      <YinyuPanel p="xl">
        <Stack gap="md">
          <Title order={2}>TeamLab 拓扑</Title>
          <Select label="绑定已有拓扑" data={topologies.map((item) => ({ value: item.id, label: item.name }))} onChange={(value) => void bindTopology(value)} searchable />
          <Button leftSection={<Icon path={mdiPlus} size={0.8} />} loading={busy} onClick={() => void createTopology()}>创建比赛拓扑</Button>
        </Stack>
      </YinyuPanel>
    )
  }

  return (
    <Stack gap="md">
      <YinyuPanel p="sm">
        <Group justify="space-between" wrap="wrap">
          <Group>
            <Title order={2}>{draft.definition.name}</Title>
            <Badge variant="light">revision {draft.revision}</Badge>
            {binding.activeReleaseId ? <Badge color="teal" variant="light">已发布</Badge> : <Badge color="yellow" variant="light">草稿</Badge>}
          </Group>
          <Group gap="xs">
            <Button variant="light" leftSection={<Icon path={mdiCheck} size={0.75} />} disabled={busy} onClick={() => void validate()}>校验</Button>
            <Button leftSection={<Icon path={mdiContentSaveOutline} size={0.75} />} disabled={busy} onClick={() => void save()}>保存</Button>
            <Button leftSection={<Icon path={mdiPublish} size={0.75} />} disabled={busy} onClick={() => void publish()}>发布</Button>
          </Group>
        </Group>
      </YinyuPanel>

      <Tabs defaultValue="topology" keepMounted={false}>
        <Tabs.List>
          <Tabs.Tab value="topology">拓扑设计</Tabs.Tab>
          <Tabs.Tab value="assets">资产配置</Tabs.Tab>
          <Tabs.Tab value="connections">连通关系</Tabs.Tab>
          <Tabs.Tab value="runtime">发布与运行</Tabs.Tab>
        </Tabs.List>

        <Tabs.Panel value="topology" pt="sm">
          <div style={{ display: 'grid', gridTemplateColumns: '220px minmax(0, 1fr) 300px', gap: 12, minHeight: 620 }}>
            <YinyuPanel p="sm">
              <Stack gap="xs">
                <Button leftSection={<Icon path={mdiLan} size={0.75} />} variant="light" onClick={addNetwork}>添加网段</Button>
                <Button leftSection={<Icon path={mdiServer} size={0.75} />} variant="light" disabled={!templates.length} onClick={addAsset}>添加资产</Button>
                <ScrollArea h={500}>
                  <Stack gap={4}>
                    {draft.definition.networks.map((item) => <Button key={item.key} variant={selection?.key === item.key ? 'filled' : 'subtle'} justify="flex-start" onClick={() => setSelection({ kind: 'network', key: item.key })}>{item.name}</Button>)}
                    {draft.definition.assets.map((item) => <Button key={item.key} variant={selection?.key === item.key ? 'filled' : 'subtle'} justify="flex-start" onClick={() => setSelection({ kind: 'asset', key: item.key })}>{item.name}</Button>)}
                  </Stack>
                </ScrollArea>
              </Stack>
            </YinyuPanel>
            <YinyuPanel p={0} style={{ overflow: 'hidden' }}>
              <div style={{ height: 620 }}>
                <ReactFlowProvider>
                  <ReactFlow
                    nodes={flowNodes}
                    edges={flowEdges}
                    fitView
                    onNodeClick={(_, node) => {
                      const [kind, key] = node.id.split(':')
                      setSelection({ kind: kind as 'network' | 'asset', key })
                    }}
                    onNodeDragStop={(_, node) => mutateDraft((current) => ({
                      ...current,
                      editor: {
                        ...current.editor,
                        [node.id.startsWith('network:') ? 'networks' : 'assets']: {
                          ...current.editor[node.id.startsWith('network:') ? 'networks' : 'assets'],
                          [node.id.split(':')[1]]: { x: node.position.x, y: node.position.y },
                        },
                      },
                    }))}
                  >
                    <Background variant={BackgroundVariant.Dots} gap={24} size={1} />
                    <MiniMap pannable zoomable />
                    <Controls />
                  </ReactFlow>
                </ReactFlowProvider>
              </div>
            </YinyuPanel>
            <YinyuPanel p="sm">
              <Stack gap="sm">
                {selectedNetwork ? <>
                  <TextInput label="网段名称" value={selectedNetwork.name} onChange={(event) => mutateDraft((current) => ({ ...current, definition: { ...current.definition, networks: current.definition.networks.map((item) => item.key === selectedNetwork.key ? { ...item, name: event.currentTarget.value } : item) } }))} />
                  <TextInput label="地址池" value={selectedNetwork.addressPool.poolCidr} onChange={(event) => mutateDraft((current) => ({ ...current, definition: { ...current.definition, networks: current.definition.networks.map((item) => item.key === selectedNetwork.key ? { ...item, addressPool: { ...item.addressPool, poolCidr: event.currentTarget.value } } : item) } }))} />
                  <NumberInput label="运行网段前缀" min={16} max={30} value={selectedNetwork.addressPool.runtimePrefixLength} onChange={(value) => mutateDraft((current) => ({ ...current, definition: { ...current.definition, networks: current.definition.networks.map((item) => item.key === selectedNetwork.key ? { ...item, addressPool: { ...item.addressPool, runtimePrefixLength: Number(value) } } : item) } }))} />
                  <Checkbox label="入口网段" checked={selectedNetwork.isEntry} onChange={() => mutateDraft((current) => ({ ...current, definition: { ...current.definition, networks: current.definition.networks.map((item) => ({ ...item, isEntry: item.key === selectedNetwork.key })) } }))} />
                </> : null}
                {selectedAsset ? <>
                  <TextInput label="资产名称" value={selectedAsset.name} onChange={(event) => mutateDraft((current) => ({ ...current, definition: { ...current.definition, assets: current.definition.assets.map((item) => item.key === selectedAsset.key ? { ...item, name: event.currentTarget.value } : item) } }))} />
                  <Select label="镜像模板" searchable data={templates.map((item) => ({ value: String(item.id), label: item.name }))} value={String(selectedAsset.imageTemplateId)} onChange={(value) => value && mutateDraft((current) => ({ ...current, definition: { ...current.definition, assets: current.definition.assets.map((item) => item.key === selectedAsset.key ? { ...item, imageTemplateId: Number(value) } : item) } }))} />
                  <Select label="主网段" data={networkOptions} value={selectedAsset.interfaces.find((item) => item.primary)?.networkKey ?? null} onChange={(value) => value && mutateDraft((current) => ({ ...current, definition: { ...current.definition, assets: current.definition.assets.map((item) => item.key === selectedAsset.key ? { ...item, interfaces: item.interfaces.map((iface) => ({ ...iface, networkKey: value })) } : item) } }))} />
                  <Checkbox label="启用路由" checked={selectedAsset.routingEnabled} onChange={(event) => mutateDraft((current) => ({ ...current, definition: { ...current.definition, assets: current.definition.assets.map((item) => item.key === selectedAsset.key ? { ...item, routingEnabled: event.currentTarget.checked } : item) } }))} />
                </> : null}
                {selection ? <Button color="red" variant="light" leftSection={<Icon path={mdiDeleteOutline} size={0.7} />} onClick={deleteSelection}>删除</Button> : <Text size="sm" c="dimmed">选择网段或资产</Text>}
              </Stack>
            </YinyuPanel>
          </div>
        </Tabs.Panel>

        <Tabs.Panel value="assets" pt="sm">
          <Stack gap="sm">
            <YinyuTableShell p="xs">
              <Group justify="space-between" mb="xs"><Text fw={800}>得分目标</Text><Group><NumberInput w={150} label="重置上限" min={0} max={100} value={maxResetCount} onChange={(value) => setMaxResetCount(Number(value))} /><Button leftSection={<Icon path={mdiPlus} size={0.7} />} disabled={!draft.definition.assets.length} onClick={() => setObjectives((items) => [...items, defaultObjective(draft.definition.assets[0].key, items.length)])}>添加目标</Button></Group></Group>
              <Table.ScrollContainer minWidth={900}><Table><Table.Thead><Table.Tr><Table.Th>标题</Table.Th><Table.Th>资产</Table.Th><Table.Th>分值</Table.Th><Table.Th>Flag</Table.Th><Table.Th>前置目标</Table.Th><Table.Th /></Table.Tr></Table.Thead><Table.Tbody>
                {objectives.map((objective, index) => <Table.Tr key={objective.key}>
                  <Table.Td><TextInput value={objective.title} onChange={(event) => setObjectives((items) => items.map((item, itemIndex) => itemIndex === index ? { ...item, title: event.currentTarget.value } : item))} /><Textarea mt={4} minRows={2} value={objective.description ?? ''} onChange={(event) => setObjectives((items) => items.map((item, itemIndex) => itemIndex === index ? { ...item, description: event.currentTarget.value } : item))} /></Table.Td>
                  <Table.Td><Select data={draft.definition.assets.map((item) => ({ value: item.key, label: item.name }))} value={objective.assetKey} onChange={(value) => value && setObjectives((items) => items.map((item, itemIndex) => itemIndex === index ? { ...item, assetKey: value } : item))} /></Table.Td>
                  <Table.Td><NumberInput min={0} value={objective.score} onChange={(value) => setObjectives((items) => items.map((item, itemIndex) => itemIndex === index ? { ...item, score: Number(value) } : item))} /></Table.Td>
                  <Table.Td><Checkbox label="动态" checked={objective.dynamic} onChange={(event) => setObjectives((items) => items.map((item, itemIndex) => itemIndex === index ? { ...item, dynamic: event.currentTarget.checked } : item))} /><TextInput mt={4} value={objective.dynamic ? objective.flagTemplate ?? '' : objective.staticFlag ?? ''} onChange={(event) => setObjectives((items) => items.map((item, itemIndex) => itemIndex === index ? objective.dynamic ? { ...item, flagTemplate: event.currentTarget.value } : { ...item, staticFlag: event.currentTarget.value } : item))} /></Table.Td>
                  <Table.Td><Select data={objectives.filter((_, itemIndex) => itemIndex !== index).map((item) => ({ value: item.key, label: item.title }))} value={objective.prerequisiteKeys[0] ?? null} clearable onChange={(value) => setObjectives((items) => items.map((item, itemIndex) => itemIndex === index ? { ...item, prerequisiteKeys: value ? [value] : [] } : item))} /></Table.Td>
                  <Table.Td><Tooltip label="删除"><ActionIcon color="red" variant="subtle" onClick={() => setObjectives((items) => items.filter((_, itemIndex) => itemIndex !== index))}><Icon path={mdiDeleteOutline} size={0.75} /></ActionIcon></Tooltip></Table.Td>
                </Table.Tr>)}
              </Table.Tbody></Table></Table.ScrollContainer>
            </YinyuTableShell>
          </Stack>
        </Tabs.Panel>

        <Tabs.Panel value="connections" pt="sm">
          <YinyuPanel p="md">
            <Stack gap="md">
              <Group align="flex-end"><Select label="起点网段" data={networkOptions} value={connectionFrom} onChange={setConnectionFrom} /><Select label="终点网段" data={networkOptions} value={connectionTo} onChange={setConnectionTo} /><Select label="路由资产" data={routingOptions} value={connectionVia} onChange={setConnectionVia} /><Button leftSection={<Icon path={mdiLinkVariant} size={0.75} />} disabled={!connectionFrom || !connectionTo || !connectionVia || connectionFrom === connectionTo} onClick={() => {
                const connection: TeamLabTopologyConnectionModel = { key: `connection-${crypto.randomUUID().slice(0, 8)}`, fromNetworkKey: connectionFrom!, toNetworkKey: connectionTo!, viaAssetKey: connectionVia! }
                mutateDraft((current) => ({ ...current, definition: { ...current.definition, connections: [...current.definition.connections, connection] } }))
                setConnectionFrom(null); setConnectionTo(null); setConnectionVia(null)
              }}>添加关系</Button></Group>
              <Table><Table.Thead><Table.Tr><Table.Th>起点</Table.Th><Table.Th>终点</Table.Th><Table.Th>路由资产</Table.Th><Table.Th /></Table.Tr></Table.Thead><Table.Tbody>{draft.definition.connections.map((connection) => <Table.Tr key={connection.key}><Table.Td>{draft.definition.networks.find((item) => item.key === connection.fromNetworkKey)?.name}</Table.Td><Table.Td>{draft.definition.networks.find((item) => item.key === connection.toNetworkKey)?.name}</Table.Td><Table.Td>{draft.definition.assets.find((item) => item.key === connection.viaAssetKey)?.name}</Table.Td><Table.Td><ActionIcon color="red" variant="subtle" onClick={() => mutateDraft((current) => ({ ...current, definition: { ...current.definition, connections: current.definition.connections.filter((item) => item.key !== connection.key) } }))}><Icon path={mdiDeleteOutline} size={0.75} /></ActionIcon></Table.Td></Table.Tr>)}</Table.Tbody></Table>
            </Stack>
          </YinyuPanel>
        </Tabs.Panel>

        <Tabs.Panel value="runtime" pt="sm">
          <Stack gap="md">
            <YinyuPanel p="md"><Group justify="space-between" wrap="wrap"><Group><Select label="活动版本" data={releases.map((item) => ({ value: item.id, label: `v${item.version}` }))} value={binding.activeReleaseId ?? null} onChange={(value) => value && penetrationAdminApi.activateRelease(gameId, value).then((response) => setBinding(response.data)).catch((error) => showErrorMsg(error, (key) => key))} /><Button variant="light" leftSection={<Icon path={mdiRefresh} size={0.75} />} disabled={!binding.activeReleaseId || busy} onClick={() => void previewPlan()}>分片预览</Button></Group><Group><Button leftSection={<Icon path={mdiServer} size={0.75} />} disabled={!binding.activeReleaseId || busy} onClick={() => void deploy()}>部署环境</Button><Button color="red" variant="light" leftSection={<Icon path={mdiStop} size={0.75} />} disabled={busy} onClick={() => void stop()}>停止全部</Button></Group></Group></YinyuPanel>
            {plan ? <YinyuTableShell p="xs"><Table><Table.Thead><Table.Tr><Table.Th>分片</Table.Th><Table.Th>网段</Table.Th><Table.Th>资产</Table.Th><Table.Th>Docker / VM</Table.Th></Table.Tr></Table.Thead><Table.Tbody>{plan.shards.map((shard) => <Table.Tr key={shard.key}><Table.Td>{shard.key}</Table.Td><Table.Td>{shard.networkKeys.join(', ')}</Table.Td><Table.Td>{shard.assetKeys.join(', ')}</Table.Td><Table.Td>{shard.dockerSlots} / {shard.vmSlots}</Table.Td></Table.Tr>)}</Table.Tbody></Table></YinyuTableShell> : null}
            <TeamLabRuntimeObservability runtimes={runtimes} busy={busy} onRefresh={() => void loadRuntimes()} onRebuild={(teamId) => penetrationAdminApi.rebuildTeam(gameId, teamId).then(loadRuntimes).catch((error) => showErrorMsg(error, (key) => key))} onCleanup={(teamId) => penetrationAdminApi.cleanupTeam(gameId, teamId).then(loadRuntimes).catch((error) => showErrorMsg(error, (key) => key))} />
          </Stack>
        </Tabs.Panel>
      </Tabs>
    </Stack>
  )
}

export default PenetrationAdminPage
