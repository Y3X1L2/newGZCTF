import { Box, Container, Monitor, MonitorCog, Settings2 } from 'lucide-react'
import { updateTopologyNode } from '../../model/topologyCommands'
import type { TopologyAssetNode } from '../../model/topologyDocument'
import type { TeamLabImageOption } from '../../api'
import { BootstrapEditor } from './BootstrapEditor'
import { HealthCheckEditor } from './HealthCheckEditor'
import {
  AdvancedSection,
  InspectorSection,
  KeyValueEditor,
  NumberInput,
  PositionEditor,
  TextAreaInput,
  TextInput,
  ToggleInput,
  SelectInput,
} from './InspectorFields'
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
      <NetworkInterfacesEditor document={document} nodeKey={node.key} onDocumentChange={onDocumentChange} readOnly={readOnly} />
      <HealthCheckEditor healthCheck={node.healthCheck} onChange={(healthCheck) => update({ healthCheck })} readOnly={readOnly} />

      <AdvancedSection summary="高级配置">
        <InspectorSection icon={<Settings2 aria-hidden="true" size={16} />} title="运行参数">
          <ToggleInput checked={node.routingEnabled} disabled={readOnly} label="启用资产路由" onChange={(routingEnabled) => update({ routingEnabled })} />
          <ToggleInput
            checked={node.exposePort !== null}
            disabled={readOnly}
            label="暴露服务端口"
            onChange={(enabled) => update({ exposePort: enabled ? 80 : null })}
          />
          {node.exposePort !== null ? (
            <NumberInput disabled={readOnly} label="服务端口" max={65535} min={1} onChange={(exposePort) => update({ exposePort })} value={node.exposePort} />
          ) : null}
          <ToggleInput
            checked={node.environment !== null}
            disabled={readOnly}
            description="这里只保存普通环境变量，敏感值由运行时安全参数提供。"
            label="环境变量"
            onChange={(enabled) => update({ environment: enabled ? {} : null })}
          />
          {node.environment !== null ? (
            <KeyValueEditor label="环境变量" onChange={(environment) => update({ environment })} readOnly={readOnly} values={node.environment} />
          ) : null}
          <TextAreaInput
            disabled={readOnly}
            hint="留空表示使用镜像默认启动命令"
            label="启动命令"
            onChange={(startCommand) => update({ startCommand: startCommand.trim() ? startCommand : null })}
            value={node.startCommand ?? ''}
          />
          <ToggleInput
            checked={node.stateless}
            description="仅用于可随时重建、无需保留本地数据的服务。"
            disabled={readOnly}
            help="stateless"
            label="无状态资产"
            onChange={(stateless) => update({ stateless })}
          />
          <ToggleInput checked={node.bakeAtPublish} disabled={readOnly} help="bakeAtPublish" label="发布时预制" onChange={(bakeAtPublish) => update({ bakeAtPublish })} />
          <TextInput
            disabled={readOnly}
            hint="可选；用于固定不可变镜像版本"
            help="imageDigest"
            label="镜像 Digest"
            onChange={(imageDigest) => update({ imageDigest: imageDigest.trim() ? imageDigest : null })}
            value={node.imageDigest ?? ''}
          />
        </InspectorSection>

        <BootstrapEditor assetType={node.type} bootstrap={node.bootstrap} onChange={(bootstrap) => update({ bootstrap })} readOnly={readOnly} />
        <ObservationEditor endpointMode={node.endpointObservation} onEndpointModeChange={(endpointObservation) => update({ endpointObservation })} readOnly={readOnly} />

        <InspectorSection icon={<Box aria-hidden="true" size={16} />} title="编辑器元数据">
          <NumberInput disabled={readOnly} label="排序" min={0} onChange={(orderIndex) => update({ orderIndex })} value={node.orderIndex} />
          <TextInput disabled label="资产标识" value={node.key} />
          <PositionEditor onChange={(position) => update({ position })} position={node.position} readOnly={readOnly} />
        </InspectorSection>
      </AdvancedSection>
    </>
  )
}
