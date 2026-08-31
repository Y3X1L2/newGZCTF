import { Network } from 'lucide-react'
import { updateTopologyNode } from '../../model/topologyCommands'
import type { TopologySwitchNode } from '../../model/topologyDocument'
import { InspectorSection, NumberInput, TextInput, ToggleInput } from './InspectorFields'
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
        <ToggleInput
          checked={node.position.collapsed}
          disabled={readOnly}
          label="折叠此网段"
          onChange={(collapsed) => update({ position: { ...node.position, collapsed } })}
        />
      </InspectorSection>
    </>
  )
}
