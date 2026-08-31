import { Container, Monitor, MonitorCog } from 'lucide-react'
import { updateTopologyNode } from '../../model/topologyCommands'
import type { TopologyAssetNode } from '../../model/topologyDocument'
import type { TeamLabImageOption } from '../../api'
import { CapabilityBindingEditor } from './CapabilityBindingEditor'
import { HealthCheckEditor } from './HealthCheckEditor'
import { InspectorSection, SelectInput, TextInput } from './InspectorFields'
import type { InspectorDocumentProps } from './inspectorTypes'
import { NetworkInterfacesEditor } from './NetworkInterfacesEditor'
import { ObservationEditor } from './ObservationEditor'
import { ResourceRequirementsEditor } from './ResourceRequirementsEditor'

const typePresentation = {
  docker: { label: 'Docker 资产', icon: <Container aria-hidden="true" size={16} /> },
  'linux-vm': { label: 'Linux 虚拟机', icon: <MonitorCog aria-hidden="true" size={16} /> },
  'windows-vm': { label: 'Windows 虚拟机', icon: <Monitor aria-hidden="true" size={16} /> },
} as const

export function AssetInspector({
  document,
  node,
  onDocumentChange,
  readOnly,
  imageOptions,
}: InspectorDocumentProps & { node: TopologyAssetNode; imageOptions: readonly TeamLabImageOption[] }) {
  const update = (patch: Partial<TopologyAssetNode>) => {
    onDocumentChange(updateTopologyNode(document, { ...node, ...patch } as TopologyAssetNode).document)
  }
  const presentation = typePresentation[node.type]
  const compatibleImages = imageOptions.filter((option) => option.deviceType === node.type)
  const currentAvailable = compatibleImages.some((option) => option.id === node.imageTemplateId)

  return (
    <>
      <InspectorSection icon={presentation.icon} title={presentation.label}>
        <TextInput disabled={readOnly} label="资产名称" onChange={(name) => update({ name })} value={node.name} />
        <SelectInput
          disabled={readOnly}
          label="镜像模板"
          onChange={(value) => update({ imageTemplateId: Number(value) })}
          value={String(node.imageTemplateId)}
        >
          {node.imageTemplateId <= 0 ? <option value="0">请选择可用镜像</option> : null}
          {!currentAvailable && node.imageTemplateId > 0 ? <option value={node.imageTemplateId}>当前模板 #{node.imageTemplateId}（不可用）</option> : null}
          {compatibleImages.map((option) => <option key={option.id} value={option.id}>{option.name} (#{option.id}){option.remoteAccessProtocol === 'ssh' ? ' - 已配置 SSH 运维' : option.remoteAccessProtocol === 'rdp' ? ' - 已配置 RDP 运维' : ' - 未配置运维接入'}</option>)}
        </SelectInput>
      </InspectorSection>

      <ResourceRequirementsEditor onChange={(resources) => update({ resources })} readOnly={readOnly} resources={node.resources} />
      <CapabilityBindingEditor node={node} onAssetChange={update} readOnly={readOnly} />
      <NetworkInterfacesEditor document={document} nodeKey={node.key} onDocumentChange={onDocumentChange} readOnly={readOnly} />
      <HealthCheckEditor healthCheck={node.healthCheck} onChange={(healthCheck) => update({ healthCheck })} readOnly={readOnly} />

      <ObservationEditor endpointMode={node.endpointObservation} onEndpointModeChange={(endpointObservation) => update({ endpointObservation })} readOnly={readOnly} />
    </>
  )
}
