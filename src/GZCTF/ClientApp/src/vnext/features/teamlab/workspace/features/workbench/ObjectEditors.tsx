import {
  ActionIcon,
  Button,
  Collapse,
  Drawer,
  MultiSelect,
  NumberInput,
  Select,
  SegmentedControl,
  Textarea,
  TextInput,
} from '@mantine/core'
import { useQuery } from '@tanstack/react-query'
import { Cable, ChevronDown, GitBranch, Plus, Trash2 } from 'lucide-react'
import { useEffect, useState } from 'react'
import {
  useWorkspaceApi,
  type Asset,
  type EnvironmentSpec,
  type Network,
  type Schema,
  type Template,
} from '../../api/client'
import { ErrorMessage } from '../../foundation/Feedback'
import { LoadMore, type CursorPagination } from '../../foundation/LoadMore'
import { randomUUID } from '../../foundation/id'
import { useCursorList } from '../../foundation/useCursorList'
import styles from './ObjectEditors.module.css'
import { networkOptions } from './networking'

export function AssetEditor({
  asset,
  templates,
  pagination,
  search,
  onSearch,
  spec,
  onSave,
  onClose,
}: {
  asset?: Asset
  templates: Template[]
  pagination: CursorPagination
  search: string
  onSearch: (value: string) => void
  spec: EnvironmentSpec
  onSave: (asset: Asset) => void
  onClose: () => void
}) {
  const api = useWorkspaceApi()
  const [templateId, setTemplateId] = useState(asset?.templateId ?? '')
  const [chosenTemplate, setChosenTemplate] = useState(templates.find((item) => item.id === asset?.templateId))
  const [media, setMedia] = useState(asset?.media)
  const [name, setName] = useState(asset?.name ?? '')
  const [networkIds, setNetworkIds] = useState(
    asset?.interfaces.map((item) => item.networkId) ?? spec.networks.slice(0, 1).map((item) => item.id)
  )
  const [cpu, setCpu] = useState(asset?.resources.cpu ?? 2)
  const [memoryGiB, setMemoryGiB] = useState((asset?.resources.memoryMiB ?? 2048) / 1024)
  const [disk, setDisk] = useState(asset?.resources.diskGiB ?? 20)
  const [advanced, setAdvanced] = useState(false)
  const [volumes, setVolumes] = useState<Schema<'Volume'>[]>(asset?.volumes ?? [])
  const [volumesOpen, setVolumesOpen] = useState(Boolean(asset?.volumes?.length))
  const identity = useQuery({ queryKey: ['identity'], queryFn: api.identity })
  const [devicesOpen, setDevicesOpen] = useState(Boolean(asset?.pciBinding))
  const [pciBinding, setPciBinding] = useState(asset?.pciBinding)
  const deviceNodes = useCursorList(['nodes', 'pci-devices'], api.nodes, {
    enabled: devicesOpen && Boolean(identity.data?.administrator),
  })
  const availableVolumes = useQuery({
    queryKey: ['volumes'],
    queryFn: api.volumes,
    enabled: volumesOpen && Boolean(identity.data?.administrator),
  })
  const [storagePoolId, setStoragePoolId] = useState<string | null>(asset?.storagePoolId ?? null)
  const storage = useQuery({
    queryKey: ['storage-pools'],
    queryFn: api.storagePools,
    enabled: advanced && !asset,
  })
  const [restartPolicy, setRestartPolicy] = useState<Schema<'RestartPolicy'>>(asset?.restartPolicy ?? 'never')
  const [guestOpen, setGuestOpen] = useState(Boolean(asset?.guest))
  const [hostname, setHostname] = useState(asset?.guest?.hostname ?? '')
  const [username, setUsername] = useState(asset?.guest?.username ?? '')
  const [sshKeys, setSshKeys] = useState(asset?.guest?.sshAuthorizedKeys?.join('\n') ?? '')
  const template = chosenTemplate ?? templates.find((item) => item.id === templateId)
  const initialized = template?.kind === 'vm' && template.initialization !== 'none'
  const setTemplate = (id: string | null) => {
    setTemplateId(id ?? '')
    const template = templates.find((item) => item.id === id)
    setChosenTemplate(template)
    if (template) {
      if (!asset) {
        const defaults = template.volumes ?? []
        const mounts = new Set(defaults.map((volume) => volume.mountPath))
        const volumes = [
          ...defaults,
          ...(template.imageVolumes ?? []).flatMap((mountPath, index) =>
            mounts.has(mountPath) ? [] : [{ id: `image-volume-${index}`, mountPath, sizeGiB: 1 }]
          ),
        ]
        setVolumes(volumes)
        setVolumesOpen(volumes.length > 0)
      }
      setMedia(template.media?.map((item) => item.id))
      setCpu(template.resources.cpu)
      setMemoryGiB(template.resources.memoryMiB / 1024)
      setDisk(template.resources.diskGiB)
      if (!name) setName(`${template.name}-${spec.assets.filter((item) => item.templateId === id).length + 1}`)
    }
  }
  const save = () =>
    onSave({
      ...asset,
      id: asset?.id ?? randomUUID(),
      name,
      templateId,
      storagePoolId: storagePoolId ?? undefined,
      resources: { cpu, memoryMiB: memoryGiB * 1024, diskGiB: disk },
      volumes,
      media: template?.media?.length ? media : undefined,
      restartPolicy: template?.kind === 'container' ? restartPolicy : undefined,
      pciBinding: template?.kind === 'vm' && pciBinding?.groupIds.length ? pciBinding : undefined,
      guest: initialized
        ? {
            hostname: hostname.trim() || undefined,
            username: username.trim() || undefined,
            sshAuthorizedKeys: sshKeys
              .split('\n')
              .map((key) => key.trim())
              .filter(Boolean),
          }
        : undefined,
      interfaces: networkIds.map(
        (networkId, index) =>
          asset?.interfaces.find((item) => item.networkId === networkId) ?? {
            id: randomUUID(),
            networkId,
            mac: '',
            address: '',
            primary: index === 0,
          }
      ),
    })
  return (
    <Drawer opened onClose={onClose} title={asset ? '编辑资产' : '添加资产'} position="right" size={504}>
      <form
        className={styles.form}
        onSubmit={(event) => {
          event.preventDefault()
          save()
        }}
      >
        <Select
          label="资产模板"
          placeholder="选择模板"
          searchable
          searchValue={search}
          onSearchChange={onSearch}
          required
          value={templateId}
          onChange={setTemplate}
          data={[
            ...new Map(
              [...templates, ...(chosenTemplate ? [chosenTemplate] : [])].map((item) => [item.id, item])
            ).values(),
          ]
            .filter((item) => item.state === 'ready')
            .map((item) => ({
              value: item.id,
              label: `${item.name} · ${item.kind === 'vm' ? '虚拟机' : '容器'}`,
            }))}
        />
        <LoadMore list={pagination} />
        {templateId && (
          <>
            <TextInput
              label="资产名称"
              value={name}
              onChange={(event) => setName(event.currentTarget.value)}
              required
            />
            <MultiSelect
              label="连接网段"
              data={networkOptions(spec)}
              value={networkIds}
              onChange={setNetworkIds}
              searchable
            />
            <div className={styles.resources}>
              <NumberInput
                label="CPU · 核"
                min={template?.kind === 'container' ? 0.01 : 1}
                step={template?.kind === 'container' ? 0.25 : 1}
                decimalScale={template?.kind === 'container' ? 2 : 0}
                allowDecimal={template?.kind === 'container'}
                value={cpu}
                onChange={(value) => setCpu(Number(value))}
              />
              <NumberInput
                label="内存 · GiB"
                min={0.25}
                step={0.25}
                value={memoryGiB}
                onChange={(value) => setMemoryGiB(Number(value))}
              />
              <NumberInput
                label={template?.kind === 'container' ? '磁盘预算 · GiB' : '系统盘 · GiB'}
                min={1}
                value={disk}
                onChange={(value) => setDisk(Number(value))}
              />
            </div>
            {template?.kind === 'container' && (
              <Select
                label="进程退出后"
                value={restartPolicy}
                onChange={(value) => setRestartPolicy(value as Schema<'RestartPolicy'>)}
                allowDeselect={false}
                data={[
                  { value: 'never', label: '保持停止' },
                  { value: 'on-failure', label: '异常退出时重启' },
                  { value: 'always', label: '自动重启' },
                ]}
              />
            )}
            {Boolean(template?.media?.length) && (
              <MultiSelect
                label="光驱"
                data={template!.media!.map((item) => ({
                  value: item.id,
                  label: item.source.split('/').at(-1) ?? item.source,
                }))}
                value={media ?? template!.media!.map((item) => item.id)}
                onChange={setMedia}
              />
            )}
            {!asset && (
              <>
                <button
                  className={styles.disclosure}
                  type="button"
                  aria-expanded={advanced}
                  onClick={() => setAdvanced(!advanced)}
                >
                  存储位置
                  <ChevronDown size={16} />
                </button>
                <Collapse expanded={advanced}>
                  <Select
                    label="存储位置"
                    placeholder="自动分配"
                    clearable
                    value={storagePoolId}
                    onChange={setStoragePoolId}
                    data={(storage.data ?? [])
                      .filter(
                        (pool) =>
                          !pool.default &&
                          pool.state === 'ready' &&
                          !pool.error &&
                          (template?.kind === 'vm' || pool.capabilities.includes('volumes'))
                      )
                      .map((pool) => ({ value: pool.id, label: pool.name }))}
                  />
                  <ErrorMessage error={storage.error} />
                </Collapse>
              </>
            )}
            {template?.kind === 'vm' && identity.data?.administrator && (
              <>
                <button
                  className={styles.disclosure}
                  type="button"
                  aria-expanded={devicesOpen}
                  onClick={() => setDevicesOpen(!devicesOpen)}
                >
                  PCI 直通 <ChevronDown size={16} />
                </button>
                <Collapse expanded={devicesOpen}>
                  <Select
                    label="宿主节点"
                    clearable
                    value={pciBinding?.nodeId ?? null}
                    data={(deviceNodes.data ?? [])
                      .filter((node) => node.vmHardware?.pciGroups?.length)
                      .map((node) => ({ value: node.id, label: node.name }))}
                    onChange={(nodeId) => setPciBinding(nodeId ? { nodeId, groupIds: [] } : undefined)}
                  />
                  {pciBinding && (
                    <MultiSelect
                      label="设备组"
                      searchable
                      value={pciBinding.groupIds}
                      data={(
                        deviceNodes.data?.find((node) => node.id === pciBinding.nodeId)?.vmHardware?.pciGroups ?? []
                      ).map((group) => ({
                        value: group.id,
                        label: group.name,
                        disabled:
                          !group.available &&
                          !(
                            asset?.pciBinding?.nodeId === pciBinding.nodeId &&
                            asset.pciBinding.groupIds.includes(group.id)
                          ),
                      }))}
                      onChange={(groupIds) => setPciBinding({ ...pciBinding, groupIds })}
                    />
                  )}
                  <ErrorMessage error={deviceNodes.error} />
                  <LoadMore list={deviceNodes} />
                </Collapse>
              </>
            )}
            <button
              className={styles.disclosure}
              type="button"
              aria-expanded={volumesOpen}
              onClick={() => setVolumesOpen(!volumesOpen)}
            >
              数据卷
              <ChevronDown size={16} />
            </button>
            <Collapse expanded={volumesOpen}>
              <div className={styles.form}>
                {volumes.map((volume, index) => {
                  const imageMount = template?.imageVolumes?.includes(volume.mountPath)
                  const update = (values: Partial<Schema<'Volume'>>) =>
                    setVolumes((items) => items.map((item, i) => (i === index ? { ...item, ...values } : item)))
                  return (
                    <div className={styles.volume} key={volume.id}>
                      <div className={styles.volumeSource}>
                        <Select
                          label="数据来源"
                          clearable
                          placeholder="随资产创建"
                          value={volume.persistentVolumeId ?? null}
                          onChange={(id) => {
                            const source = availableVolumes.data?.find((item) => item.id === id)
                            update({
                              persistentVolumeId: id ?? undefined,
                              sizeGiB: source?.sizeGiB ?? volume.sizeGiB,
                            })
                            if (source)
                              setStoragePoolId(
                                source.storagePoolId.startsWith('default:') ? null : source.storagePoolId
                              )
                          }}
                          data={
                            !identity.data?.administrator && volume.persistentVolumeId
                              ? [
                                  {
                                    value: volume.persistentVolumeId,
                                    label: '已附加数据卷',
                                  },
                                ]
                              : (availableVolumes.data ?? [])
                                  .filter(
                                    (item) =>
                                      item.kind === template?.kind &&
                                      (item.state === 'ready' || item.id === volume.persistentVolumeId)
                                  )
                                  .map((item) => ({
                                    value: item.id,
                                    label: item.name,
                                  }))
                          }
                        />
                        {!imageMount && (
                          <ActionIcon
                            variant="subtle"
                            color="red"
                            aria-label={`移除数据卷 ${index + 1}`}
                            onClick={() => setVolumes((items) => items.filter((_, i) => i !== index))}
                          >
                            <Trash2 size={16} />
                          </ActionIcon>
                        )}
                      </div>
                      <div className={styles.columns}>
                        <TextInput
                          label={template?.kind === 'vm' ? '盘名' : imageMount ? '镜像挂载目录' : '挂载目录'}
                          value={volume.mountPath}
                          readOnly={imageMount}
                          required
                          onChange={(event) => update({ mountPath: event.currentTarget.value })}
                        />
                        <NumberInput
                          label="容量 · GiB"
                          value={volume.sizeGiB}
                          min={1}
                          disabled={Boolean(volume.persistentVolumeId)}
                          onChange={(value) => update({ sizeGiB: Number(value) })}
                        />
                      </div>
                    </div>
                  )
                })}
                <ErrorMessage error={availableVolumes.error} />
                <Button
                  variant="default"
                  size="compact-sm"
                  leftSection={<Plus size={15} />}
                  onClick={() =>
                    setVolumes((items) => [
                      ...items,
                      {
                        id: randomUUID(),
                        mountPath:
                          template?.kind === 'vm'
                            ? `数据盘 ${items.length + 1}`
                            : `/data${items.length ? items.length + 1 : ''}`,
                        sizeGiB: 10,
                      },
                    ])
                  }
                >
                  添加数据卷
                </Button>
              </div>
            </Collapse>
            {initialized && (
              <>
                <button
                  className={styles.disclosure}
                  type="button"
                  aria-expanded={guestOpen}
                  onClick={() => setGuestOpen(!guestOpen)}
                >
                  来宾设置
                  <ChevronDown size={16} />
                </button>
                <Collapse expanded={guestOpen}>
                  <div className={styles.form}>
                    <div className={styles.columns}>
                      <TextInput
                        label="主机名"
                        value={hostname}
                        onChange={(event) => setHostname(event.currentTarget.value)}
                      />
                      <TextInput
                        label="登录用户"
                        value={username}
                        onChange={(event) => setUsername(event.currentTarget.value)}
                      />
                    </div>
                    <Textarea
                      label="SSH 公钥"
                      placeholder="ssh-ed25519 …"
                      minRows={3}
                      autosize
                      value={sshKeys}
                      onChange={(event) => setSshKeys(event.currentTarget.value)}
                    />
                  </div>
                </Collapse>
              </>
            )}
          </>
        )}
        <div className={styles.footer}>
          <Button fullWidth type="submit" disabled={!templateId}>
            {asset ? '更新资产' : '添加到环境'}
          </Button>
        </div>
      </form>
    </Drawer>
  )
}

