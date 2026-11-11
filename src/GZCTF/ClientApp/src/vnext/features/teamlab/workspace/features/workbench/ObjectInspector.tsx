import { Accordion, ActionIcon, Button, Menu } from '@mantine/core'
import {
  Activity,
  ArrowRight,
  Box,
  ChevronDown,
  Copy,
  FolderOpen,
  HardDrive,
  Monitor,
  MoreHorizontal,
  Network,
  Pencil,
  Play,
  Power,
  RotateCcw,
  SquareTerminal,
  SquareChartGantt,
  Trash2,
  X,
  Router,
  Radar,
} from 'lucide-react'
import type {
  Asset,
  EnvironmentSpec,
  EnvironmentState,
  Network as NetworkModel,
  Schema,
  Template,
} from '../../api/client'
import { AnimatedPanel } from '../../foundation/AnimatedPanel'
import { Status } from '../../foundation/Status'
import { memory } from '../../foundation/format'
import styles from './ObjectInspector.module.css'
import { networkColors } from './topology'

type Props = {
  asset?: Asset
  network?: NetworkModel
  router?: Schema<'LogicalRouter'>
  device?: Schema<'ExternalDevice'>
  deviceNetwork?: NetworkModel
  onDiscover?: () => void
  spec: EnvironmentSpec
  template?: Template
  state?: EnvironmentState
  editing: boolean
  busy: boolean
  canOperate: boolean
  canManage: boolean
  canConnect: boolean
  canObserve: boolean
  canFile: boolean
  canAccess: boolean
  onMigrate: () => void
  onClose: () => void
  onEdit: () => void
  onRemove: () => void
  onDuplicate: () => void
  onAction: (action: Schema<'ActionRequest'>['action']) => void
  onConnect: (kind: Schema<'ConsoleKind'>) => void
  onFiles: () => void
  onLogs: () => void
  onObserve: () => void
  onServices: () => void
  onSelect: (id: string) => void
  services: Schema<'ServiceEndpoint'>[]
}

