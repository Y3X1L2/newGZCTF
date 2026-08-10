import { LayoutPanelTop, Network, RotateCcw } from 'lucide-react'
import {
  fitNetworkRegionToMembers,
  networkMembersOf,
  resizeNetworkRegion,
  setNetworkCollapsed,
  updateTopologyNode,
} from '../../model/topologyCommands'
import type { TopologySwitchNode } from '../../model/topologyDocument'
import { InspectorSection, NumberInput, TextInput, ToggleInput } from './InspectorFields'
import type { InspectorDocumentProps } from './inspectorTypes'
import styles from './TeamLabInspector.module.css'

const REGION_PADDING = 48

function derivedSize(document: InspectorDocumentProps['document'], networkKey: string) {
  const members = networkMembersOf(document, networkKey)
  if (members.length === 0) return { width: 320, height: 220 }
  let minX = Number.POSITIVE_INFINITY
  let minY = Number.POSITIVE_INFINITY
  let maxX = Number.NEGATIVE_INFINITY
  let maxY = Number.NEGATIVE_INFINITY
  for (const key of members) {
    const node = document.nodes[key]
    if (!node) continue
    minX = Math.min(minX, node.position.x)
    minY = Math.min(minY, node.position.y)
    maxX = Math.max(maxX, node.position.x + (node.position.width ?? 208))
    maxY = Math.max(maxY, node.position.y + (node.position.height ?? 102))
  }
  return { width: maxX - minX + REGION_PADDING * 2, height: maxY - minY + REGION_PADDING * 2 }
}

export function NetworkRegionInspector({
  document,
  networkKey,
  onDocumentChange,
  readOnly,
}: InspectorDocumentProps & { networkKey: string }) {
  const switchNode = Object.values(document.nodes).find(
    (node): node is TopologySwitchNode => node.type === 'switch' && node.networkKey === networkKey
  )
  if (!switchNode) return null

  const layout = document.networkLayouts[networkKey]
  const automatic = derivedSize(document, networkKey)
  const width = layout?.width ?? automatic.width
  const height = layout?.height ?? automatic.height
  const members = networkMembersOf(document, networkKey)
  const updateSwitch = (patch: Partial<TopologySwitchNode>) =>
    onDocumentChange(updateTopologyNode(document, { ...switchNode, ...patch }).document)
  const updateSize = (nextWidth: number, nextHeight: number) =>
    onDocumentChange(resizeNetworkRegion(document, networkKey, nextWidth, nextHeight).document)

  return (
    <>
      <div className={styles.regionSummary}>
        <Network aria-hidden="true" size={22} />
        <div>
          <strong>{switchNode.networkName || switchNode.name}</strong>
          <span>{members.length} 个成员资产 · {switchNode.poolCidr}</span>
        </div>
      </div>
      <InspectorSection icon={<Network aria-hidden="true" size={16} />} title="网段区域">
        <TextInput
          disabled={readOnly}
          hint="用于画布区域标题和发布前检查结果。"
          label="网段名称"
          onChange={(networkName) => updateSwitch({ networkName })}
          value={switchNode.networkName}
        />
        <TextInput
          disabled={readOnly}
          hint="网段内的交换机名称，不会改变网络地址。"
          label="交换机名称"
          onChange={(name) => updateSwitch({ name })}
          value={switchNode.name}
        />
        <TextInput
          disabled={readOnly}
          hint="平台会从该地址池为连接到此网段的资产分配地址。"
          label="地址池 CIDR"
          onChange={(poolCidr) => updateSwitch({ poolCidr })}
          value={switchNode.poolCidr}
        />
        <ToggleInput
          checked={switchNode.isEntry}
          description="作为场景入口侧的默认网络。"
          disabled={readOnly}
          label="入口网段"
          onChange={(isEntry) => updateSwitch({ isEntry })}
        />
        <ToggleInput
          checked={layout?.collapsed ?? false}
          description="折叠后画布只保留交换机，成员资产暂时隐藏。"
          disabled={readOnly}
          label="折叠区域"
          onChange={(collapsed) => onDocumentChange(setNetworkCollapsed(document, networkKey, collapsed).document)}
        />
      </InspectorSection>
      <InspectorSection icon={<LayoutPanelTop aria-hidden="true" size={16} />} title="区域布局">
        <div className={styles.twoColumns}>
          <NumberInput
            disabled={readOnly}
            label="区域宽度"
            min={304}
            onChange={(next) => updateSize(next, height)}
            value={Math.round(width)}
          />
          <NumberInput
            disabled={readOnly}
            label="区域高度"
            min={198}
            onChange={(next) => updateSize(width, next)}
            value={Math.round(height)}
          />
        </div>
        <button
          className={styles.addButton}
          disabled={readOnly}
          onClick={() => onDocumentChange(fitNetworkRegionToMembers(document, networkKey).document)}
          type="button"
        >
          <RotateCcw aria-hidden="true" size={15} />
          按成员自动适配
        </button>
      </InspectorSection>
    </>
  )
}
