import { Cpu } from 'lucide-react'
import useSWR from 'swr'
import { teamLabResourceKeys, teamLabResourcesApi } from '../../api'
import { connectorKindLabels } from '../../resources/resourcesPresentation'
import { InspectorSection, SelectInput, TextAreaInput } from './InspectorFields'
import type { TopologyAssetNode } from '../../model/topologyDocument'

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
}: {
  node: TopologyAssetNode
  onAssetChange: (patch: Partial<TopologyAssetNode>) => void
  readOnly?: boolean
}) {
  const packages = useSWR(teamLabResourceKeys.devicePackages(null), () =>
    teamLabResourcesApi.listDevicePackages({ limit: 100 })
  )
  const connectors = useSWR(teamLabResourceKeys.connectors(), () =>
    teamLabResourcesApi.listConnectors({ limit: 100 })
  )
  const expectedKind = node.type === 'docker' ? 'docker' : 'vm'
  const packageOptions = (packages.data?.items ?? []).filter(
    (item) => item.enabled && !item.archived && item.supportedAssetKinds.includes(expectedKind)
  )
  const connectorOptions = (connectors.data?.items ?? []).filter((item) => !item.archived)
  const boundPackage = packageOptions.find((item) => String(item.id) === String(node.devicePackageId ?? ''))
  const boundConnector = connectorOptions.find((item) => String(item.id) === (node.connectorId ?? ''))
  const boundPackageMissing = Boolean(node.devicePackageId) && !boundPackage
  const boundConnectorMissing = Boolean(node.connectorId) && !boundConnector

  const updateParameters = (text: string) => {
    onAssetChange({ deviceParameters: text.trim() ? text : null })
  }

  return (
    <InspectorSection icon={<Cpu aria-hidden="true" size={16} />} title="扩展能力">
      <SelectInput
        disabled={readOnly}
        help="设备包承载工控仿真、蜜罐等协议模拟能力，由外部制品流水线发布。"
        label="设备包"
        onChange={(value) => {
          const packageId = Number(value)
          onAssetChange({
            devicePackageId: packageId > 0 ? packageId : null,
            deviceParameters: null,
          })
        }}
        value={String(node.devicePackageId ?? 0)}
      >
        <option value="0">无（仅镜像运行）</option>
        {boundPackageMissing && node.devicePackageId ? (
          <option value={String(node.devicePackageId)}>当前设备包 #{node.devicePackageId}（不可用）</option>
        ) : null}
        {packageOptions.map((item) => (
          <option key={item.id} value={item.id}>
            {item.displayName} · {item.version} (#{item.id})
          </option>
        ))}
      </SelectInput>
      {node.devicePackageId ? (
        <TextAreaInput
          disabled={readOnly}
          help="作者可配置参数，发布时冻结；语义校验由设备包运行时执行。留空表示使用默认值。"
          label="设备包参数（JSON）"
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