export function NetworkEditor({
  network,
  initialExternal = false,
  spec,
  onSave,
  onClose,
}: {
  network?: Network
  initialExternal?: boolean
  spec: EnvironmentSpec
  onSave: (network: Network, routerIds?: string[]) => void
  onClose: () => void
}) {
  const api = useWorkspaceApi()
  const [name, setName] = useState(
    network?.name ?? (initialExternal ? '现场网络' : `交换机 ${spec.networks.length + 1}`)
  )
  const [cidr, setCidr] = useState(network?.cidr ?? (initialExternal ? '' : `10.${spec.networks.length + 1}.0.0/24`))
  const [gateway, setGateway] = useState(network?.gateway ?? '')
  const [routerIds, setRouterIds] = useState(() =>
    network
      ? (spec.routers ?? [])
          .filter((router) => router.interfaces.some((iface) => iface.networkId === network.id))
          .map((router) => router.id)
      : spec.routers?.length === 1
        ? [spec.routers[0].id]
        : []
  )
  const identity = useQuery({ queryKey: ['identity'], queryFn: api.identity })
  const [external, setExternal] = useState(Boolean(network?.external) || initialExternal)
  const [nodeId, setNodeId] = useState(network?.external?.nodeId ?? '')
  const [interfaceName, setInterfaceName] = useState(network?.external?.interface ?? '')
  const [vlan, setVlan] = useState<number | string>(network?.external?.vlan ?? '')
  const [pool, setPool] = useState(network?.allocationPool ?? '')
  const nodes = useCursorList(['nodes', 'external-picker'], (page) => api.nodes(page), {
    enabled: external && identity.data?.administrator,
  })
  useEffect(() => {
    const ready = nodes.data?.filter((item) => item.state === 'ready') ?? []
    if (!nodeId && ready.length === 1 && !nodes.hasNextPage) setNodeId(ready[0].id)
  }, [nodeId, nodes.data, nodes.hasNextPage])
  const interfaces = useQuery({
    queryKey: ['node-interfaces', nodeId],
    queryFn: () => api.nodeInterfaces(nodeId),
    enabled: external && Boolean(nodeId) && identity.data?.administrator,
  })
  const [dnsAssetId, setDnsAssetId] = useState<string | null>(network?.dnsAssetId ?? null)
  const [dnsServers, setDnsServers] = useState(network?.dnsServers?.join(', ') ?? '')
  const [mtu, setMtu] = useState(network?.mtu ?? 1442)
  const [advanced, setAdvanced] = useState(
    Boolean(network?.gateway || network?.dnsAssetId || network?.dnsServers?.length)
  )
  const save = () =>
    onSave(
      {
        id: network?.id ?? randomUUID(),
        name,
        cidr,
        gateway: gateway || undefined,
        external: external
          ? {
              nodeId,
              interface: interfaceName,
              vlan: vlan === '' ? undefined : Number(vlan),
            }
          : undefined,
        allocationPool: external ? pool : undefined,
        devices: external ? network?.devices : undefined,
        dnsAssetId: dnsAssetId || undefined,
        dnsServers: dnsServers
          .split(',')
          .map((item) => item.trim())
          .filter(Boolean),
        mtu,
      },
      external ? undefined : routerIds
    )
  return (
    <Drawer
      opened
      onClose={onClose}
      title={
        network ? (network.external ? '编辑现场网络' : '编辑交换机') : initialExternal ? '接入现场网络' : '添加交换机'
      }
      position="right"
      size={504}
    >
      <form
        className={styles.form}
        onSubmit={(event) => {
          event.preventDefault()
          save()
        }}
      >
        <TextInput
          label={external ? '网络名称' : '交换机名称'}
          required
          value={name}
          onChange={(event) => setName(event.currentTarget.value)}
        />
        {identity.data?.administrator && !initialExternal && (
          <SegmentedControl
            aria-label="网络接入方式"
            value={external ? 'external' : 'internal'}
            onChange={(value) => {
              setExternal(value === 'external')
              setGateway('')
              if (!network) setCidr(value === 'external' ? '' : `10.${spec.networks.length + 1}.0.0/24`)
            }}
            data={[
              { value: 'internal', label: '虚拟网络' },
              { value: 'external', label: '现场网络' },
            ]}
          />
        )}
        {external && (
          <>
            <ErrorMessage error={nodes.error ?? interfaces.error} />
            <Select
              label="接入节点"
              required
              searchable
              value={nodeId}
              onChange={(value) => {
                setNodeId(value ?? '')
                setInterfaceName('')
              }}
              data={(nodes.data ?? []).map((item) => ({
                value: item.id,
                label: item.name,
                disabled: item.state !== 'ready',
              }))}
              disabled={!identity.data?.administrator}
            />
            <LoadMore list={nodes} />
            <Select
              label="接入网口"
              required
              value={interfaceName}
              onChange={(value) => setInterfaceName(value ?? '')}
              placeholder={interfaces.isFetching ? '正在读取网口' : '选择连接现场设备的网口'}
              data={[
                {
                  group: '以太网口',
                  items: (interfaces.data ?? [])
                    .filter((item) => item.available && item.kind === 'ethernet')
                    .map((item) => ({
                      value: item.name,
                      label: `以太网口 · ${item.name}${item.linkUp ? '' : ' · 未连接'}`,
                    })),
                },
                {
                  group: '网桥',
                  items: (interfaces.data ?? [])
                    .filter((item) => item.available && item.kind !== 'ethernet')
                    .map((item) => ({
                      value: item.name,
                      label: `虚拟网桥 · ${item.name}${item.addresses.length ? ` · ${item.addresses.join(', ')}` : ''}`,
                    })),
                },
              ].filter((group) => group.items.length)}
              renderOption={({ option }) => (
                <span className={styles.interfaceOption}>
                  {interfaces.data?.find((item) => item.name === option.value)?.kind === 'ethernet' ? (
                    <Cable size={16} />
                  ) : (
                    <GitBranch size={16} />
                  )}
                  <span>{option.label}</span>
                </span>
              )}
              nothingFoundMessage="没有可用接入口"
              disabled={!identity.data?.administrator}
            />
          </>
        )}
        <TextInput
          label={external ? '现场网段' : '网络地址 / 前缀'}
          placeholder={external ? '例如 192.168.10.0/24' : '10.1.0.0/24'}
          required
          value={cidr}
          onChange={(event) => setCidr(event.currentTarget.value)}
        />
        {!external && Boolean(spec.routers?.length) && (
          <MultiSelect
            label="连接路由器"
            placeholder="不连接路由器"
            value={routerIds}
            onChange={setRouterIds}
            data={(spec.routers ?? []).map((router) => ({
              value: router.id,
              label: router.name,
            }))}
          />
        )}
        {external && (
          <>
            <TextInput
              label="虚拟资产预留地址段"
              placeholder="例如 192.168.10.128/26"
              required
              value={pool}
              onChange={(event) => setPool(event.currentTarget.value)}
              disabled={!identity.data?.administrator}
            />
            <TextInput
              label="现有网关"
              placeholder="无网关可留空"
              value={gateway}
              onChange={(event) => setGateway(event.currentTarget.value)}
            />
          </>
        )}
        <button
          className={styles.disclosure}
          type="button"
          aria-expanded={advanced}
          onClick={() => setAdvanced(!advanced)}
        >
          网络参数
          <ChevronDown size={16} />
        </button>
        <Collapse expanded={advanced}>
          <div className={styles.form}>
            {external && (
              <NumberInput
                label="VLAN"
                placeholder="不使用 VLAN"
                value={vlan}
                min={1}
                max={4094}
                allowDecimal={false}
                onChange={setVlan}
                disabled={!identity.data?.administrator}
              />
            )}
            {!external && (
              <TextInput
                label="网关"
                placeholder="自动分配"
                value={gateway}
                onChange={(event) => setGateway(event.currentTarget.value)}
              />
            )}
            <Select
              label="环境内 DNS"
              clearable
              searchable
              placeholder="选择 DNS 资产"
              value={dnsAssetId}
              onChange={setDnsAssetId}
              data={spec.assets.map((asset) => ({
                value: asset.id,
                label: asset.name,
              }))}
            />
            <TextInput
              label="外部 DNS"
              placeholder="例如：1.1.1.1, 8.8.8.8"
              value={dnsServers}
              onChange={(event) => setDnsServers(event.currentTarget.value)}
            />
            <NumberInput label="MTU" min={1280} max={9000} value={mtu} onChange={(value) => setMtu(Number(value))} />
          </div>
        </Collapse>
        <div className={styles.footer}>
          <Button fullWidth type="submit">
            {network ? '保存' : '添加到环境'}
          </Button>
        </div>
      </form>
    </Drawer>
  )
}
