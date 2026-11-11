import { ActionIcon, Button, Menu, Modal, SegmentedControl, Select, TextInput } from '@mantine/core'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import {
  ArrowLeft,
  Box,
  ChevronDown,
  GitBranch,
  LayoutGrid,
  List,
  ListChecks,
  MoreHorizontal,
  Monitor,
  Network,
  Pencil,
  Play,
  Plus,
  Search,
  Save,
  Trash2,
  KeyRound,
  Activity,
  Gauge,
  Router,
} from 'lucide-react'
import { AnimatePresence } from 'motion/react'
import { lazy, Suspense, useMemo, useState } from 'react'
import { useWorkspaceApi, type Asset, type Network as NetworkModel, type Schema } from '../../api/client'
import { Empty, ErrorMessage, Loading } from '../../foundation/Feedback'
import motionStyles from '../../foundation/Motion.module.css'
import { Status } from '../../foundation/Status'
import { randomUUID } from '../../foundation/id'
import { allows, allowsProject } from '../access/permissions'
import { SaveBlueprintDialog } from '../templates/SaveBlueprintDialog'
import { Link, useParams, useSearchParams, useWorkspaceRoute } from './../../routing'
import { ConnectionDialog } from './ConnectionDialog'
import { ExternalDeviceEditor } from './ExternalDeviceEditor'
import { FileWorkspace } from './FileWorkspace'
import { LogDrawer } from './LogDrawer'
import { MigrationDialog } from './MigrationDialog'
import { NetworkList } from './NetworkList'
import { AssetEditor, NetworkEditor } from './ObjectEditors'
import { ObjectInspector } from './ObjectInspector'
import { ObjectLocator } from './ObjectLocator'
import { RecoveryDrawer } from './RecoveryDrawer'
import { RouterEditor } from './RouterEditor'
import { ServiceDrawer } from './ServiceDrawer'
import { StorageUsage } from './StorageUsage'
import { TaskTray } from './TaskTray'
import { TopologyCanvas } from './TopologyCanvas'
import { VPNDrawer } from './VPNDrawer'
import styles from './WorkbenchPage.module.css'
import { consoleHref } from './consoles'
import { routerGateways } from './networking'
import { connectAsset, connectRouter } from './topology'
import { useTopology } from './useTopology'
import { useVPNAccess } from './useVPNAccess'
import { useWorkbench } from './useWorkbench'

const ObservationWorkspace = lazy(() => import('./ObservationWorkspace'))
const TrafficWorkspace = lazy(() => import('./TrafficWorkspace'))
const PerformanceWorkspace = lazy(() => import('./PerformanceWorkspace'))
type WorkbenchView = 'topology' | 'assets' | 'traffic' | 'performance'

export function WorkbenchPage() {
  const { id = '' } = useParams()
  return <Workbench key={id} id={id} />
}

