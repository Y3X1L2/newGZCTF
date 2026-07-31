import type {
  TopologyConnection,
  TopologyDocument,
  TopologyNode,
} from '../../model/topologyDocument'

export interface InspectorDocumentProps {
  document: TopologyDocument
  onDocumentChange: (document: TopologyDocument) => void
  readOnly?: boolean
}

export interface NodeInspectorProps extends InspectorDocumentProps {
  node: TopologyNode
}

export interface ConnectionInspectorProps extends InspectorDocumentProps {
  connection: TopologyConnection
}
