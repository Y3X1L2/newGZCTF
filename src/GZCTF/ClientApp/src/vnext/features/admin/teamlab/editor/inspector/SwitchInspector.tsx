import { Network } from 'lucide-react'
import { updateTopologyNode } from '../../model/topologyCommands'
import type { TopologySwitchNode } from '../../model/topologyDocument'
import { AdvancedSection, InspectorSection, NumberInput, PositionEditor, TextInput, ToggleInput } from './InspectorFields'
import type { InspectorDocumentProps } from './inspectorTypes'

export function SwitchInspector({
  document,
  node,
  onDocumentChange,
  readOnly,
}: InspectorDocumentProps & { node: TopologySwitchNode }) {
  const update = (patch: Partial<TopologySwitchNode>) => {
    onDocumentChange(updateTopologyNode(document, { ...node, ...patch }).document)
  }
  return (
    <>
      <InspectorSection icon={<Network aria-hidden="true" size={16} />} title="交换机与网段">
        <TextInput disabled={readOnly} label="交换机名称" onChange={(name) => update({ name })} value={node.name} />
        <TextInput disabled={readOnly} label="网段名称" onChange={(networkName) => update({ networkName })} value={node.networkName} />
        <TextInput disabled={readOnly} label="地址池 CIDR" onChange={(poolCidr) => update({ poolCidr })} value={node.poolCidr} />
        <NumberInput disabled={readOnly} label="运行时前缀" max={32} min={1} onChange={(runtimePrefixLength) => update({ runtimePrefixLength })} value={node.runtimePrefixLength} />
        <ToggleInput checked={node.isEntry} disabled={readOnly} label="入口网段" onChange={(isEntry) => update({ isEntry })} />
      </InspectorSection>
      <AdvancedSection summary="高级配置">
        <TextInput disabled={readOnly} label="网络标识" onChange={(networkKey) => update({ networkKey })} value={node.networkKey} />
        <NumberInput disabled={readOnly} label="排序" min={0} onChange={(orderIndex) => update({ orderIndex })} value={node.orderIndex} />
        <TextInput disabled label="节点标识" value={node.key} />
        <PositionEditor onChange={(position) => update({ position })} position={node.position} readOnly={readOnly} />
      </AdvancedSection>
    </>
  )
}