export function ObjectInspector({
  asset,
  network,
  router,
  device,
  deviceNetwork,
  onDiscover,
  spec,
  template,
  state,
  editing,
  busy,
  canOperate,
  canManage,
  onMigrate,
  canConnect,
  canFile,
  onFiles,
  canObserve,
  onClose,
  onEdit,
  onRemove,
  onDuplicate,
  onAction,
  onConnect,
  onLogs,
  onObserve,
  services,
  canAccess,
  onServices,
  onSelect,
}: Props) {
  const assetState = state?.assets.find((item) => item.assetId === asset?.id)
  const vm = template?.kind === 'vm'
  const stopped = assetState?.state === 'stopped'
  const assetServices = services.filter((item) => item.assetId === asset?.id)
  return (
    <AnimatedPanel className={styles.inspector} label="对象详情">
      <div key={asset?.id ?? network?.id ?? router?.id ?? device?.id}>
        <header className={styles.header}>
          <span className={styles.symbol}>
            {device || router ? (
              <Router size={22} />
            ) : network ? (
              <Network size={22} />
            ) : vm ? (
              <Monitor size={22} />
            ) : (
              <Box size={22} />
            )}
          </span>
          <div className={styles.identity}>
            <h2>{device?.name ?? router?.name ?? asset?.name ?? network?.name}</h2>
            {asset ? (
              <Status value={assetState?.state ?? 'draft'} />
            ) : (
              <span className={styles.address}>{device ? '外部设备' : router ? '原生路由器' : network?.cidr}</span>
            )}
          </div>
          <ActionIcon variant="subtle" color="gray" aria-label="关闭对象详情" onClick={onClose}>
            <X size={17} />
          </ActionIcon>
        </header>
        {(editing || (assetState && (canOperate || canManage || canObserve || canConnect || canFile || canAccess))) && (
          <div className={styles.actions}>
            {editing ? (
              <Button variant="default" leftSection={<Pencil size={14} />} onClick={onEdit}>
                编辑
              </Button>
            ) : (
              asset && (
                <>
                  {stopped
                    ? canOperate && (
                        <Button leftSection={<Play size={14} />} disabled={busy} onClick={() => onAction('start')}>
                          启动
                        </Button>
                      )
                    : canConnect && (
                        <div className={styles.connect}>
                          <Button
                            leftSection={vm ? <Monitor size={15} /> : <SquareTerminal size={15} />}
                            disabled={busy || (assetState?.state === 'suspended' && !vm)}
                            onClick={() => onConnect(vm ? 'vnc' : 'terminal')}
                          >
                            {vm ? '控制台' : '终端'}
                          </Button>
                          <Menu position="bottom-start">
                            <Menu.Target>
                              <ActionIcon variant="filled" aria-label="连接方式" disabled={busy}>
                                <ChevronDown size={15} />
                              </ActionIcon>
                            </Menu.Target>
                            <Menu.Dropdown>
                              <Menu.Item
                                leftSection={<SquareTerminal size={15} />}
                                disabled={assetState?.state !== 'running'}
                                onClick={() => onConnect('ssh')}
                              >
                                SSH
                              </Menu.Item>
                              {vm && (
                                <Menu.Item
                                  leftSection={<Monitor size={15} />}
                                  disabled={assetState?.state !== 'running'}
                                  onClick={() => onConnect('rdp')}
                                >
                                  远程桌面
                                </Menu.Item>
                              )}
                              {vm && (
                                <Menu.Item
                                  leftSection={<SquareTerminal size={15} />}
                                  disabled={stopped}
                                  onClick={() => onConnect('serial')}
                                >
                                  串口控制台
                                </Menu.Item>
                              )}
                            </Menu.Dropdown>
                          </Menu>
                        </div>
                      )}
                  {canFile && (
                    <ActionIcon variant="default" aria-label="文件" onClick={onFiles}>
                      <FolderOpen size={17} />
                    </ActionIcon>
                  )}
                  {canAccess && (
                    <ActionIcon variant="default" aria-label="管理服务入口" disabled={busy} onClick={onServices}>
                      <Network size={17} />
                    </ActionIcon>
                  )}
                </>
              )
            )}
            {(editing || (asset && (canOperate || canManage || canObserve))) && (
              <Menu position="bottom-end">
                <Menu.Target>
                  <ActionIcon variant="subtle" color="gray" aria-label="对象操作">
                    <MoreHorizontal size={18} />
                  </ActionIcon>
                </Menu.Target>
                <Menu.Dropdown>
                  {editing ? (
                    <>
                      {asset && (
                        <Menu.Item leftSection={<Copy size={15} />} onClick={onDuplicate}>
                          复制资产
                        </Menu.Item>
                      )}
                      <Menu.Item color="red" leftSection={<Trash2 size={15} />} onClick={onRemove}>
                        {device
                          ? '移除设备'
                          : router
                            ? '移除路由器'
                            : asset
                              ? '移除资产'
                              : network?.external
                                ? '移除网络'
                                : '移除交换机'}
                      </Menu.Item>
                    </>
                  ) : (
                    asset && (
                      <>
                        {canOperate && (
                          <>
                            <Menu.Item
                              color={stopped ? undefined : 'red'}
                              leftSection={<Power size={15} />}
                              disabled={busy}
                              onClick={() => onAction(stopped ? 'start' : 'stop')}
                            >
                              {stopped ? '启动' : vm ? '关机' : '停止'}
                            </Menu.Item>
                            <Menu.Item
                              leftSection={<RotateCcw size={15} />}
                              disabled={busy}
                              onClick={() => onAction('reboot')}
                            >
                              重启
                            </Menu.Item>
                            <Menu.Item
                              disabled={busy || !['running', 'suspended'].includes(assetState?.state ?? '')}
                              onClick={() => onAction(assetState?.state === 'suspended' ? 'resume' : 'suspend')}
                            >
                              {assetState?.state === 'suspended' ? '继续运行' : '暂停'}
                            </Menu.Item>
                            <Menu.Divider />
                          </>
                        )}
                        {canObserve && (
                          <Menu.Item leftSection={<Activity size={15} />} onClick={onObserve}>
                            资源曲线
                          </Menu.Item>
                        )}
                        {canObserve && template?.kind === 'container' && (
                          <Menu.Item leftSection={<SquareChartGantt size={15} />} onClick={onLogs}>
                            进程日志
                          </Menu.Item>
                        )}
                        {canManage &&
                          !asset.pciBinding &&
                          assetState &&
                          ['running', 'suspended', 'stopped'].includes(assetState.state) && (
                            <Menu.Item disabled={busy} leftSection={<Monitor size={15} />} onClick={onMigrate}>
                              迁移节点
                            </Menu.Item>
                          )}
                        {(canOperate || canManage) && <Menu.Divider />}
                        {canOperate && (
                          <Menu.Item color="red" disabled={busy} onClick={() => onAction('force-stop')}>
                            强制停止
                          </Menu.Item>
                        )}
                        {canManage && (
                          <Menu.Item
                            color="red"
                            leftSection={<HardDrive size={15} />}
                            disabled={busy}
                            onClick={() => onAction('rebuild')}
                          >
                            重建资产
                          </Menu.Item>
                        )}
                      </>
                    )
                  )}
                </Menu.Dropdown>
              </Menu>
            )}
          </div>
        )}
        {assetState?.error && (
          <div className={styles.error} role="alert">
            {assetState.error}
          </div>
        )}
        <div className={styles.body}>
          {router && (
            <>
              <section className={styles.section}>
                <h3>
                  网络接口 <span>{router.interfaces.length}</span>
                </h3>
                <div className={styles.items}>
                  {router.interfaces.map((iface) => {
                    const attached = spec.networks.find((item) => item.id === iface.networkId)
                    return (
                      <button className={styles.item} key={iface.id} onClick={() => onSelect(iface.networkId)}>
                        <Network size={16} />
                        <span className={styles.itemText}>
                          <strong>{attached?.name}</strong>
                          <span className={styles.address}>{iface.address || '自动分配'}</span>
                        </span>
                        <ArrowRight size={13} />
                      </button>
                    )
                  })}
                </div>
              </section>
              <section className={styles.section}>
                <h3>
                  静态路由 <span>{spec.routes?.filter((route) => route.routerId === router.id).length ?? 0}</span>
                </h3>
                <div className={styles.items}>
                  {spec.routes
                    ?.filter((route) => route.routerId === router.id)
                    .map((route) => (
                      <div className={styles.item} key={`${route.networkId}:${route.destination}:${route.nextHop}`}>
                        <ArrowRight size={15} />
                        <span className={styles.itemText}>
                          <strong className={styles.address}>{route.destination}</strong>
                          <span className={styles.address}>{route.nextHop}</span>
                        </span>
                        <span className={styles.meta}>
                          {spec.networks.find((item) => item.id === route.networkId)?.name}
                        </span>
                      </div>
                    ))}
                </div>
              </section>
            </>
          )}
          {device && deviceNetwork && (
            <>
              <dl className={styles.properties}>
                <div>
                  <dt>IP 地址</dt>
                  <dd className={styles.address}>{device.address}</dd>
                </div>
                <div>
                  <dt>MAC 地址</dt>
                  <dd className={styles.address}>{device.mac}</dd>
                </div>
                {device.observedAt && (
                  <div>
                    <dt>发现时间</dt>
                    <dd>{new Date(device.observedAt).toLocaleString()}</dd>
                  </div>
                )}
              </dl>
              <section className={styles.section}>
                <h3>接入网络</h3>
                <button className={styles.item} onClick={() => onSelect(deviceNetwork.id)}>
                  <Network size={16} />
                  <span className={styles.itemText}>
                    <strong>{deviceNetwork.name}</strong>
                    <span className={styles.address}>{deviceNetwork.external?.interface}</span>
                  </span>
                  <ArrowRight size={13} />
                </button>
              </section>
            </>
          )}
          {asset && (
            <>
              <dl className={styles.resources}>
                <div>
                  <dt>CPU</dt>
                  <dd>
                    {asset.resources.cpu}
                    <span>核</span>
                  </dd>
                </div>
                <div>
                  <dt>内存</dt>
                  <dd>{memory(asset.resources.memoryMiB)}</dd>
                </div>
                <div>
                  <dt>{template?.kind === 'container' ? '磁盘预算' : '系统盘'}</dt>
                  <dd>
                    {asset.resources.diskGiB}
                    <span>GiB</span>
                  </dd>
                </div>
              </dl>
              <section className={styles.section}>
                <h3>
                  网络接口 <span>{asset.interfaces.length}</span>
                </h3>
                <div className={styles.items}>
                  {asset.interfaces.map((item, index) => {
                    const networkIndex = spec.networks.findIndex((network) => network.id === item.networkId)
                    const connected = spec.networks[networkIndex]
                    return (
                      <button key={item.id} className={styles.item} onClick={() => onSelect(item.networkId)}>
                        <span
                          className={styles.marker}
                          style={{
                            background: networkColors[networkIndex % networkColors.length],
                          }}
                        />
                        <span className={styles.itemText}>
                          <strong>{connected?.name}</strong>
                          <span className={styles.address}>{item.address || connected?.cidr}</span>
                        </span>
                        <span className={styles.meta}>{item.primary ? '主接口' : `网卡 ${index + 1}`}</span>
                        <ArrowRight size={13} />
                      </button>
                    )
                  })}
                </div>
              </section>
              {!editing && assetState && assetServices.length > 0 && (
                <section className={styles.section}>
                  <h3>
                    服务入口 <span>{assetServices.length}</span>
                  </h3>
                  <div className={styles.items}>
                    {assetServices.map((item) => (
                      <button key={item.id} className={styles.item} onClick={onServices}>
                        <span className={styles.itemText}>
                          <strong>
                            {item.protocol.toUpperCase()} / {item.targetPort}
                          </strong>
                          <span className={styles.address}>
                            {item.address}:{item.port}
                          </span>
                        </span>
                        <ArrowRight size={13} />
                      </button>
                    ))}
                  </div>
                </section>
              )}
              {template && (
                <Accordion className={styles.details}>
                  <Accordion.Item value="hardware">
                    <Accordion.Control>模板与硬件</Accordion.Control>
                    <Accordion.Panel>
                      <dl className={styles.properties}>
                        <div>
                          <dt>模板</dt>
                          <dd>{template.name}</dd>
                        </div>
                        <div>
                          <dt>系统</dt>
                          <dd>{template.os}</dd>
                        </div>
                        {vm && (
                          <div>
                            <dt>固件</dt>
                            <dd className={styles.technical}>{template.hardware?.firmware.toUpperCase()}</dd>
                          </div>
                        )}
                        {asset.interfaces.map(
                          (item, index) =>
                            item.mac && (
                              <div key={item.id}>
                                <dt>网卡 {index + 1}</dt>
                                <dd className={styles.address}>{item.mac}</dd>
                              </div>
                            )
                        )}
                      </dl>
                    </Accordion.Panel>
                  </Accordion.Item>
                </Accordion>
              )}
            </>
          )}
          {network && (
            <>
              <dl className={styles.properties}>
                <div>
                  <dt>网关</dt>
                  <dd className={styles.address}>{network.gateway || (network.external ? '—' : '自动')}</dd>
                </div>
                <div>
                  <dt>DNS</dt>
                  <dd>
                    {spec.assets.find((item) => item.id === network.dnsAssetId)?.name ||
                      network.dnsServers?.join(', ') ||
                      '默认'}
                  </dd>
                </div>
                <div>
                  <dt>MTU</dt>
                  <dd>{network.mtu || 1442}</dd>
                </div>
                {network.external && (
                  <>
                    <div>
                      <dt>接入网口</dt>
                      <dd>
                        {network.external.interface}
                        {network.external.vlan ? ` · VLAN ${network.external.vlan}` : ''}
                      </dd>
                    </div>
                    <div>
                      <dt>虚拟资产地址段</dt>
                      <dd className={styles.address}>{network.allocationPool}</dd>
                    </div>
                  </>
                )}
              </dl>
              {spec.routers?.some((router) => router.interfaces.some((iface) => iface.networkId === network.id)) && (
                <section className={styles.section}>
                  <h3>连接路由器</h3>
                  <div className={styles.items}>
                    {spec.routers
                      .filter((router) => router.interfaces.some((iface) => iface.networkId === network.id))
                      .map((router) => (
                        <button className={styles.item} key={router.id} onClick={() => onSelect(router.id)}>
                          <Router size={16} />
                          <span className={styles.itemText}>
                            <strong>{router.name}</strong>
                            <span className={styles.address}>
                              {router.interfaces.find((iface) => iface.networkId === network.id)?.address || '自动分配'}
                            </span>
                          </span>
                          <ArrowRight size={13} />
                        </button>
                      ))}
                  </div>
                </section>
              )}
              {network.external && (
                <section className={styles.section}>
                  <h3>
                    外部设备 <span>{network.devices?.length ?? 0}</span>
                  </h3>
                  <div className={styles.items}>
                    {network.devices?.map((device) => (
                      <button key={device.id} className={styles.item} onClick={() => onSelect(device.id)}>
                        <Router size={16} />
                        <span className={styles.itemText}>
                          <strong>{device.name}</strong>
                          <span className={styles.address}>{device.address}</span>
                        </span>
                        <ArrowRight size={13} />
                      </button>
                    ))}
                  </div>
                  {onDiscover && (
                    <Button variant="default" leftSection={<Radar size={15} />} onClick={onDiscover}>
                      发现设备
                    </Button>
                  )}
                </section>
              )}
              <section className={styles.section}>
                <h3>连接资产</h3>
                <div className={styles.items}>
                  {spec.assets
                    .filter((item) => item.interfaces.some((iface) => iface.networkId === network.id))
                    .map((member) => (
                      <button className={styles.item} key={member.id} onClick={() => onSelect(member.id)}>
                        <Box size={16} />
                        <span className={styles.itemText}>
                          <strong>{member.name}</strong>
                          <span className={styles.address}>
                            {member.interfaces.find((item) => item.networkId === network.id)?.address}
                          </span>
                        </span>
                        <ArrowRight size={13} />
                      </button>
                    ))}
                </div>
              </section>
            </>
          )}
        </div>
      </div>
    </AnimatedPanel>
  )
}
