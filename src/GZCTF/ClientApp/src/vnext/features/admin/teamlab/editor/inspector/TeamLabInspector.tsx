import { Box, Cable, FileText, Layers3, Radar } from 'lucide-react'
import type { TopologyDocument } from '../../model/topologyDocument'
import type { TopologySelection } from '../../model/topologySelection'
import { AssetInspector } from './AssetInspector'
import { DependencyEditor } from './DependencyEditor'
import { InspectorSection, TextInput } from './InspectorFields'
import type { InspectorDocumentProps } from './inspectorTypes'
import type { TeamLabImageOption } from '../../api'
import { NetworkInterfacesEditor } from './NetworkInterfacesEditor'
import { ObservationEditor } from './ObservationEditor'
import { RouterInspector } from './RouterInspector'
import { SwitchInspector } from './SwitchInspector'
import styles from './TeamLabInspector.module.css'

export interface TeamLabInspectorProps extends InspectorDocumentProps {
  selection: TopologySelection
  imageOptions?: readonly TeamLabImageOption[]
}

function SelectionSummary({ document, selection }: { document: TopologyDocument; selection: TopologySelection }) {
  const nodes = [...selection.nodeKeys].map((key) => document.nodes[key]).filter((node) => node !== undefined)
  const connections = [...selection.connectionKeys]
    .map((key) => document.connections[key])
    .filter((connection) => connection !== undefined)
  return (
    <div className={styles.summaryContent}>
      <dl className={styles.summaryGrid}>
        <div><dt><Box aria-hidden="true" size={15} />节点</dt><dd>{nodes.length}</dd></div>
        <div><dt><Cable aria-hidden="true" size={15} />连接</dt><dd>{connections.length}</dd></div>
      </dl>
      <p>已选择多个对象。为避免批量覆盖异构配置，请单选后编辑属性。</p>
      <ul className={styles.selectionList}>
        {nodes.map((node) => <li key={node.key}><strong>{node.name}</strong><code>{node.key}</code></li>)}
        {connections.map((connection) => <li key={connection.key}><strong>{connection.type}</strong><code>{connection.key}</code></li>)}
      </ul>
    </div>
  )
}

export function TeamLabInspector({ document, selection, onDocumentChange, readOnly, imageOptions = [] }: TeamLabInspectorProps) {
  const nodes = [...selection.nodeKeys].map((key) => document.nodes[key]).filter((node) => node !== undefined)
  const connections = [...selection.connectionKeys]
    .map((key) => document.connections[key])
    .filter((connection) => connection !== undefined)
  const selectedCount = nodes.length + connections.length

  let content
  if (selectedCount === 0) {
    content = (
      <>
        <div className={styles.empty}><Radar aria-hidden="true" size={22} /><strong>场景观测策略</strong><span>选择节点或连接可编辑其属性</span></div>
        <InspectorSection icon={<FileText aria-hidden="true" size={16} />} title="场景">
          <TextInput
            disabled={readOnly}
            label="场景名称"
            onChange={(name) => onDocumentChange({ ...document, name })}
            value={document.name}
          />
        </InspectorSection>
        <ObservationEditor
          onChange={(observation) => onDocumentChange({ ...document, observation })}
          policy={document.observation}
          readOnly={readOnly}
        />
      </>
    )
  } else if (selectedCount > 1) {
    content = <SelectionSummary document={document} selection={selection} />
  } else if (nodes.length === 1) {
    const node = nodes[0]
    content = node.type === 'switch'
      ? <SwitchInspector document={document} node={node} onDocumentChange={onDocumentChange} readOnly={readOnly} />
      : node.type === 'router'
        ? <RouterInspector document={document} node={node} onDocumentChange={onDocumentChange} readOnly={readOnly} />
        : <AssetInspector document={document} imageOptions={imageOptions} node={node} onDocumentChange={onDocumentChange} readOnly={readOnly} />
  } else {
    const connection = connections[0]
    content = connection.type === 'membership'
      ? <NetworkInterfacesEditor connection={connection} document={document} onDocumentChange={onDocumentChange} readOnly={readOnly} />
      : connection.type === 'route'
        ? <RouterInspector connection={connection} document={document} onDocumentChange={onDocumentChange} readOnly={readOnly} />
        : <DependencyEditor connection={connection} document={document} onDocumentChange={onDocumentChange} readOnly={readOnly} />
  }

  return (
    <aside aria-label="属性检查器" className={styles.panel}>
      <header className={styles.panelHeader}>
        <span>INSPECTOR</span>
        <strong><Layers3 aria-hidden="true" size={17} />属性配置</strong>
      </header>
      <div className={styles.content}>{content}</div>
    </aside>
  )
}
