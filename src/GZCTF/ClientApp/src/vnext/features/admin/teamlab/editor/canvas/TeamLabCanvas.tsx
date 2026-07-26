import {
  applyNodeChanges,
  Background,
  BackgroundVariant,
  MarkerType,
  ReactFlow,
  ReactFlowProvider,
  useReactFlow,
  type Connection,
  type NodeChange,
  type OnNodeDrag,
  type OnSelectionChangeParams,
} from '@xyflow/react'
import { useCallback, useEffect, useMemo, useState } from 'react'
import type { TopologyDocument, TopologyNodeType } from '../../model/topologyDocument'
import type { TopologySelection } from '../../model/topologySelection'
import { teamLabEdgeTypes } from '../edges'
import type { TeamLabFlowEdge } from '../edges/edgeTypes'
import { teamLabNodeTypes } from '../nodes'
import type { TeamLabFlowNode } from '../nodes/nodeTypes'
import { teamLabPaletteMime } from '../palette/NodePalette'
import type { CanvasConnectionMode } from '../state/canvasCommands'
import styles from './TeamLabCanvas.module.css'
import { TeamLabCanvasToolbar } from './TeamLabCanvasToolbar'
import { TeamLabMiniMap } from './TeamLabMiniMap'

const paletteTypes = new Set<TopologyNodeType>(['switch', 'router', 'docker', 'linux-vm', 'windows-vm'])

function allowPaletteDrop(event: React.DragEvent) {
  event.preventDefault()
  event.dataTransfer.dropEffect = 'copy'
}

function connectionCount(document: TopologyDocument, key: string) {
  return Object.values(document.connections).filter(
    (connection) =>
      connection.type === 'membership' && (connection.nodeKey === key || connection.switchKey === key)
  ).length
}

function flowNodes(document: TopologyDocument, selection: TopologySelection, readOnly: boolean): TeamLabFlowNode[] {
  return Object.values(document.nodes).map((node) => ({
    id: node.key,
    type: node.type,
    position: { x: node.position.x, y: node.position.y },
    selected: selection.nodeKeys.has(node.key),
    draggable: !readOnly,
    data: { topologyNode: node, connectionCount: connectionCount(document, node.key), readOnly },
  }))
}

function flowEdges(document: TopologyDocument, selection: TopologySelection): TeamLabFlowEdge[] {
  return Object.values(document.connections).map((connection) => {
    if (connection.type === 'membership') {
      return {
        id: connection.key,
        source: connection.nodeKey,
        target: connection.switchKey,
        type: 'network',
        selected: selection.connectionKeys.has(connection.key),
        data: { connection, label: connection.primary ? '主网卡' : '' },
      }
    }
    if (connection.type === 'route') {
      return {
        id: connection.key,
        source: connection.fromSwitchKey,
        target: connection.toSwitchKey,
        type: 'network',
        selected: selection.connectionKeys.has(connection.key),
        markerStart: connection.direction === 'bidirectional' ? { type: MarkerType.ArrowClosed } : undefined,
        markerEnd: { type: MarkerType.ArrowClosed },
        data: { connection, label: connection.direction === 'bidirectional' ? '双向路由' : '单向路由' },
      }
    }
    return {
      id: connection.key,
      source: connection.dependsOnKey,
      target: connection.assetKey,
      type: 'dependency',
      selected: selection.connectionKeys.has(connection.key),
      markerEnd: { type: MarkerType.ArrowClosed },
      data: { connection, label: connection.condition },
    }
  })
}

interface TeamLabCanvasProps {
  document: TopologyDocument
  selection: TopologySelection
  connectionMode: CanvasConnectionMode
  readOnly: boolean
  canUndo: boolean
  canRedo: boolean
  focusMode: boolean
  leftPanelOpen: boolean
  rightPanelOpen: boolean
  layoutRequest: number
  focusNodeKey?: string | null
  onAddNode: (type: TopologyNodeType, position: { x: number; y: number }) => void
  onAutoLayout: () => void
  onConnectNodes: (sourceKey: string, targetKey: string) => void
  onMoveNodes: (positions: ReadonlyMap<string, { x: number; y: number }>) => void
  onSelectionChange: (nodeKeys: readonly string[], connectionKeys: readonly string[]) => void
  onUndo: () => void
  onRedo: () => void
  onToggleFocus: () => void
  onToggleLeftPanel: () => void
  onToggleRightPanel: () => void
}

