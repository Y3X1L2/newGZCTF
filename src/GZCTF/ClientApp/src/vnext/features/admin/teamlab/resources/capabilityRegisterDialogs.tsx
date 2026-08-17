import { useEffect, useId, useState } from 'react'
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
    supportedAssetKinds: 'docker',
    cpuMillis: '500',
    memoryMiB: '256',
    storageGib: '4',
    parameterSchema: '',
    healthDeclaration: '',
    protocolEventTypes: '',
  })
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
        supportedAssetKinds: form.supportedAssetKinds
          .split(/[,，\s]+/)
          .filter(Boolean)
          .map((kind) => kind.trim().toLowerCase()),
        cpuMillis: Number(form.cpuMillis) || 0,
        memoryMiB: Number(form.memoryMiB) || 0,
        storageGib: Number(form.storageGib) || 0,
        parameterSchema: parseOptionalJson(form.parameterSchema, '参数 schema', setError),
        healthDeclaration: parseOptionalJson(form.healthDeclaration, '健康声明', setError),
        protocolEventTypes: form.protocolEventTypes
          .split(/[,，\s]+/)
          .filter(Boolean)
          .map((entry) => entry.trim().toLowerCase()),
      }
      if (request.name && request.displayName && request.version && request.artifactReference) {
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
      description="登记外部流水线产出的不可变制品引用；平台不制作镜像内容。"
      eyebrow="DEVICE PACKAGE"
      footer={
        <>
          <ActionButton disabled={submitting} onClick={onClose} type="button">取消</ActionButton>
          <ActionButton
            disabled={submitting || !form.name.trim() || !form.displayName.trim() || !form.version.trim() || !form.artifactReference.trim()}
            onClick={() => void register()}
            tone="primary"
            type="button"
          >
            {submitting ? '正在登记' : '登记设备包'}
          </ActionButton>
        </>
      }
      onClose={() => {
        if (!submitting) onClose()
      }}
      open={open}
      title="登记设备包"
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
        <TextFieldRow id={`${formId}-kinds`} label="支持的资产类型（逗号分隔：docker, vm）" value={form.supportedAssetKinds} onChange={(value) => patch({ supportedAssetKinds: value })} />
        <div className={styles.dialogFormGrid}>
          <TextFieldRow id={`${formId}-cpu`} label="CPU（毫核）" value={form.cpuMillis} onChange={(value) => patch({ cpuMillis: value })} />
          <TextFieldRow id={`${formId}-memory`} label="内存（MiB）" value={form.memoryMiB} onChange={(value) => patch({ memoryMiB: value })} />
          <TextFieldRow id={`${formId}-storage`} label="存储（GiB）" value={form.storageGib} onChange={(value) => patch({ storageGib: value })} />
        </div>
        <label htmlFor={`${formId}-schema`}>参数 schema（JSON，可选）</label>
        <textarea
          aria-label="参数 schema"
          id={`${formId}-schema`}
          onChange={(event) => patch({ parameterSchema: event.currentTarget.value })}
          placeholder='{"type":"object","properties":{}}'
          rows={3}
          value={form.parameterSchema}
        />
        <label htmlFor={`${formId}-health`}>健康声明（JSON，可选）</label>
        <textarea
          aria-label="健康声明"
          id={`${formId}-health`}
          onChange={(event) => patch({ healthDeclaration: event.currentTarget.value })}
          placeholder='{"kind":"tcp","port":502}'
          rows={2}
          value={form.healthDeclaration}
        />
        <TextFieldRow id={`${formId}-events`} label="协议事件类型（逗号分隔，可选）" value={form.protocolEventTypes} onChange={(value) => patch({ protocolEventTypes: value })} placeholder="modbus-read, modbus-write" />
        {error ? <InlineFeedback tone="danger">{errorMessage(error, '设备包登记失败。')}</InlineFeedback> : null}
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
    kind: 'vlan' as TeamLabConnectorKind,
    controlScopeId: '',
    supportsSharedUse: false,
    capacity: '1',
    attachmentReference: '',
    description: '',
  })
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
      await teamLabResourcesApi.registerConnector({
        name: form.name.trim(),
        displayName: form.displayName.trim(),
        kind: form.kind,
        controlScopeId: form.controlScopeId.trim() || null,
        supportsSharedUse: form.supportsSharedUse,
        capacity: Math.max(1, Number(form.capacity) || 1),
        attachmentReference: form.attachmentReference.trim() || null,
        description: form.description.trim() || null,
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
      description="登记经管理员授权的真实资源；场景只引用连接器 ID，不保存地址或凭据。"
      eyebrow="FIELD CONNECTOR"
      footer={
        <>
          <ActionButton disabled={submitting} onClick={onClose} type="button">取消</ActionButton>
          <ActionButton
            disabled={submitting || !form.name.trim() || !form.displayName.trim()}
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
          <option value="vlan">VLAN</option>
          <option value="segment">网段</option>
          <option value="serial">串口</option>
          <option value="usb-gateway">USB 设备网关</option>
          <option value="dedicated-network">专用外部网络</option>
        </select>
        <TextFieldRow id={`${formId}-scope`} label="授权控制范围 ID（留空表示平台级）" value={form.controlScopeId} onChange={(value) => patch({ controlScopeId: value })} />
        <label className={styles.dialogToggle}>
          <input
            checked={form.supportsSharedUse}
            onChange={(event) => patch({ supportsSharedUse: event.currentTarget.checked })}
            type="checkbox"
          />
          允许共享使用（默认独占）
        </label>
        {form.supportsSharedUse ? (
          <TextFieldRow id={`${formId}-capacity`} label="共享容量（1-64）" value={form.capacity} onChange={(value) => patch({ capacity: value })} />
        ) : null}
        <TextFieldRow id={`${formId}-reference`} label="接入引用（运维内部，可选）" value={form.attachmentReference} onChange={(value) => patch({ attachmentReference: value })} />
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
    <>
      <label htmlFor={id}>{label}</label>
      <input
        autoComplete="off"
        id={id}
        onChange={(event) => onChange(event.currentTarget.value)}
        placeholder={placeholder}
        value={value}
      />
    </>
  )
}

function parseOptionalJson(
  text: string,
  label: string,
  onError: (error: unknown) => void
): unknown {
  const trimmed = text.trim()
  if (!trimmed) return undefined
  try {
    return JSON.parse(trimmed)
  } catch {
    onError(new Error(`${label} 不是合法 JSON`))
    throw new Error(`${label} 不是合法 JSON`)
  }
}
