import { Cable } from 'lucide-react'
import type { TeamLabImageOption } from '../../api/teamlabImageCatalog'
import { updateTopologyConnection } from '../../model/topologyCommands'
import type { TopologyMembershipConnection } from '../../model/topologyDocument'
import { GuestInterfaceRequirementsEditor } from './GuestInterfaceRequirementsEditor'
import { InspectorSection, NumberInput, SelectInput, TextInput, ToggleInput } from './InspectorFields'
import styles from './TeamLabInspector.module.css'
import type { InspectorDocumentProps } from './inspectorTypes'

type NetworkInterfacesEditorProps = InspectorDocumentProps & { imageOptions?: readonly TeamLabImageOption[] } & (
    | { nodeKey: string; connection?: never }
    | { connection: TopologyMembershipConnection; nodeKey?: never }
  )

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
        {memberships.map((connection, index) => {
          const owner = document.nodes[connection.nodeKey]
          const vm = owner?.type === 'linux-vm' || owner?.type === 'windows-vm' ? owner : null
          const mode =
            vm?.vmNetworkMode ?? props.imageOptions?.find((image) => image.id === vm?.imageTemplateId)?.vmNetworkMode
          const unsupportedDockerRequirements =
            owner?.type === 'docker' &&
            (connection.guestInterfaceName != null ||
              connection.useDefaultGateway != null ||
              connection.dnsServers != null ||
              connection.staticRoutes != null)
          return (
            <div className={styles.interfaceCard} key={connection.key}>
              <header>
                <strong>网卡 {index + 1}</strong>
                <code>{connection.interfaceKey ?? connection.key}</code>
              </header>
              {props.connection ? (
                <SelectInput
                  disabled={readOnly}
                  label="连接节点"
                  onChange={(nodeKey) => update(connection, { nodeKey })}
                  value={connection.nodeKey}
                >
                  {attachableNodes.map((node) => (
                    <option key={node.key} value={node.key}>
                      {node.name}
                    </option>
                  ))}
                </SelectInput>
              ) : null}
              <SelectInput
                disabled={readOnly}
                label="所属交换机"
                onChange={(switchKey) => update(connection, { switchKey })}
                value={connection.switchKey}
              >
                {switches.map((node) => (
                  <option key={node.key} value={node.key}>
                    {node.name} · {node.networkName}
                  </option>
                ))}
              </SelectInput>
              <div className={styles.twoColumns}>
                <NumberInput
                  disabled={readOnly}
                  help="hostOffset"
                  label="主机偏移"
                  min={1}
                  onChange={(hostOffset) => update(connection, { hostOffset })}
                  value={connection.hostOffset}
                />
                <NumberInput
                  disabled={readOnly}
                  help="interfaceOrder"
                  label="排序"
                  min={0}
                  onChange={(orderIndex) => update(connection, { orderIndex })}
                  value={connection.orderIndex}
                />
              </div>
              <ToggleInput
                checked={connection.primary}
                disabled={readOnly}
                description={vm ? '未单独设置网关时，由主网卡承载默认网关' : '主网卡承载默认网关'}
                label="主网卡"
                onChange={(primary) => update(connection, { primary })}
              />
              <TextInput disabled label="接口标识" value={connection.interfaceKey ?? connection.key} />
              {unsupportedDockerRequirements ? (
                <>
                  <p className={styles.muted}>Docker 不支持这条连接上已保存的 VM 专用网络配置。请明确移除后再保存。</p>
                  <button
                    className={styles.addButton}
                    disabled={readOnly}
                    type="button"
                    onClick={() => {
                      const cleaned = { ...connection }
                      delete cleaned.guestInterfaceName
                      delete cleaned.useDefaultGateway
                      delete cleaned.dnsServers
                      delete cleaned.staticRoutes
                      onDocumentChange(updateTopologyConnection(document, cleaned).document)
                    }}
                  >
                    移除 VM 专用网络配置
                  </button>
                </>
              ) : null}
              {vm ? (
                <GuestInterfaceRequirementsEditor
                  connection={connection}
                  onChange={(patch) => update(connection, patch)}
                  preconfigured={mode === 'preconfigured'}
                  managedStatic={mode === 'managed-static'}
                  readOnly={readOnly}
                />
              ) : null}
            </div>
          )
        })}
      </div>
    </InspectorSection>
  )
}
