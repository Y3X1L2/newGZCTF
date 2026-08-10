import { GitBranch } from 'lucide-react'
import { updateTopologyConnection } from '../../model/topologyCommands'
import type { TopologyDependencyConnection } from '../../model/topologyDocument'
import { InspectorSection, SelectInput, TextInput } from './InspectorFields'
import type { InspectorDocumentProps } from './inspectorTypes'

export function DependencyEditor({
  document,
  connection,
  onDocumentChange,
  readOnly,
}: InspectorDocumentProps & { connection: TopologyDependencyConnection }) {
  const assets = Object.values(document.nodes).filter((node) => node.type !== 'switch' && node.type !== 'router')
  const update = (patch: Partial<TopologyDependencyConnection>) => {
    onDocumentChange(updateTopologyConnection(document, { ...connection, ...patch }).document)
  }
  return (
    <InspectorSection icon={<GitBranch aria-hidden="true" size={16} />} title="启动依赖">
      <SelectInput disabled={readOnly} label="当前资产" onChange={(assetKey) => update({ assetKey })} value={connection.assetKey}>
        {assets.map((asset) => <option key={asset.key} value={asset.key}>{asset.name}</option>)}
      </SelectInput>
      <SelectInput disabled={readOnly} label="依赖资产" onChange={(dependsOnKey) => update({ dependsOnKey })} value={connection.dependsOnKey}>
        {assets.map((asset) => <option key={asset.key} value={asset.key}>{asset.name}</option>)}
      </SelectInput>
      <SelectInput
        disabled={readOnly}
        label="就绪条件"
        onChange={(condition) => {
          if (
            condition === 'network-ready' ||
            condition === 'guest-ready' ||
            condition === 'service-ready'
          ) update({ condition })
        }}
        value={connection.condition}
      >
        <option value="network-ready">网络就绪</option>
        <option value="guest-ready">来宾系统就绪</option>
        <option value="service-ready">服务就绪</option>
      </SelectInput>
      <TextInput disabled label="连接标识" value={connection.key} />
    </InspectorSection>
  )
}
