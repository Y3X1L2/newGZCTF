import { Cpu } from 'lucide-react'
import useSWR from 'swr'
import { teamLabResourceKeys, teamLabResourcesApi } from '../../api'
import { connectorKindLabels } from '../../resources/resourcesPresentation'
import { InspectorSection, SelectInput } from './InspectorFields'
import { DeviceParametersEditor } from './DeviceParametersEditor'
import type { TopologyAssetNode } from '../../model/topologyDocument'
import type { TeamLabImageOption } from '../../api/teamlabImageCatalog'

/**
 * Industrial emulation and field-integration binding for an asset. Device
 * packages come from the external artifact pipeline; connectors are
 * admin-registered real resources referenced by id only. Both selections are
 * optional so plain image-backed assets stay untouched.
 */
export function CapabilityBindingEditor({
  node,
  onAssetChange,
  readOnly,
  imageOptions,
}: {
  node: TopologyAssetNode
  onAssetChange: (patch: Partial<TopologyAssetNode>) => void
  readOnly?: boolean
  imageOptions: readonly TeamLabImageOption[]
}) {
  const packages = useSWR(teamLabResourceKeys.devicePackages(null), () =>
    teamLabResourcesApi.listDevicePackages({ limit: 100 })
  )
  const connectors = useSWR(teamLabResourceKeys.connectors(), () =>
    teamLabResourcesApi.listConnectors({ limit: 100 })
  )
  const expectedKind = node.type === 'docker' ? 'docker' : 'vm'
  const packageOptions = (packages.data?.items ?? []).filter(
    (item) => item.enabled && !item.archived && (item.bindingId ?? 0) > 0 && item.supportedAssetKinds.includes(expectedKind)
  )
  const connectorOptions = (connectors.data?.items ?? []).filter((item) => !item.archived)
  const boundPackage = packageOptions.find((item) => item.bindingId === node.devicePackageId)
  const boundConnector = connectorOptions.find((item) => String(item.id) === (node.connectorId ?? ''))
  const boundPackageMissing = Boolean(node.devicePackageId) && !boundPackage
  const boundConnectorMissing = Boolean(node.connectorId) && !boundConnector
  const digest = (value: string | null | undefined) => value?.replace(/^sha256:/i, '').toLowerCase()
  const imageFor = (value: string | null) => value ? imageOptions.find(item => item.id === node.imageTemplateId && digest(item.digest) === digest(value))
    ?? imageOptions.find(item => digest(item.digest) === digest(value)) : undefined

  const updateParameters = (text: string) => {
    onAssetChange({ deviceParameters: text.trim() ? text : null })
  }

  return (
    <InspectorSection icon={<Cpu aria-hidden="true" size={16} />} title="扩展能力">
      {packages.error || connectors.error ? <p role="alert">扩展资源加载失败，请刷新重试；现有绑定不会被清除。</p> : null}
      <SelectInput
        disabled={readOnly}
        help="设备包承载工控仿真、蜜罐等协议模拟能力，由外部制品流水线发布。"
        label="设备包"
        onChange={(value) => {
          const packageId = Number(value)
          const selected = packageOptions.find(item => item.bindingId === packageId)
          const image = selected ? imageFor(selected.digest) : undefined
          if (packageId > 0 && (!selected || !image)) return
          onAssetChange({
            devicePackageId: packageId > 0 ? packageId : null,
            deviceParameters: null,
            ...(selected && image ? { imageTemplateId: image.id, resources: {
              cpuUnits: Math.max(node.resources.cpuUnits, Math.ceil(selected.cpuMillis / 1000)),
              memoryMiB: Math.max(node.resources.memoryMiB, selected.memoryMiB),
              storageMiB: Math.max(node.resources.storageMiB, selected.storageGib * 1024),
            } } : {}),
          })
        }}
        value={String(node.devicePackageId ?? 0)}
      >
        <option value="0">无（仅镜像运行）</option>
        {boundPackageMissing && node.devicePackageId ? (
          <option value={String(node.devicePackageId)}>当前设备包 #{node.devicePackageId}（不可用）</option>
        ) : null}
        {packageOptions.map((item) => (
          <option key={item.id} value={item.bindingId} disabled={!imageFor(item.digest)}>
            {item.displayName} · {item.version}{!imageFor(item.digest) ? '（请先导入对应镜像）' : ''}
          </option>
        ))}
      </SelectInput>
      {node.devicePackageId ? (
        <DeviceParametersEditor
          key={`${node.key}:${node.devicePackageId}`}
          schema={boundPackage?.parameterSchema}
          disabled={readOnly}
          onChange={updateParameters}
          value={node.deviceParameters ?? ''}
        />
      ) : null}
      <SelectInput
        disabled={readOnly}
        help="把资产接入登记过的真实网段或设备；连接器同一时间通常只归属一个运行环境。"
        label="现场连接器"
        onChange={(value) => onAssetChange({ connectorId: value || null })}
        value={node.connectorId ?? ''}
      >
        <option value="">无（纯虚拟场景）</option>
        {boundConnectorMissing ? <option value={node.connectorId ?? ''}>当前连接器（不可用）</option> : null}
        {connectorOptions.map((item) => (
          <option key={item.id} value={item.id}>
            {item.displayName}（{connectorKindLabels[item.kind]}）
          </option>
        ))}
      </SelectInput>
    </InspectorSection>
  )
}
