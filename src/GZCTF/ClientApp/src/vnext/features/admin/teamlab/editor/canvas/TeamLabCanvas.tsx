import {
  applyEdgeChanges,
  applyNodeChanges,
  Background,
  BackgroundVariant,
  MarkerType,
  ReactFlow,
  ReactFlowProvider,
  useReactFlow,
  type Connection,
  type EdgeChange,
  type NodeChange,
  type OnNodeDrag,
  type OnSelectionChangeParams,
} from '@xyflow/react'
import { useCallback, useEffect, useState } from 'react'
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

function connectionCounts(document: TopologyDocument) {
  const counts = new Map<string, number>()
  for (const connection of Object.values(document.connections)) {
    if (connection.type !== 'membership') continue
    counts.set(connection.nodeKey, (counts.get(connection.nodeKey) ?? 0) + 1)
    counts.set(connection.switchKey, (counts.get(connection.switchKey) ?? 0) + 1)
  }
  return counts
}

function flowNodes(document: TopologyDocument, readOnly: boolean): TeamLabFlowNode[] {
  const counts = connectionCounts(document)
  return Object.values(document.nodes).map((node) => ({
    id: node.key,
    type: node.type,
    position: { x: node.position.x, y: node.position.y },
    selected: false,
    draggable: !readOnly,
    data: { topologyNode: node, connectionCount: counts.get(node.key) ?? 0, readOnly },
  }))
}

function reconcileNodes(current: TeamLabFlowNode[], next: TeamLabFlowNode[]) {
  const byId = new Map(current.map((node) => [node.id, node]))
  let changed = current.length !== next.length
  const reconciled = next.map((node) => {
    const previous = byId.get(node.id)
    if (
      previous &&
      previous.type === node.type &&
      previous.position.x === node.position.x &&
      previous.position.y === node.position.y &&
      previous.draggable === node.draggable &&
      previous.data.topologyNode === node.data.topologyNode &&
      previous.data.connectionCount === node.data.connectionCount &&
      previous.data.readOnly === node.data.readOnly
    )
      return previous
    changed = true
    return { ...node, selected: previous?.selected ?? false }
  })
  return changed ? reconciled : current
}

function reconcileEdges(current: TeamLabFlowEdge[], next: TeamLabFlowEdge[]) {
  const byId = new Map(current.map((edge) => [edge.id, edge]))
  let changed = current.length !== next.length
  const reconciled = next.map((edge) => {
    const previous = byId.get(edge.id)
    if (
      previous &&
      previous.source === edge.source &&
      previous.target === edge.target &&
      previous.type === edge.type &&
      previous.data?.connection === edge.data?.connection &&
      previous.data?.label === edge.data?.label &&
      Boolean(previous.markerStart) === Boolean(edge.markerStart) &&
      Boolean(previous.markerEnd) === Boolean(edge.markerEnd)
    )
      return previous
    changed = true
    return { ...edge, selected: previous?.selected ?? false }
  })
  return changed ? reconciled : current
}

function applySelection<T extends { id: string; selected?: boolean }>(items: T[], selectedIds: ReadonlySet<string>) {
  let changed = false
  const next = items.map((item) => {
    const selected = selectedIds.has(item.id)
    if (Boolean(item.selected) === selected) return item
    changed = true
    return { ...item, selected }
  })
  return changed ? next : items
}

function flowEdges(document: TopologyDocument): TeamLabFlowEdge[] {
  return Object.values(document.connections).map((connection) => {
    if (connection.type === 'membership') {
      return {
        id: connection.key,
        source: connection.nodeKey,
        target: connection.switchKey,
        type: 'network',
        selected: false,
        data: { connection, label: connection.primary ? '主网卡' : '' },
      }
    }
    if (connection.type === 'route') {
      return {
        id: connection.key,
        source: connection.fromSwitchKey,
        target: connection.toSwitchKey,
        type: 'network',
        selected: false,
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
      selected: false,
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
  const [nodes, setNodes] = useState(() =>
    applySelection(flowNodes(props.document, props.readOnly), props.selection.nodeKeys)
  )
  const [edges, setEdges] = useState(() => applySelection(flowEdges(props.document), props.selection.connectionKeys))
  const flow = useReactFlow<TeamLabFlowNode, TeamLabFlowEdge>()

  useEffect(() => {
    setNodes((current) => reconcileNodes(current, flowNodes(props.document, props.readOnly)))
    setEdges((current) => reconcileEdges(current, flowEdges(props.document)))
  }, [props.document, props.readOnly])

  useEffect(() => {
    setNodes((current) => applySelection(current, props.selection.nodeKeys))
    setEdges((current) => applySelection(current, props.selection.connectionKeys))
  }, [props.selection.connectionKeys, props.selection.nodeKeys])

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
  const onEdgesChange = useCallback(
    (changes: EdgeChange<TeamLabFlowEdge>[]) => setEdges((current) => applyEdgeChanges(changes, current)),
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
        onEdgesChange={onEdgesChange}
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
