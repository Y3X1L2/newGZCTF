import { Container, Monitor, MonitorCog } from 'lucide-react'
import { Link } from 'react-router'
import type { TeamLabImageOption } from '../../api'
import { updateTopologyNode } from '../../model/topologyCommands'
import type { TopologyAssetNode } from '../../model/topologyDocument'
import { CapabilityBindingEditor } from './CapabilityBindingEditor'
import { GuestNetworkModeEditor } from './GuestNetworkModeEditor'
import { HealthCheckEditor } from './HealthCheckEditor'
import { InspectorSection, SelectInput, TextInput } from './InspectorFields'
import { NetworkInterfacesEditor } from './NetworkInterfacesEditor'
import { ResourceRequirementsEditor } from './ResourceRequirementsEditor'
import type { InspectorDocumentProps } from './inspectorTypes'

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
          disabled={readOnly || !!node.devicePackageId}
          label="镜像模板"
          onChange={(value) => update({ imageTemplateId: Number(value) })}
          value={String(node.imageTemplateId)}
        >
          {node.imageTemplateId <= 0 ? <option value="0">请选择可用镜像</option> : null}
          {!currentAvailable && node.imageTemplateId > 0 ? (
            <option value={node.imageTemplateId}>当前模板 #{node.imageTemplateId}（不可用）</option>
          ) : null}
          {compatibleImages.map((option) => (
            <option key={option.id} value={option.id}>
              {option.name}
              {option.remoteAccessProtocol === 'ssh'
                ? ' - 已配置 SSH 运维'
                : option.remoteAccessProtocol === 'rdp'
                  ? ' - 已配置 RDP 运维'
                  : ' - 未配置运维接入'}
            </option>
          ))}
        </SelectInput>
        {node.devicePackageId ? <p>镜像由设备模板确定；解除模板绑定后可单独更换镜像。</p> : null}
        <p>镜像与可选 SSH/RDP 运维入口在 <Link to="/admin/images">环境模板</Link> 中配置。运维入口未配置不会阻止场景发布。</p>
        {node.type !== 'docker' ? (
          <GuestNetworkModeEditor
            mode={node.vmNetworkMode}
            inheritedMode={compatibleImages.find((image) => image.id === node.imageTemplateId)?.vmNetworkMode}
            onChange={(vmNetworkMode) => update({ vmNetworkMode })}
            readOnly={readOnly}
            windows={node.type === 'windows-vm'}
          />
        ) : null}
      </InspectorSection>

      <ResourceRequirementsEditor
        onChange={(resources) => update({ resources })}
        readOnly={readOnly}
        resources={node.resources}
      />
      <NetworkInterfacesEditor
        imageOptions={imageOptions}
        document={document}
        nodeKey={node.key}
        onDocumentChange={onDocumentChange}
        readOnly={readOnly}
      />
      <details open={Boolean(node.devicePackageId || node.connectorId)}>
        <summary>高级配置{node.devicePackageId || node.connectorId ? ' · 已有设备或现场绑定' : ''}</summary>
        {node.devicePackageId || node.connectorId ? <CapabilityBindingEditor node={node} imageOptions={compatibleImages} onAssetChange={update} readOnly /> : null}
        {node.devicePackageId || node.connectorId ? <p>已有绑定保持原值。请在 <Link to="/admin/teamlab/resources">管理员维护入口</Link> 查看资源；常规设计不再创建或修改绑定。</p> : null}
        <HealthCheckEditor
          healthCheck={node.healthCheck}
          onChange={(healthCheck) => update({ healthCheck })}
          readOnly={readOnly}
        />
      </details>
    </>
  )
}
