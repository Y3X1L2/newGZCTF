import { Cable } from 'lucide-react'
import { updateTopologyConnection } from '../../model/topologyCommands'
import type { TopologyMembershipConnection } from '../../model/topologyDocument'
import { InspectorSection, NumberInput, SelectInput, TextInput, ToggleInput } from './InspectorFields'
import type { InspectorDocumentProps } from './inspectorTypes'
import styles from './TeamLabInspector.module.css'

type NetworkInterfacesEditorProps = InspectorDocumentProps &
  ({ nodeKey: string; connection?: never } | { connection: TopologyMembershipConnection; nodeKey?: never })

export function NetworkInterfacesEditor(props: NetworkInterfacesEditorProps) {
  const { document, onDocumentChange, readOnly } = props
  const memberships = props.connection
    ? [props.connection]
    : Object.values(document.connections)
        .filter((connection): connection is TopologyMembershipConnection => connection.type === 'membership')
        .filter((connection) => connection.nodeKey === props.nodeKey)
        .sort((left, right) => left.orderIndex - right.orderIndex || left.key.localeCompare(right.key))
  const switches = Object.values(document.nodes).filter((node) => node.type === 'switch')
  const attachableNodes = Object.values(document.nodes).filter((node) => node.type !== 'switch')

  const update = (connection: TopologyMembershipConnection, patch: Partial<TopologyMembershipConnection>) => {
    let next = updateTopologyConnection(document, { ...connection, ...patch }).document
    if (patch.primary === true) {
      for (const candidate of Object.values(next.connections)) {
        if (
          candidate.type === 'membership' &&
          candidate.nodeKey === connection.nodeKey &&
          candidate.key !== connection.key &&
          candidate.primary
        ) {
          next = updateTopologyConnection(next, { ...candidate, primary: false }).document
        }
      }
    }
    onDocumentChange(next)
  }

  return (
    <InspectorSection icon={<Cable aria-hidden="true" size={16} />} title="网络接口">
      {memberships.length === 0 ? <p className={styles.muted}>尚未连接到交换机。请在画布中创建连接。</p> : null}
      <div className={styles.interfaceList}>
        {memberships.map((connection, index) => (
          <div className={styles.interfaceCard} key={connection.key}>
            <header><strong>网卡 {index + 1}</strong><code>{connection.interfaceKey ?? connection.key}</code></header>
            {props.connection ? (
              <SelectInput
                disabled={readOnly}
                label="连接节点"
                onChange={(nodeKey) => update(connection, { nodeKey })}
                value={connection.nodeKey}
              >
                {attachableNodes.map((node) => <option key={node.key} value={node.key}>{node.name}</option>)}
              </SelectInput>
            ) : null}
            <SelectInput
              disabled={readOnly}
              label="所属交换机"
              onChange={(switchKey) => update(connection, { switchKey })}
              value={connection.switchKey}
            >
              {switches.map((node) => <option key={node.key} value={node.key}>{node.name} · {node.networkName}</option>)}
            </SelectInput>
            <div className={styles.twoColumns}>
              <NumberInput disabled={readOnly} label="主机偏移" min={1} onChange={(hostOffset) => update(connection, { hostOffset })} value={connection.hostOffset} />
              <NumberInput disabled={readOnly} label="排序" min={0} onChange={(orderIndex) => update(connection, { orderIndex })} value={connection.orderIndex} />
            </div>
            <ToggleInput
              checked={connection.primary}
              disabled={readOnly}
              description="主网卡承载默认网关"
              label="主网卡"
              onChange={(primary) => update(connection, { primary })}
            />
            <TextInput disabled label="接口标识" value={connection.interfaceKey ?? connection.key} />
          </div>
        ))}
      </div>
    </InspectorSection>
  )
}