function Workbench({ id }: { id: string }) {
  const api = useWorkspaceApi()
  const route = useWorkspaceRoute()
  const { environmentOnly } = route
  const workbench = useWorkbench(id)
  const queryClient = useQueryClient()
  const identity = useQuery({ queryKey: ['identity'], queryFn: api.identity })
  const [selection, setSelection] = useState<string>()
  const [focusRequest, setFocusRequest] = useState<{ id: string }>()
  const [tasksOpened, setTasksOpened] = useState(false)
  const [searchParams, setSearchParams] = useSearchParams()
  const requestedView = searchParams.get('view')
  const view: WorkbenchView =
    requestedView === 'assets' || requestedView === 'traffic' || requestedView === 'performance'
      ? requestedView
      : 'topology'
  const setView = (value: WorkbenchView) =>
    setSearchParams(
      (params) => {
        if (value === 'topology') params.delete('view')
        else params.set('view', value)
        return params
      },
      { replace: true }
    )
  const list = view === 'assets',
    traffic = view === 'traffic',
    performance = view === 'performance'
  const [query, setQuery] = useState('')
  const [assetFilter, setAssetFilter] = useState('all')
  const [listKind, setListKind] = useState('assets')
  const [editor, setEditor] = useState<'asset' | 'network' | 'router' | 'external' | 'external-network'>()
  const [editingObject, setEditingObject] = useState(false)
  const [destroying, setDestroying] = useState(false)
  const [resetting, setResetting] = useState(false)
  const [recoveryOpened, setRecoveryOpened] = useState(false)
  const [rebuilding, setRebuilding] = useState<Asset>()
  const [migrating, setMigrating] = useState<Asset>()
  const [savingBlueprint, setSavingBlueprint] = useState(false)
  const [logging, setLogging] = useState<Asset>()
  const [observationOpened, setObservationOpened] = useState(false)
  const [serving, setServing] = useState<string>()
  const [vpnOpened, setVPNOpened] = useState(false)
  const [filesOpened, setFilesOpened] = useState(false)
  const [connectionSettings, setConnectionSettings] = useState<{
    asset: Asset
    kind: 'ssh' | 'rdp'
  }>()
  const [context, setContext] = useState<{
    x: number
    y: number
    id: string
  }>()
  const environment = workbench.environment.data
  const vpn = useVPNAccess(
    id,
    vpnOpened,
    environment,
    workbench.operations.data ?? [],
    workbench.state.data?.operation,
    workbench.refresh
  )
  const canCompose = allows(environment, 'compose')
  const canOperate = allows(environment, 'operate')
  const canManage = allows(environment, 'manage')
  const canRead = allows(environment, 'read')
  const templates = workbench.templates.data ?? []
  const spec = workbench.editing
    ? workbench.spec
    : (environment?.appliedSpec ?? environment?.spec ?? { assets: [], networks: [] })
  const templatesById = useMemo(() => new Map(templates.map((template) => [template.id, template])), [templates])
  const assetStates = useMemo(
    () => new Map((workbench.state.data?.assets ?? []).map((asset) => [asset.assetId, asset.state])),
    [workbench.state.data?.assets]
  )
  const layout = useTopology(spec, templates)
  const graph = useMemo(
    () => ({
      edges: layout.data?.edges ?? [],
      nodes: (layout.data?.nodes ?? []).map((node) => ({
        ...node,
        data: { ...node.data, state: assetStates.get(node.id) ?? 'draft' },
      })),
    }),
    [layout.data, assetStates]
  )
  const asset = spec.assets.find((item) => item.id === selection)
  const router = spec.routers?.find((item) => item.id === selection)
  const externalDevices = spec.networks.flatMap((network) =>
    (network.devices ?? []).map((device) => ({ device, network }))
  )
  const externalDevice = externalDevices.find((item) => item.device.id === selection)
  const listedExternalDevices =
    assetFilter === 'all'
      ? externalDevices.filter(({ device }) =>
          `${device.name} ${device.address} ${device.mac}`.toLocaleLowerCase().includes(query.toLocaleLowerCase())
        )
      : []
  const listedAssets = spec.assets.filter(
    (item) =>
      (assetFilter === 'all' || assetStates.get(item.id) === assetFilter) &&
      [item.name, ...item.interfaces.map((iface) => iface.address)]
        .join(' ')
        .toLocaleLowerCase()
        .includes(query.toLocaleLowerCase())
  )
  const fileAsset =
    filesOpened && !workbench.editing && asset && allows(environment, 'file', asset.id) ? asset : undefined
  const servedAsset = spec.assets.find((item) => item.id === serving)
  const observedAsset =
    !workbench.editing && observationOpened && asset && allows(environment, 'observe', asset.id) ? asset : undefined
  const connect = (kind: Schema<'ConsoleKind'>) => {
    if (asset) window.open(consoleHref(route, id, asset.id, kind), '_blank', 'noopener,noreferrer')
  }
  const network = spec.networks.find((item) => item.id === selection)
  const status = workbench.state.data?.status ?? environment?.status ?? 'unknown'
  const busy = ['deploying', 'changing', 'destroying'].includes(status)
  const activeAction = ['running', 'suspended'].includes(status)
    ? status === 'suspended'
      ? 'resume'
      : 'stop'
    : 'start'
  const operation = workbench.state.data?.operation
  const latestOperation = operation ?? workbench.operations.data?.[0]
  const switchView = (value: typeof view) => {
    setView(value)
    setFilesOpened(false)
    setObservationOpened(false)
  }
  const beginEdit = () => {
    setView(list ? 'assets' : 'topology')
    setObservationOpened(false)
    setFilesOpened(false)
    workbench.beginEdit()
  }
  const adding = (kind: 'asset' | 'network' | 'router' | 'external' | 'external-network') => {
    if (!workbench.editing) beginEdit()
    setEditingObject(false)
    setEditor(kind)
  }
  const editObject = () => {
    setEditingObject(true)
    setEditor(externalDevice ? 'external' : router ? 'router' : asset ? 'asset' : 'network')
  }
  const updateAsset = (value: Asset) => {
    workbench.setSpec((current) => ({
      ...current,
      assets: current.assets.some((item) => item.id === value.id)
        ? current.assets.map((item) => (item.id === value.id ? value : item))
        : [...current.assets, value],
    }))
    setSelection(value.id)
    setEditor(undefined)
  }
  const updateNetwork = (value: NetworkModel, routerIds?: string[]) => {
    workbench.setSpec((current) => {
      const routers = current.routers?.map((item) => {
        if (!routerIds) return item
        const existing = item.interfaces.find((iface) => iface.networkId === value.id)
        if (existing && routerIds.includes(item.id)) return item
        return {
          ...item,
          interfaces: [
            ...item.interfaces.filter((iface) => iface.networkId !== value.id),
            ...(routerIds.includes(item.id)
              ? [
                  {
                    id: randomUUID(),
                    networkId: value.id,
                    address: '',
                  },
                ]
              : []),
          ],
        }
      })
      const networks = current.networks.some((item) => item.id === value.id)
        ? current.networks.map((item) => (item.id === value.id ? value : item))
        : [...current.networks, value]
      return {
        ...current,
        networks: routerGateways(networks, current.routers ?? [], routers ?? []),
        routers,
        routes: current.routes?.filter(
          (route) =>
            !routerIds ||
            route.networkId !== value.id ||
            (route.routerId !== undefined && routerIds.includes(route.routerId))
        ),
      }
    })
    setSelection(value.id)
    setEditor(editor === 'external-network' ? 'external' : undefined)
  }
  const updateRouter = (value: Schema<'LogicalRouter'>, routes: Schema<'Route'>[]) => {
    workbench.setSpec((current) => {
      const routers = current.routers?.some((item) => item.id === value.id)
        ? current.routers.map((item) => (item.id === value.id ? value : item))
        : [...(current.routers ?? []), value]
      return {
        ...current,
        routers,
        networks: routerGateways(current.networks, current.routers ?? [], routers),
        routes: [...(current.routes ?? []).filter((route) => route.routerId !== value.id), ...routes],
      }
    })
    setSelection(value.id)
    setEditor(undefined)
  }
  const remove = () => {
    workbench.setSpec((current) => {
      const routers = current.routers
        ?.filter((item) => item.id !== selection)
        .map((item) => ({
          ...item,
          interfaces: item.interfaces.filter((iface) => iface.networkId !== selection),
        }))
      return {
        ...current,
        assets: current.assets
          .filter((item) => item.id !== selection)
          .map((item) => ({
            ...item,
            interfaces: item.interfaces.filter((iface) => iface.networkId !== selection),
          })),
        networks: routerGateways(
          current.networks
            .filter((item) => item.id !== selection)
            .map((item) => ({
              ...item,
              devices: item.devices?.filter((device) => device.id !== selection),
            })),
          current.routers ?? [],
          routers ?? []
        ),
        routers,
        routes: current.routes?.filter((item) => item.networkId !== selection && item.routerId !== selection),
        policies: current.policies?.filter((item) => item.networkId !== selection),
        services: current.services?.filter((service) =>
          current.assets.some(
            (item) =>
              item.id === service.assetId &&
              item.id !== selection &&
              item.interfaces.some((iface) => iface.id === service.interfaceId && iface.networkId !== selection)
          )
        ),
      }
    })
    setSelection(undefined)
  }
  const duplicate = () => {
    if (asset)
      updateAsset({
        ...structuredClone(asset),
        id: randomUUID(),
        name: `${asset.name}-副本`,
        interfaces: asset.interfaces.map((item) => ({
          ...item,
          id: randomUUID(),
          address: '',
          mac: '',
        })),
        volumes: asset.volumes?.map((volume) => ({
          ...volume,
          id: randomUUID(),
        })),
      })
  }
  const errors = [
    workbench.environment.error,
    workbench.templates.error,
    workbench.state.error,
    workbench.action.error,
    workbench.retry.error,
    workbench.saveDraft.error,
    workbench.discard.error,
    workbench.saveView.error,
    layout.error,
  ]
  if (workbench.environment.isPending) return <Loading />
  if (!environment)
    return (
      <main className="collection-page">
        <ErrorMessage error={workbench.environment.error} />
      </main>
    )
  return (
    <main className={styles.workbench} data-editing={workbench.editing}>
      <header className={styles.header}>
        <div className={styles.identity}>
          {!environmentOnly && (
            <ActionIcon component={Link} to="/environments" variant="subtle" color="gray" aria-label="返回环境列表">
              <ArrowLeft size={18} />
            </ActionIcon>
          )}
          <div className={styles.title}>
            <h1>{environment.name}</h1>
            <Status value={status} />
            {canRead && <StorageUsage id={id} revision={environment.revision} />}
          </div>
          {workbench.editing && <span className={styles.editing}>调整中</span>}
        </div>
        <div className={styles.actions}>
          {environment.permissions?.includes('access') && (
            <Button
              variant="default"
              aria-label="VPN"
              title="VPN"
              className={styles.utility}
              leftSection={<KeyRound size={15} />}
              onClick={() => setVPNOpened(true)}
            >
              VPN
            </Button>
          )}
          {workbench.editing ? (
            <>
              <Button
                variant="default"
                onClick={() => workbench.saveDraft.mutate()}
                loading={workbench.saveDraft.isPending}
              >
                保存草稿
              </Button>
              <Button
                leftSection={<GitBranch size={15} />}
                onClick={() => workbench.preview.mutate()}
                loading={workbench.preview.isPending}
              >
                预览变更
              </Button>
              <Menu>
                <Menu.Target>
                  <ActionIcon variant="default" aria-label="草稿操作">
                    <MoreHorizontal size={18} />
                  </ActionIcon>
                </Menu.Target>
                <Menu.Dropdown>
                  <Menu.Item leftSection={<Save size={15} />} onClick={() => setSavingBlueprint(true)}>
                    保存为环境模板
                  </Menu.Item>
                  <Menu.Item onClick={() => workbench.saveDraft.mutate()}>返回现场</Menu.Item>
                  <Menu.Item color="red" leftSection={<Trash2 size={15} />} onClick={() => workbench.discard.mutate()}>
                    放弃草稿
                  </Menu.Item>
                </Menu.Dropdown>
              </Menu>
            </>
          ) : (
            <>
              {canCompose && (
                <Button
                  variant="default"
                  leftSection={<Pencil size={15} />}
                  onClick={beginEdit}
                  disabled={busy || status === 'destroyed'}
                >
                  {environment.draft ? '继续调整' : '调整环境'}
                </Button>
              )}
              {canOperate && (
                <Button
                  variant={activeAction === 'stop' ? 'outline' : 'filled'}
                  color={activeAction === 'stop' ? 'red' : undefined}
                  leftSection={activeAction === 'start' ? <Play size={15} /> : undefined}
                  loading={workbench.action.isPending}
                  disabled={busy || status === 'destroyed'}
                  onClick={() => workbench.action.mutate({ action: activeAction })}
                >
                  {activeAction === 'start' ? '运行' : activeAction === 'resume' ? '继续运行' : '停止'}
                </Button>
              )}
              {(canRead || canCompose || canOperate || canManage) && (
                <Menu position="bottom-end">
                  <Menu.Target>
                    <ActionIcon variant="default" aria-label="环境操作">
                      <MoreHorizontal size={18} />
                    </ActionIcon>
                  </Menu.Target>
                  <Menu.Dropdown>
                    {canRead && <Menu.Item onClick={() => setRecoveryOpened(true)}>恢复点</Menu.Item>}
                    {canCompose && (
                      <Menu.Item
                        color="red"
                        disabled={busy || status === 'destroyed'}
                        onClick={() => setResetting(true)}
                      >
                        全面重建环境
                      </Menu.Item>
                    )}
                    {canCompose && (
                      <Menu.Item leftSection={<Save size={15} />} onClick={() => setSavingBlueprint(true)}>
                        保存为环境模板
                      </Menu.Item>
                    )}
                    {canOperate && (
                      <>
                        <Menu.Divider />
                        {status === 'suspended' && (
                          <Menu.Item color="red" onClick={() => workbench.action.mutate({ action: 'stop' })}>
                            停止环境
                          </Menu.Item>
                        )}
                        <Menu.Item
                          disabled={busy || status !== 'running'}
                          onClick={() => workbench.action.mutate({ action: 'suspend' })}
                        >
                          暂停环境
                        </Menu.Item>
                        <Menu.Item
                          disabled={busy || status !== 'running'}
                          onClick={() => workbench.action.mutate({ action: 'reboot' })}
                        >
                          重启环境
                        </Menu.Item>
                      </>
                    )}
                    {canManage && (
                      <>
                        <Menu.Divider />
                        <Menu.Item
                          color="red"
                          disabled={busy || status === 'destroyed'}
                          leftSection={<Trash2 size={15} />}
                          onClick={() => setDestroying(true)}
                        >
                          销毁环境
                        </Menu.Item>
                      </>
                    )}
                  </Menu.Dropdown>
                </Menu>
              )}
            </>
          )}
        </div>
      </header>
      {errors.map((error, index) => (
        <ErrorMessage key={index} error={error} />
      ))}
      {environment.error && (
        <div className={styles.error} role="alert">
          <span>{environment.error}</span>
          <button onClick={() => setTasksOpened(true)}>查看任务</button>
        </div>
      )}
      {!observedAsset && (
        <div className={styles.toolbar}>
          <div className={styles.viewControls}>
            <SegmentedControl
              className={styles.views}
              aria-label="工作区视图"
              value={view}
              onChange={(value) => switchView(value as WorkbenchView)}
              data={[
                { value: 'topology', label: '拓扑', icon: LayoutGrid },
                { value: 'assets', label: '资产', icon: List },
                { value: 'traffic', label: '流量', icon: Activity },
                { value: 'performance', label: '性能', icon: Gauge },
              ]
                .filter(
                  (item) =>
                    ['topology', 'assets'].includes(item.value) ||
                    (!workbench.editing && allows(environment, 'observe'))
                )
                .map(({ value, label, icon: Icon }) => ({
                  value,
                  label: (
                    <span className={styles.viewLabel}>
                      <Icon size={15} />
                      <span>{label}</span>
                    </span>
                  ),
                }))}
            />
            {!fileAsset && view === 'topology' && (
              <ObjectLocator
                spec={spec}
                templates={templates}
                onSelect={(id) => {
                  setSelection(id)
                  setFocusRequest({ id })
                }}
              />
            )}
          </div>
          <div className={styles.addControls}>
            {workbench.editing && (
              <Menu position="bottom-end">
                <Menu.Target>
                  <Button
                    variant="default"
                    size="xs"
                    leftSection={<Plus size={14} />}
                    rightSection={<ChevronDown size={13} />}
                  >
                    添加
                  </Button>
                </Menu.Target>
                <Menu.Dropdown>
                  <Menu.Item leftSection={<Box size={16} />} onClick={() => adding('asset')}>
                    资产
                  </Menu.Item>
                  <Menu.Item leftSection={<Network size={16} />} onClick={() => adding('network')}>
                    交换机
                  </Menu.Item>
                  <Menu.Item leftSection={<Router size={16} />} onClick={() => adding('router')}>
                    路由器
                  </Menu.Item>
                  {identity.data?.administrator && (
                    <Menu.Item leftSection={<Router size={16} />} onClick={() => adding('external')}>
                      外部设备
                    </Menu.Item>
                  )}
                </Menu.Dropdown>
              </Menu>
            )}
            <span className={styles.count}>
              {spec.assets.length} 资产 · {spec.networks.length} 网段
              {Boolean(spec.routers?.length) && ` · ${spec.routers?.length} 路由器`}
              {externalDevices.length > 0 && ` · ${externalDevices.length} 外部设备`}
            </span>
            <button
              className={styles.taskButton}
              aria-label="运行任务"
              aria-expanded={tasksOpened}
              onClick={() => setTasksOpened(true)}
            >
              <ListChecks size={16} />
              <span>任务</span>
              {latestOperation && <Status value={latestOperation.state} />}
            </button>
          </div>
        </div>
      )}
      <div className={styles.body}>
        <section className={styles.canvas} data-view={view} aria-label="环境拓扑">
          <div
            key={fileAsset ? `files-${fileAsset.id}` : observedAsset ? `observe-${observedAsset.id}` : view}
            className={`${styles.viewContent} ${motionStyles.enter}`}
          >
            {performance && !observedAsset ? (
              <Suspense fallback={<Loading />}>
                <PerformanceWorkspace
                  environmentId={id}
                  assets={spec.assets}
                  onAsset={(item) => {
                    setSelection(item.id)
                    setObservationOpened(true)
                    setFilesOpened(false)
                  }}
                />
              </Suspense>
            ) : !spec.assets.length && !spec.networks.length && !spec.routers?.length ? (
              <Empty
                icon={<Network size={36} />}
                title="从一台交换机开始"
                action="添加交换机"
                onAction={() => adding('network')}
              />
            ) : traffic ? (
              <Suspense fallback={<Loading />}>
                <TrafficWorkspace
                  environment={environment}
                  onView={canCompose ? (view) => workbench.saveView.mutateAsync(view) : undefined}
                />
              </Suspense>
            ) : observedAsset ? (
              <Suspense fallback={<Loading />}>
                <ObservationWorkspace
                  key={observedAsset.id}
                  environmentId={id}
                  asset={observedAsset}
                  networks={spec.networks}
                  backLabel={performance ? '返回环境性能' : list ? '返回资产列表' : '返回拓扑'}
                  onClose={() => setObservationOpened(false)}
                />
              </Suspense>
            ) : fileAsset ? (
              <FileWorkspace
                key={fileAsset.id}
                environmentId={id}
                asset={fileAsset}
                onClose={() => setFilesOpened(false)}
                onSettings={
                  templatesById.get(fileAsset.templateId)?.kind === 'vm'
                    ? () =>
                        setConnectionSettings({
                          asset: fileAsset,
                          kind: 'ssh',
                        })
                    : undefined
                }
              />
            ) : list ? (
              <div className={styles.assetList}>
                <div className={styles.listTools}>
                  <SegmentedControl
                    className={styles.listKind}
                    size="sm"
                    aria-label="对象类型"
                    value={listKind}
                    onChange={(value) => {
                      setListKind(value)
                      setSelection(undefined)
                    }}
                    data={[
                      { value: 'assets', label: '资产' },
                      { value: 'networks', label: '网段' },
                    ]}
                  />
                  <TextInput
                    className={styles.listSearch}
                    aria-label={listKind === 'assets' ? '查找资产' : '查找网段'}
                    placeholder="名称或地址"
                    leftSection={<Search size={15} />}
                    value={query}
                    onChange={(event) => setQuery(event.currentTarget.value)}
                  />
                  {listKind === 'assets' && (
                    <Select
                      className={styles.listStatus}
                      aria-label="资产状态"
                      value={assetFilter}
                      onChange={(value) => setAssetFilter(value!)}
                      allowDeselect={false}
                      data={[
                        { value: 'all', label: '全部状态' },
                        { value: 'running', label: '运行中' },
                        { value: 'stopped', label: '已停止' },
                        { value: 'suspended', label: '已暂停' },
                        { value: 'failed', label: '失败' },
                      ]}
                    />
                  )}
                </div>
                <div key={listKind} className={motionStyles.enter}>
                  {listKind === 'networks' ? (
                    <NetworkList spec={spec} query={query} selection={selection} onSelect={setSelection} />
                  ) : (
                    <div className={styles.tableScroll}>
                      <table className={styles.assetTable} aria-label="运行资产">
                        <thead>
                          <tr>
                            <th>资产</th>
                            <th>状态</th>
                            <th>连接网段</th>
                            <th>地址</th>
                          </tr>
                        </thead>
                        <tbody>
                          {listedAssets.map((item) => (
                            <tr
                              key={item.id}
                              aria-selected={selection === item.id}
                              onClick={() => setSelection(item.id)}
                              onContextMenu={(event) => {
                                event.preventDefault()
                                setSelection(item.id)
                                setContext({
                                  id: item.id,
                                  x: event.clientX,
                                  y: event.clientY,
                                })
                              }}
                            >
                              <td>
                                <button className={styles.assetName} onClick={() => setSelection(item.id)}>
                                  {templatesById.get(item.templateId)?.kind === 'vm' ? (
                                    <Monitor size={17} />
                                  ) : (
                                    <Box size={17} />
                                  )}
                                  {item.name}
                                </button>
                              </td>
                              <td>
                                <Status value={assetStates.get(item.id) ?? 'draft'} />
                              </td>
                              <td>
                                {item.interfaces
                                  .map((iface) => spec.networks.find((network) => network.id === iface.networkId)?.name)
                                  .join('、')}
                              </td>
                              <td className="mono">
                                {item.interfaces
                                  .map((iface) => iface.address)
                                  .filter(Boolean)
                                  .join('、')}
                              </td>
                            </tr>
                          ))}
                          {listedExternalDevices.map(({ device, network }) => (
                            <tr
                              key={device.id}
                              aria-selected={selection === device.id}
                              onClick={() => setSelection(device.id)}
                            >
                              <td>
                                <button className={styles.assetName} onClick={() => setSelection(device.id)}>
                                  <Router size={17} />
                                  {device.name}
                                </button>
                              </td>
                              <td>外部设备</td>
                              <td>{network.name}</td>
                              <td className="mono">{device.address}</td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                      {!listedAssets.length && !listedExternalDevices.length && (
                        <div className={styles.empty}>没有匹配的资产</div>
                      )}
                    </div>
                  )}
                </div>
                {workbench.editing && (
                  <Button
                    variant="subtle"
                    leftSection={<Plus size={15} />}
                    onClick={() => adding(listKind === 'assets' ? 'asset' : 'network')}
                  >
                    {listKind === 'assets' ? '添加资产' : '添加网段'}
                  </Button>
                )}
              </div>
            ) : layout.isPending ? (
              <Loading />
            ) : (
              <TopologyCanvas
                focusRequest={focusRequest}
                nodes={graph.nodes}
                edges={graph.edges}
                selection={selection}
                onSelect={setSelection}
                editing={workbench.editing}
                onConnect={({ source, target }) => {
                  const routerId = spec.routers?.find((item) => item.id === source || item.id === target)?.id
                  const connectedNetwork = spec.networks.find((item) => item.id === source || item.id === target)?.id
                  if (routerId && connectedNetwork) {
                    workbench.setSpec((value) => connectRouter(value, routerId, connectedNetwork))
                    return
                  }
                  const assetId = spec.assets.some((item) => item.id === source) ? source : target
                  const networkId = spec.networks.some((item) => item.id === source) ? source : target
                  if (assetId && networkId && assetId !== networkId)
                    workbench.setSpec((value) => connectAsset(value, assetId, networkId))
                }}
                onContext={(objectId, x, y) => {
                  setSelection(objectId)
                  setContext({ id: objectId, x, y })
                }}
              />
            )}
          </div>
        </section>
        <AnimatePresence initial={false}>
          {(asset || network || router || externalDevice) &&
            !fileAsset &&
            !observedAsset &&
            !traffic &&
            !performance && (
              <ObjectInspector
                asset={asset}
                network={network}
                router={router}
                device={externalDevice?.device}
                deviceNetwork={externalDevice?.network}
                spec={spec}
                template={asset && templatesById.get(asset.templateId)}
                state={workbench.state.data}
                editing={workbench.editing}
                busy={busy}
                canOperate={allows(environment, 'operate', asset?.id)}
                canManage={allows(environment, 'manage', asset?.id)}
                onMigrate={() => setMigrating(asset)}
                canConnect={allows(environment, 'session', asset?.id)}
                canFile={allows(environment, 'file', asset?.id)}
                onFiles={() => {
                  setObservationOpened(false)
                  setFilesOpened(true)
                }}
                canObserve={allows(environment, 'observe', asset?.id)}
                canAccess={allows(environment, 'access', asset?.id)}
                services={workbench.services.data ?? []}
                onClose={() => setSelection(undefined)}
                onEdit={editObject}
                onRemove={remove}
                onDuplicate={duplicate}
                onAction={(action) =>
                  action === 'rebuild' ? setRebuilding(asset) : workbench.action.mutate({ action, assetId: asset?.id })
                }
                onConnect={connect}
                onLogs={() => setLogging(asset)}
                onObserve={() => {
                  setFilesOpened(false)
                  setObservationOpened(true)
                }}
                onServices={() => {
                  workbench.exposeService.reset()
                  workbench.revokeService.reset()
                  setServing(asset?.id)
                }}
                onSelect={setSelection}
                onDiscover={identity.data?.administrator && canCompose ? () => adding('external') : undefined}
              />
            )}
        </AnimatePresence>
      </div>
      {migrating && (
        <MigrationDialog
          id={id}
          asset={migrating}
          revision={environment.revision}
          onClose={() => setMigrating(undefined)}
          onSubmitted={workbench.refresh}
        />
      )}
      {connectionSettings && (
        <ConnectionDialog
          environmentId={id}
          asset={connectionSettings.asset}
          kind={connectionSettings.kind}
          onClose={() => setConnectionSettings(undefined)}
          onSaved={() => {
            void queryClient.invalidateQueries({
              queryKey: [connectionSettings.kind, id, connectionSettings.asset.id],
            })
            void queryClient.invalidateQueries({
              queryKey: ['files', id, connectionSettings.asset.id],
            })
            setConnectionSettings(undefined)
          }}
        />
      )}
      <TaskTray
        opened={tasksOpened}
        onClose={() => setTasksOpened(false)}
        canRetry={canOperate && !busy}
        operations={workbench.operations.data ?? []}
        operation={operation}
        pagination={workbench.operations}
        spec={spec}
        retrying={workbench.retry.isPending}
        onRetry={(operationId) => workbench.retry.mutate(operationId)}
      />
      {vpnOpened && environment.permissions?.includes('access') && (
        <VPNDrawer
          vpn={vpn}
          spec={environment.appliedSpec ?? { networks: [], assets: [] }}
          busy={busy || status === 'destroyed' || operation?.state === 'queued' || operation?.state === 'running'}
          onClose={() => setVPNOpened(false)}
        />
      )}
      {logging && <LogDrawer environmentId={id} asset={logging} onClose={() => setLogging(undefined)} />}
      {servedAsset && (
        <ServiceDrawer
          key={servedAsset.id}
          asset={servedAsset}
          spec={spec}
          services={workbench.services.data ?? []}
          loading={workbench.services.isLoading}
          pending={busy || workbench.exposeService.isPending || workbench.revokeService.isPending}
          canManage={allows(environment, 'access', servedAsset.id)}
          error={workbench.services.error ?? workbench.exposeService.error ?? workbench.revokeService.error}
          onCreate={(body, onAccepted) =>
            workbench.exposeService.mutate({ assetId: servedAsset.id, ...body }, { onSuccess: onAccepted })
          }
          onRevoke={(serviceId) => workbench.revokeService.mutate(serviceId)}
          onClose={() => setServing(undefined)}
        />
      )}
      {editor === 'asset' && (
        <AssetEditor
          asset={editingObject ? asset : undefined}
          templates={templates}
          pagination={workbench.templates}
          search={workbench.templateSearch}
          onSearch={workbench.setTemplateSearch}
          spec={spec}
          onSave={updateAsset}
          onClose={() => setEditor(undefined)}
        />
      )}
      {editor === 'router' && (
        <RouterEditor
          router={editingObject ? router : undefined}
          spec={spec}
          onSave={updateRouter}
          onClose={() => setEditor(undefined)}
        />
      )}
      {editor === 'external' && (
        <ExternalDeviceEditor
          networks={spec.networks}
          assets={spec.assets}
          selectedNetwork={externalDevice?.network.id ?? (network?.external ? network.id : undefined)}
          device={editingObject ? externalDevice?.device : undefined}
          onSave={updateNetwork}
          onConnectNetwork={() => adding('external-network')}
          onClose={() => setEditor(undefined)}
        />
      )}
      {(editor === 'network' || editor === 'external-network') && (
        <NetworkEditor
          initialExternal={editor === 'external-network'}
          network={editingObject ? network : undefined}
          spec={spec}
          onSave={updateNetwork}
          onClose={() => setEditor(undefined)}
        />
      )}
      {savingBlueprint && (
        <SaveBlueprintDialog
          environmentId={environment.id}
          environmentName={environment.name}
          expectedRevision={environment.revision}
          spec={spec}
          onClose={() => setSavingBlueprint(false)}
        />
      )}
      <Modal
        opened={Boolean(workbench.preview.data) || Boolean(workbench.preview.error)}
        onClose={() => workbench.preview.reset()}
        title="应用环境变更"
        centered
        size="md"
      >
        <ErrorMessage error={workbench.preview.error} />
        <ErrorMessage error={workbench.apply.error} />
        {workbench.preview.data && (
          <>
            <div className="change-summary">
              {workbench.preview.data.changes.length ? (
                workbench.preview.data.changes.map((item) => (
                  <div key={item.id} className="change-row">
                    <span className={`change-effect ${item.effect}`}>
                      {
                        {
                          add: '新增',
                          update: '修改',
                          replace: '替换',
                          remove: '移除',
                        }[item.effect]
                      }
                    </span>
                    <strong>{item.name}</strong>
                    {item.requiresStop && <span className="impact-tag">需要停机</span>}
                    {item.dataEffect && <p>{item.dataEffect}</p>}
                  </div>
                ))
              ) : (
                <div className={styles.empty}>没有执行配置变更</div>
              )}
            </div>
            <div className="dialog-actions">
              <Button variant="default" onClick={() => workbench.preview.reset()}>
                返回调整
              </Button>
              <Button
                onClick={() => workbench.apply.mutate()}
                loading={workbench.apply.isPending}
                disabled={!workbench.preview.data.changes.length}
              >
                应用变更
              </Button>
            </div>
          </>
        )}
      </Modal>
      <Modal
        opened={Boolean(rebuilding)}
        onClose={() => setRebuilding(undefined)}
        title={`重建 ${rebuilding?.name ?? ''}`}
        centered
        size="sm"
      >
        <p>系统盘上的改动将清除，恢复为模板初始内容。</p>
        <div className="dialog-actions">
          <Button variant="default" onClick={() => setRebuilding(undefined)}>
            取消
          </Button>
          <Button
            color="red"
            loading={workbench.action.isPending}
            disabled={busy}
            onClick={() => {
              workbench.action.mutate({
                action: 'rebuild',
                assetId: rebuilding!.id,
              })
              setRebuilding(undefined)
            }}
          >
            重建资产
          </Button>
        </div>
      </Modal>
      <Modal
        opened={resetting}
        onClose={() => setResetting(false)}
        title={`全面重建 ${environment.name}`}
        centered
        size="sm"
      >
        <p>全部资产的系统盘和环境数据卷将恢复初始内容。网络、IP、名称和规格保留。</p>
        {spec.assets.some((asset) => asset.volumes?.some((volume) => volume.persistentVolumeId)) && (
          <p>独立挂载的数据卷将解除挂载，原卷保留。</p>
        )}
        <div className="dialog-actions">
          <Button variant="default" onClick={() => setResetting(false)}>
            取消
          </Button>
          <Button
            color="red"
            disabled={busy}
            loading={workbench.action.isPending}
            onClick={() => {
              workbench.action.mutate({
                action: 'rebuild',
                resetVolumes: true,
              })
              setResetting(false)
            }}
          >
            清空改动并重建
          </Button>
        </div>
      </Modal>
      <Modal
        opened={destroying}
        onClose={() => setDestroying(false)}
        title={`销毁 ${environment.name}`}
        centered
        size="sm"
      >
        <div className="destroy-summary">
          <strong>{spec.assets.length} 个资产</strong>
          <span>{spec.networks.length} 个网段</span>
        </div>
        <p>运行资产及其系统盘将删除。</p>
        <div className="dialog-actions">
          <Button variant="default" onClick={() => setDestroying(false)}>
            取消
          </Button>
          <Button
            color="red"
            onClick={() => {
              setDestroying(false)
              workbench.action.mutate({ action: 'destroy' })
            }}
          >
            销毁环境
          </Button>
        </div>
      </Modal>
      {recoveryOpened && (
        <RecoveryDrawer
          id={id}
          projectId={environment.projectId}
          revision={environment.revision}
          busy={busy}
          canManage={canManage}
          canClone={canManage && allowsProject(identity.data, 'compose', environment.projectId)}
          canCapture={['running', 'stopped', 'suspended'].includes(status)}
          canCaptureMemory={
            !environment.spec.assets.some((asset) => asset.pciBinding) &&
            environment.spec.assets.some(
              (asset) =>
                templatesById.get(asset.templateId)?.kind === 'vm' &&
                ['running', 'suspended'].includes(assetStates.get(asset.id) ?? '')
            )
          }
          onClose={() => setRecoveryOpened(false)}
        />
      )}
      <Menu opened={Boolean(context)} onClose={() => setContext(undefined)} position="bottom-start">
        <Menu.Target>
          <span className="context-anchor" style={{ left: context?.x, top: context?.y }} />
        </Menu.Target>
        <Menu.Dropdown>
          {workbench.editing ? (
            <>
              <Menu.Item leftSection={<Pencil size={15} />} onClick={editObject}>
                编辑
              </Menu.Item>
              {asset && <Menu.Item onClick={duplicate}>复制资产</Menu.Item>}
              <Menu.Item color="red" leftSection={<Trash2 size={15} />} onClick={remove}>
                移除
              </Menu.Item>
            </>
          ) : (
            canCompose && (
              <Menu.Item leftSection={<Pencil size={15} />} disabled={busy} onClick={beginEdit}>
                调整环境
              </Menu.Item>
            )
          )}
        </Menu.Dropdown>
      </Menu>
    </main>
  )
}
