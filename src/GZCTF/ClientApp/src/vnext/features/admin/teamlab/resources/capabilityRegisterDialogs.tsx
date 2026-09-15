import { useEffect, useId, useState } from 'react'
import { Plus, Trash2 } from 'lucide-react'
import { ActionButton, InlineFeedback, VNextDialog } from '../../../../shared/Interaction'
import { errorMessage } from '../../../../shared/errors'
import { teamLabResourcesApi } from '../api'
import type {
  RegisterTeamLabConnectorRequest,
  RegisterTeamLabDevicePackageRequest,
  TeamLabConnectorKind,
  TeamLabDeviceArtifactKind,
} from '../api/teamlabResourcesContracts'
import styles from './TeamLabResourcesPage.module.css'
import { useConnectorInterfaces, useConnectorNodes } from './useTeamLabResources'

/**
 * Device packages are produced by the external artifact pipeline; this dialog
 * only registers the immutable reference and capability declaration, it never
 * uploads content.
 */
export function DevicePackageRegisterDialog({
  open,
  onClose,
  onRegistered,
}: {
  open: boolean
  onClose: () => void
  onRegistered: () => void
}) {
  const formId = useId()
  const [form, setForm] = useState({
    name: '',
    displayName: '',
    version: '',
    artifactKind: 'oci-image' as TeamLabDeviceArtifactKind,
    artifactReference: '',
    digest: '',
    supportedAssetKinds: ['docker'] as Array<'docker' | 'vm'>,
    cpuMillis: '500',
    memoryMiB: '256',
    storageGib: '4',
  })
  const [ports, setPorts] = useState([{ name: 'service', port: '502', protocol: 'tcp' }])
  const [parameters, setParameters] = useState<{ name: string; type: 'string' | 'number' | 'boolean'; required: boolean }[]>([])
  const [healthEnabled, setHealthEnabled] = useState(true)
  const [healthKind, setHealthKind] = useState<'tcp' | 'http'>('tcp')
  const [healthPort, setHealthPort] = useState('502')
  const [healthPath, setHealthPath] = useState('/')
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState<unknown>(null)

  useEffect(() => {
    if (!open) setError(null)
  }, [open])

  const patch = (changes: Partial<typeof form>) => setForm((current) => ({ ...current, ...changes }))

  const register = async () => {
    if (submitting) return
    setSubmitting(true)
    setError(null)
    try {
      const request: RegisterTeamLabDevicePackageRequest = {
        name: form.name.trim(),
        displayName: form.displayName.trim(),
        version: form.version.trim(),
        artifactKind: form.artifactKind,
        artifactReference: form.artifactReference.trim(),
        digest: form.digest.trim() || null,
        supportedAssetKinds: form.supportedAssetKinds,
        cpuMillis: Number(form.cpuMillis) || 0,
        memoryMiB: Number(form.memoryMiB) || 0,
        storageGib: Number(form.storageGib) || 0,
        ports: ports.filter(item => item.name.trim() && Number(item.port) > 0).map(item => ({
          name: item.name.trim(), port: Number(item.port), protocol: item.protocol,
        })),
        parameterSchema: parameters.length ? {
          type: 'object',
          properties: Object.fromEntries(parameters.filter(item => item.name.trim()).map(item => [item.name.trim(), { type: item.type }])),
          required: parameters.filter(item => item.required && item.name.trim()).map(item => item.name.trim()),
        } : undefined,
        healthDeclaration: healthEnabled ? {
          kind: healthKind, port: Number(healthPort), ...(healthKind === 'http' ? { path: healthPath || '/' } : {}),
        } : undefined,
        protocolEventTypes: [],
      }
      if (request.name && request.displayName && request.version && request.artifactReference && request.supportedAssetKinds.length) {
        await teamLabResourcesApi.registerDevicePackage(request)
        onRegistered()
      }
    } catch (reason) {
      setError(reason)
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <VNextDialog
      description="登记可在场景中复用的容器或虚拟机镜像，并说明它需要的资源、端口和启动参数。"
      eyebrow="DEVICE TEMPLATE"
      footer={
        <>
          <ActionButton disabled={submitting} onClick={onClose} type="button">取消</ActionButton>
          <ActionButton
            disabled={submitting || !form.name.trim() || !form.displayName.trim() || !form.version.trim() || !form.artifactReference.trim() || form.supportedAssetKinds.length === 0}
            onClick={() => void register()}
            tone="primary"
            type="button"
          >
            {submitting ? '正在登记' : '登记设备模板'}
          </ActionButton>
        </>
      }
      onClose={() => {
        if (!submitting) onClose()
      }}
      open={open}
      title="登记设备模板"
    >
      <div className={styles.dialogForm}>
        <TextFieldRow id={`${formId}-name`} label="名称（唯一标识）" value={form.name} onChange={(value) => patch({ name: value })} placeholder="plc-simulator" />
        <TextFieldRow id={`${formId}-display`} label="显示名称" value={form.displayName} onChange={(value) => patch({ displayName: value })} placeholder="PLC 模拟器" />
        <TextFieldRow id={`${formId}-version`} label="版本" value={form.version} onChange={(value) => patch({ version: value })} placeholder="1.0.0" />
        <label htmlFor={`${formId}-kind`}>制品类型</label>
        <select
          aria-label="制品类型"
          id={`${formId}-kind`}
          onChange={(event) => patch({ artifactKind: event.currentTarget.value as TeamLabDeviceArtifactKind })}
          value={form.artifactKind}
        >
          <option value="oci-image">OCI 镜像</option>
          <option value="vm-image">VM 镜像</option>
        </select>
        <TextFieldRow id={`${formId}-reference`} label="制品引用" value={form.artifactReference} onChange={(value) => patch({ artifactReference: value })} placeholder="registry.example.com/yinyu/plc-simulator:1.0.0" />
        <TextFieldRow id={`${formId}-digest`} label="sha256 摘要（可选）" value={form.digest} onChange={(value) => patch({ digest: value })} placeholder="sha256:…" />
        <fieldset className={styles.dialogChoices}>
          <legend>支持的资产类型</legend>
          {([['docker', 'Docker 容器'], ['vm', '虚拟机']] as const).map(([kind, label]) => (
            <label className={styles.dialogToggle} key={kind}>
              <input
                checked={form.supportedAssetKinds.includes(kind)}
                onChange={(event) => patch({
                  supportedAssetKinds: event.currentTarget.checked
                    ? [...form.supportedAssetKinds, kind]
                    : form.supportedAssetKinds.filter(item => item !== kind),
                })}
                type="checkbox"
              />
              {label}
            </label>
          ))}
        </fieldset>
        <div className={styles.dialogFormGrid}>
          <TextFieldRow id={`${formId}-cpu`} label="CPU（毫核）" value={form.cpuMillis} onChange={(value) => patch({ cpuMillis: value })} />
          <TextFieldRow id={`${formId}-memory`} label="内存（MiB）" value={form.memoryMiB} onChange={(value) => patch({ memoryMiB: value })} />
          <TextFieldRow id={`${formId}-storage`} label="存储（GiB）" value={form.storageGib} onChange={(value) => patch({ storageGib: value })} />
        </div>
        <label>服务端口</label>
        {ports.map((item, index) => <div className={styles.dialogFormGrid} key={index}>
          <TextFieldRow id={`${formId}-port-name-${index}`} label="名称" value={item.name} onChange={(value) => setPorts(current => current.map((entry, offset) => offset === index ? { ...entry, name: value } : entry))} />
          <TextFieldRow id={`${formId}-port-${index}`} label="端口" value={item.port} onChange={(value) => setPorts(current => current.map((entry, offset) => offset === index ? { ...entry, port: value } : entry))} />
          <label>协议<select value={item.protocol} onChange={(event) => setPorts(current => current.map((entry, offset) => offset === index ? { ...entry, protocol: event.currentTarget.value } : entry))}><option value="tcp">TCP</option><option value="udp">UDP</option></select></label>
          <ActionButton aria-label="删除端口" icon={<Trash2 size={15} />} onClick={() => setPorts(current => current.filter((_, offset) => offset !== index))} type="button" />
        </div>)}
        <ActionButton icon={<Plus size={15} />} onClick={() => setPorts(current => [...current, { name: '', port: '', protocol: 'tcp' }])} type="button">添加端口</ActionButton>
        <label>启动参数</label>
        {parameters.map((item, index) => <div className={styles.dialogFormGrid} key={index}>
          <TextFieldRow id={`${formId}-parameter-${index}`} label="参数名" value={item.name} onChange={(value) => setParameters(current => current.map((entry, offset) => offset === index ? { ...entry, name: value } : entry))} />
          <label>类型<select value={item.type} onChange={(event) => setParameters(current => current.map((entry, offset) => offset === index ? { ...entry, type: event.currentTarget.value as typeof item.type } : entry))}><option value="string">文本</option><option value="number">数字</option><option value="boolean">开关</option></select></label>
          <label className={styles.dialogToggle}><input checked={item.required} onChange={(event) => setParameters(current => current.map((entry, offset) => offset === index ? { ...entry, required: event.currentTarget.checked } : entry))} type="checkbox" />必填</label>
          <ActionButton aria-label="删除参数" icon={<Trash2 size={15} />} onClick={() => setParameters(current => current.filter((_, offset) => offset !== index))} type="button" />
        </div>)}
        <ActionButton icon={<Plus size={15} />} onClick={() => setParameters(current => [...current, { name: '', type: 'string', required: false }])} type="button">添加参数</ActionButton>
        <label className={styles.dialogToggle}><input checked={healthEnabled} onChange={(event) => setHealthEnabled(event.currentTarget.checked)} type="checkbox" />启动后检查服务是否可用</label>
        {healthEnabled ? <div className={styles.dialogFormGrid}>
          <label>检查方式<select value={healthKind} onChange={(event) => setHealthKind(event.currentTarget.value as 'tcp' | 'http')}><option value="tcp">TCP 端口</option><option value="http">HTTP 地址</option></select></label>
          <TextFieldRow id={`${formId}-health-port`} label="端口" value={healthPort} onChange={setHealthPort} />
          {healthKind === 'http' ? <TextFieldRow id={`${formId}-health-path`} label="访问路径" value={healthPath} onChange={setHealthPath} /> : null}
        </div> : null}
        {error ? <InlineFeedback tone="danger">{errorMessage(error, '设备模板登记失败。')}</InlineFeedback> : null}
      </div>
    </VNextDialog>
  )
}
export function ConnectorRegisterDialog({
  open,
  onClose,
  onRegistered,
}: {
  open: boolean
  onClose: () => void
  onRegistered: () => void
}) {
  const formId = useId()
  const [form, setForm] = useState({
    name: '',
    displayName: '',
    kind: 'managed-nic' as TeamLabConnectorKind,
    controlScopeId: '',
    supportsSharedUse: false,
    capacity: '1',
    attachmentReference: '',
    nodeId: '',
    interfaceName: '',
    macAddress: '',
    description: '',
  })
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState<unknown>(null)
  const [interfaceMode, setInterfaceMode] = useState<'auto' | 'manual'>('auto')

  useEffect(() => {
    if (!open) setError(null)
  }, [open])

  const patch = (changes: Partial<typeof form>) => setForm((current) => ({ ...current, ...changes }))
  const nodes = useConnectorNodes(open)
  const interfaces = useConnectorInterfaces(form.nodeId, open && interfaceMode === 'auto')

  const register = async () => {
    if (submitting) return
    setSubmitting(true)
    setError(null)
    try {
      await teamLabResourcesApi.registerConnector({
        name: form.name.trim(),
        displayName: form.displayName.trim(),
        kind: form.kind,
        controlScopeId: form.controlScopeId.trim() || null,
        supportsSharedUse: form.supportsSharedUse,
        capacity: Math.max(1, Number(form.capacity) || 1),
        attachmentReference: form.attachmentReference.trim() || null,
        description: form.description.trim() || null,
        managedNic: { nodeId: form.nodeId, interfaceName: form.interfaceName.trim(), macAddress: form.macAddress.trim() },
      } satisfies RegisterTeamLabConnectorRequest)
      onRegistered()
    } catch (reason) {
      setError(reason)
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <VNextDialog
      description="将节点上的专用网卡接入资产的主网段。网卡需已启用、接线，且没有主机 IP；设备地址按运行时网段配置。当前仅支持独占接入，同一场景的连接器需位于同一节点。"
      eyebrow="FIELD CONNECTOR"
      footer={
        <>
          <ActionButton disabled={submitting} onClick={onClose} type="button">取消</ActionButton>
          <ActionButton
            disabled={submitting || !form.name.trim() || !form.displayName.trim() || !form.nodeId || !form.interfaceName.trim() || !form.macAddress.trim()}
            onClick={() => void register()}
            tone="primary"
            type="button"
          >
            {submitting ? '正在登记' : '登记连接器'}
          </ActionButton>
        </>
      }
      onClose={() => {
        if (!submitting) onClose()
      }}
      open={open}
      title="登记现场连接器"
    >
      <div className={styles.dialogForm}>
        <TextFieldRow id={`${formId}-name`} label="名称（唯一标识）" value={form.name} onChange={(value) => patch({ name: value })} placeholder="field-vlan-1" />
        <TextFieldRow id={`${formId}-display`} label="显示名称" value={form.displayName} onChange={(value) => patch({ displayName: value })} placeholder="现场 VLAN 1" />
        <label htmlFor={`${formId}-kind`}>类型</label>
        <select
          aria-label="连接器类型"
          id={`${formId}-kind`}
          onChange={(event) => patch({ kind: event.currentTarget.value as TeamLabConnectorKind })}
          value={form.kind}
        >
          <option value="managed-nic">受管网卡</option>
          <option disabled value="vlan">VLAN（尚未支持执行）</option>
          <option disabled value="segment">网段（尚未支持执行）</option>
          <option disabled value="serial">串口（尚未支持执行）</option>
          <option disabled value="usb-gateway">USB 设备网关（尚未支持执行）</option>
          <option disabled value="dedicated-network">专用外部网络（尚未支持执行）</option>
        </select>
        <TextFieldRow id={`${formId}-scope`} label="授权控制范围 ID（留空表示平台级）" value={form.controlScopeId} onChange={(value) => patch({ controlScopeId: value })} />
        <label className={styles.dialogToggle}>
          <input
            checked={form.supportsSharedUse}
            disabled
            onChange={(event) => patch({ supportsSharedUse: event.currentTarget.checked })}
            type="checkbox"
          />
          共享使用（专用网卡不支持）
        </label>
        {form.supportsSharedUse ? (
          <TextFieldRow id={`${formId}-capacity`} label="共享容量（1-64）" value={form.capacity} onChange={(value) => patch({ capacity: value })} />
        ) : null}
        <div className={styles.dialogField}>
          <label htmlFor={`${formId}-node`}>所属节点</label>
          <select id={`${formId}-node`} value={form.nodeId} onChange={(event) => patch({ nodeId: event.currentTarget.value, interfaceName: '', macAddress: '' })}>
            <option value="">请选择节点</option>
            {nodes.data?.map((node) => <option key={node.id} value={node.id}>{node.name}</option>)}
          </select>
        </div>
        {nodes.error ? <InlineFeedback tone="danger">节点读取失败，请关闭后重试。</InlineFeedback> : null}
        <label className={styles.dialogToggle}><input checked={interfaceMode === 'auto'} onChange={() => setInterfaceMode('auto')} type="radio" />从节点网卡中选择</label>
        <label className={styles.dialogToggle}><input checked={interfaceMode === 'manual'} onChange={() => setInterfaceMode('manual')} type="radio" />手工填写</label>
        {interfaceMode === 'auto' ? <div className={styles.dialogField}>
          <label htmlFor={`${formId}-interface-choice`}>专用网卡</label>
          <select id={`${formId}-interface-choice`} value={form.interfaceName} onChange={(event) => {
            const selected = interfaces.data?.find(item => item.name === event.currentTarget.value)
            patch({ interfaceName: selected?.name ?? '', macAddress: selected?.macAddress ?? '' })
          }}>
            <option value="">请选择网卡</option>
            {interfaces.data?.map(item => <option key={item.name} value={item.name}>{item.name} · {item.linkUp ? '已连接' : '未连接'}{item.addresses.length ? ` · ${item.addresses.join(', ')}` : ''}</option>)}
          </select>
          {interfaces.error ? <InlineFeedback tone="danger">无法读取节点网卡，可切换为手工填写。</InlineFeedback> : null}
        </div> : <>
          <TextFieldRow id={`${formId}-interface`} label="专用网卡名称" value={form.interfaceName} onChange={(value) => patch({ interfaceName: value })} placeholder="enp2s0" />
          <TextFieldRow id={`${formId}-mac`} label="网卡 MAC 地址" value={form.macAddress} onChange={(value) => patch({ macAddress: value })} placeholder="02:00:00:00:00:01" />
        </>}
        <TextFieldRow id={`${formId}-description`} label="描述（可选）" value={form.description} onChange={(value) => patch({ description: value })} />
        {error ? <InlineFeedback tone="danger">{errorMessage(error, '连接器登记失败。')}</InlineFeedback> : null}
      </div>
    </VNextDialog>
  )
}
function TextFieldRow({
  id,
  label,
  value,
  onChange,
  placeholder,
}: {
  id: string
  label: string
  value: string
  onChange: (value: string) => void
  placeholder?: string
}) {
  return (
    <div className={styles.dialogField}>
      <label htmlFor={id}>{label}</label>
      <input
        autoComplete="off"
        id={id}
        onChange={(event) => onChange(event.currentTarget.value)}
        placeholder={placeholder}
        value={value}
      />
    </div>
  )
}