function TeamLabCanvasInner(props: TeamLabCanvasProps) {
  const [nodes, setNodes] = useState(() => flowNodes(props.document, props.selection, props.readOnly))
  const edges = useMemo(() => flowEdges(props.document, props.selection), [props.document, props.selection])
  const flow = useReactFlow<TeamLabFlowNode, TeamLabFlowEdge>()

  useEffect(() => {
    setNodes(flowNodes(props.document, props.selection, props.readOnly))
  }, [props.document, props.readOnly, props.selection])

  useEffect(() => {
    if (!props.focusNodeKey || !props.document.nodes[props.focusNodeKey]) return
    void flow.fitView({ nodes: [{ id: props.focusNodeKey }], duration: 260, padding: 0.6, maxZoom: 1.2 })
  }, [flow, props.document.nodes, props.focusNodeKey])

  useEffect(() => {
    if (props.layoutRequest === 0) return
    const frame = window.requestAnimationFrame(() => {
      void flow.fitView({
        padding: 0.2,
        duration: window.matchMedia('(prefers-reduced-motion: reduce)').matches ? 0 : 320,
      })
    })
    return () => window.cancelAnimationFrame(frame)
  }, [flow, props.layoutRequest])

  const onNodesChange = useCallback(
    (changes: NodeChange<TeamLabFlowNode>[]) => setNodes((current) => applyNodeChanges(changes, current)),
    []
  )
  const onConnect = useCallback(
    (connection: Connection) => {
      if (connection.source && connection.target) props.onConnectNodes(connection.source, connection.target)
    },
    [props.onConnectNodes]
  )
  const onSelectionChange = useCallback(
    ({ nodes: selectedNodes, edges: selectedEdges }: OnSelectionChangeParams<TeamLabFlowNode, TeamLabFlowEdge>) =>
      props.onSelectionChange(
        selectedNodes.map((node) => node.id),
        selectedEdges.map((edge) => edge.id)
      ),
    [props.onSelectionChange]
  )
  const onNodeDragStop = useCallback<OnNodeDrag<TeamLabFlowNode>>(
    (_event, _node, draggedNodes) => {
      props.onMoveNodes(new Map(draggedNodes.map((node) => [node.id, node.position])))
    },
    [props.onMoveNodes]
  )
  const onDrop = useCallback(
    (event: React.DragEvent) => {
      event.preventDefault()
      const type = event.dataTransfer.getData(teamLabPaletteMime) as TopologyNodeType
      if (!paletteTypes.has(type) || props.readOnly) return
      props.onAddNode(type, flow.screenToFlowPosition({ x: event.clientX, y: event.clientY }))
    },
    [flow, props.onAddNode, props.readOnly]
  )

  return (
    <div className={styles.canvas} data-connection-mode={props.connectionMode}>
      <ReactFlow<TeamLabFlowNode, TeamLabFlowEdge>
        deleteKeyCode={null}
        edges={edges}
        edgeTypes={teamLabEdgeTypes}
        fitView
        fitViewOptions={{ padding: 0.18 }}
        minZoom={0.2}
        multiSelectionKeyCode={['Meta', 'Control', 'Shift']}
        nodes={nodes}
        nodesConnectable={!props.readOnly}
        nodesDraggable={!props.readOnly}
        nodeTypes={teamLabNodeTypes}
        onConnect={onConnect}
        onDragOver={allowPaletteDrop}
        onDrop={onDrop}
        onNodeDragStop={onNodeDragStop}
        onNodesChange={onNodesChange}
        onSelectionChange={onSelectionChange}
        onlyRenderVisibleElements
        panActivationKeyCode="Space"
        panOnDrag
        selectionKeyCode="Shift"
        selectionOnDrag={false}
        zoomOnPinch
        zoomOnScroll
      >
        <Background color="var(--yn-color-grid)" gap={20} size={1.4} variant={BackgroundVariant.Dots} />
        <TeamLabCanvasToolbar
          canRedo={props.canRedo}
          canUndo={props.canUndo}
          focusMode={props.focusMode}
          leftPanelOpen={props.leftPanelOpen}
          onAutoLayout={props.onAutoLayout}
          onRedo={props.onRedo}
          onToggleFocus={props.onToggleFocus}
          onToggleLeftPanel={props.onToggleLeftPanel}
          onToggleRightPanel={props.onToggleRightPanel}
          onUndo={props.onUndo}
          readOnly={props.readOnly}
          rightPanelOpen={props.rightPanelOpen}
        />
        <TeamLabMiniMap />
      </ReactFlow>
      {nodes.length === 0 ? (
        <div className={styles.emptyState}>
          <strong>从交换机开始构建场景</strong>
          <span>当前场景还没有设备节点。</span>
        </div>
      ) : null}
    </div>
  )
}

export function TeamLabCanvas(props: TeamLabCanvasProps) {
  return (
    <ReactFlowProvider>
      <TeamLabCanvasInner {...props} />
    </ReactFlowProvider>
  )
}
