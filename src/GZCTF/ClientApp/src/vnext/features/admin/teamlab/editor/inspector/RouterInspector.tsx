import { Route } from 'lucide-react'
import { updateTopologyConnection, updateTopologyNode } from '../../model/topologyCommands'
import type { TopologyRouteConnection, TopologyRouterNode } from '../../model/topologyDocument'
import { AdvancedSection, InspectorSection, PositionEditor, SelectInput, TextInput } from './InspectorFields'
import type { InspectorDocumentProps } from './inspectorTypes'
import { NetworkInterfacesEditor } from './NetworkInterfacesEditor'

type RouterInspectorProps = InspectorDocumentProps &
  ({ node: TopologyRouterNode; connection?: never } | { connection: TopologyRouteConnection; node?: never })

export function RouterInspector(props: RouterInspectorProps) {
  const { document, onDocumentChange, readOnly } = props
  if (props.node) {
    const node = props.node
    const update = (patch: Partial<TopologyRouterNode>) => {
      onDocumentChange(updateTopologyNode(document, { ...node, ...patch }).document)
    }
    return (
      <>
        <InspectorSection icon={<Route aria-hidden="true" size={16} />} title="路由器">
          <TextInput disabled={readOnly} label="名称" onChange={(name) => update({ name })} value={node.name} />
        </InspectorSection>
        <NetworkInterfacesEditor document={document} nodeKey={node.key} onDocumentChange={onDocumentChange} readOnly={readOnly} />
        <AdvancedSection summary="高级配置">
          <TextInput disabled label="节点标识" value={node.key} />
          <PositionEditor onChange={(position) => update({ position })} position={node.position} readOnly={readOnly} />
        </AdvancedSection>
      </>
    )
  }

  const connection = props.connection
  const switches = Object.values(document.nodes).filter((node) => node.type === 'switch')
  const routingNodes = Object.values(document.nodes).filter(
    (node) => node.type === 'router' || (node.type !== 'switch' && node.routingEnabled)
  )
  const update = (patch: Partial<TopologyRouteConnection>) => {
    onDocumentChange(updateTopologyConnection(document, { ...connection, ...patch }).document)
  }
  return (
    <InspectorSection icon={<Route aria-hidden="true" size={16} />} title="路由关系">
      <SelectInput disabled={readOnly} label="起始网段" onChange={(fromSwitchKey) => update({ fromSwitchKey })} value={connection.fromSwitchKey}>
        {switches.map((node) => <option key={node.key} value={node.key}>{node.networkName}</option>)}
      </SelectInput>
      <SelectInput disabled={readOnly} label="目标网段" onChange={(toSwitchKey) => update({ toSwitchKey })} value={connection.toSwitchKey}>
        {switches.map((node) => <option key={node.key} value={node.key}>{node.networkName}</option>)}
      </SelectInput>
      <SelectInput disabled={readOnly} label="路由节点" onChange={(viaNodeKey) => update({ viaNodeKey })} value={connection.viaNodeKey}>
        {routingNodes.map((node) => <option key={node.key} value={node.key}>{node.name}</option>)}
      </SelectInput>
      <SelectInput
        disabled={readOnly}
        label="方向"
        onChange={(direction) => update({ direction: direction === 'bidirectional' ? 'bidirectional' : 'from-to' })}
        value={connection.direction}
      >
        <option value="from-to">单向</option>
        <option value="bidirectional">双向</option>
      </SelectInput>
      <TextInput disabled label="连接标识" value={connection.key} />
    </InspectorSection>
  )
}
