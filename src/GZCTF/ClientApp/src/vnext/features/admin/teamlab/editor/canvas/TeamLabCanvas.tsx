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
  type NodeMouseHandler,
  type OnNodeDrag,
} from '@xyflow/react'
import { useCallback, useEffect, useRef, useState } from 'react'
import type { TopologyDocument, TopologyNodeType, TopologySwitchNode } from '../../model/topologyDocument'
import type { TopologySelection } from '../../model/topologySelection'
import { teamLabEdgeTypes } from '../edges'
import type { TeamLabFlowEdge } from '../edges/edgeTypes'
import { teamLabNodeTypes } from '../nodes'
import type { TeamLabFlowNode } from '../nodes/nodeTypes'
import { networkRegionNodeId, type TeamLabRegionFlowNode } from '../regions/NetworkRegionNode'
import { teamLabPaletteMime } from '../palette/NodePalette'
import type { CanvasConnectionMode } from '../state/canvasCommands'
import styles from './TeamLabCanvas.module.css'
import { TeamLabCanvasToolbar } from './TeamLabCanvasToolbar'
import { TeamLabMiniMap } from './TeamLabMiniMap'

const paletteTypes = new Set<TopologyNodeType>(['switch', 'router', 'docker', 'linux-vm', 'windows-vm'])

type FlowNode = TeamLabFlowNode | TeamLabRegionFlowNode

const REGION_PADDING = 48

interface RegionDragSnapshot {
  regionPosition: { x: number; y: number }
  memberPositions: ReadonlyMap<string, { x: number; y: number }>
}

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

function switchOf(document: TopologyDocument, key: string): TopologySwitchNode | undefined {
  const value = document.nodes[key]
  return value?.type === 'switch' ? value : undefined
}

/** Maps every network key to the nodes visually owned by its region (switch + asset members). */
function networkMemberKeys(document: TopologyDocument): ReadonlyMap<string, readonly string[]> {
  const members = new Map<string, string[]>()
  for (const node of Object.values(document.nodes)) {
    if (node.type === 'switch') {
      const current = members.get(node.networkKey) ?? []
      current.push(node.key)
      members.set(node.networkKey, current)
    }
  }
  // Only assets whose memberships all point into a single network belong to its
  // region; routers and dual-homed devices stay border nodes.
  const assetNetworks = new Map<string, Set<string>>()
  for (const connection of Object.values(document.connections)) {
    if (connection.type !== 'membership') continue
    const owner = switchOf(document, connection.switchKey)
    if (!owner) continue
    const networks = assetNetworks.get(connection.nodeKey) ?? new Set<string>()
    networks.add(owner.networkKey)
    assetNetworks.set(connection.nodeKey, networks)
  }
  for (const [nodeKey, networks] of assetNetworks) {
    if (networks.size !== 1) continue
    const networkKey = [...networks][0]
    const current = members.get(networkKey) ?? []
    if (!current.includes(nodeKey)) current.push(nodeKey)
    members.set(networkKey, current)
  }
  return members
}

function nodeSize(document: TopologyDocument, key: string) {
  const node = document.nodes[key]
  return { width: node?.position.width ?? 208, height: node?.position.height ?? 102 }
}

/** Bounding box (in flow coordinates) of the member nodes of a network region. */
function regionBounds(document: TopologyDocument, memberKeys: readonly string[]) {
  let minX = Number.POSITIVE_INFINITY
  let minY = Number.POSITIVE_INFINITY
  let maxX = Number.NEGATIVE_INFINITY
  let maxY = Number.NEGATIVE_INFINITY
  for (const key of memberKeys) {
    const node = document.nodes[key]
    if (!node) continue
    const size = nodeSize(document, key)
    minX = Math.min(minX, node.position.x)
    minY = Math.min(minY, node.position.y)
    maxX = Math.max(maxX, node.position.x + size.width)
    maxY = Math.max(maxY, node.position.y + size.height)
  }
  if (memberKeys.length === 0) return { x: 0, y: 0, width: 320, height: 220 }
  return { x: minX, y: minY, width: maxX - minX, height: maxY - minY }
}

interface RegionNodeCallbacks {
  onToggleRegion: (networkKey: string, collapsed: boolean) => void
  onResizeRegion: (networkKey: string, width: number, height: number) => void
  selectedNetworkKey: string | null
}

function flowNodes(document: TopologyDocument, readOnly: boolean, callbacks: RegionNodeCallbacks): FlowNode[] {
  const counts = connectionCounts(document)
  const members = networkMemberKeys(document)
  const regionNodes: TeamLabRegionFlowNode[] = []
  const memberIds = new Set<string>()
  for (const [networkKey, memberKeys] of members) {
    const layout = document.networkLayouts[networkKey]
    const bounds = regionBounds(document, memberKeys)
    const width = layout?.width ?? bounds.width + REGION_PADDING * 2
    const height = layout?.height ?? bounds.height + REGION_PADDING * 2
    const position = layout
      ? { x: layout.x, y: layout.y }
      : { x: bounds.x - REGION_PADDING, y: bounds.y - REGION_PADDING }
    for (const key of memberKeys) memberIds.add(key)
    const switchKey = memberKeys.find((key) => document.nodes[key]?.type === 'switch') ?? ''
    const network = switchOf(document, switchKey)
    regionNodes.push({
      id: networkRegionNodeId(networkKey),
      type: 'region',
      position,
      width,
      height,
      selected: false,
      draggable: !readOnly,
      selectable: false,
      // Keep regions above interactive edge hit areas. Asset nodes are rendered
      // later in the same layer, so they remain the foremost clickable objects.
      zIndex: 0,
      data: {
        networkKey,
        switchKey,
        name: network?.networkName ?? network?.name ?? networkKey,
        addressPool: network
          ? `地址池：${network.poolCidr} · 运行网段：/${network.runtimePrefixLength}`
          : networkKey,
        isEntry: network?.isEntry ?? false,
        memberKeys,
        readOnly,
        active: callbacks.selectedNetworkKey === networkKey,
        collapsed: layout?.collapsed ?? false,
        onCollapseToggle: () => callbacks.onToggleRegion(networkKey, !(layout?.collapsed ?? false)),
        onResizeEnd: (size) => callbacks.onResizeRegion(networkKey, Math.round(size.width), Math.round(size.height)),
      },
    })
  }
  const nodeList = Object.values(document.nodes).map((node) => ({
    id: node.key,
    type: node.type,
    position: { x: node.position.x, y: node.position.y },
    selected: false,
    draggable: !readOnly,
    hidden:
      node.type !== 'switch' &&
      memberIds.has(node.key) &&
      (document.networkLayouts[membersOwner(members, node.key)]?.collapsed ?? false),
    data: { topologyNode: node, connectionCount: counts.get(node.key) ?? 0, readOnly },
  }))
  return [...regionNodes, ...nodeList]
}

function membersOwner(members: ReadonlyMap<string, readonly string[]>, nodeKey: string): string {
  for (const [networkKey, keys] of members) if (keys.includes(nodeKey)) return networkKey
  return ''
}

function reconcileNodes(current: FlowNode[], next: FlowNode[]) {
  const byId = new Map(current.map((node) => [node.id, node]))
  let changed = current.length !== next.length
  const reconciled = next.map((node) => {
    const previous = byId.get(node.id)
    if (node.type === 'region') {
      // Region data carries callback closures bound to the render-time document;
      // keeping a stale region node would silently revert unrelated edits when the
      // collapse/resize callbacks fire. Always rebuild region data, keep selection.
      changed = true
      return { ...node, selected: previous?.selected ?? false }
    }
    if (
      previous &&
      previous.type === node.type &&
      previous.position.x === node.position.x &&
      previous.position.y === node.position.y &&
      previous.width === node.width &&
      previous.height === node.height &&
      previous.hidden === node.hidden &&
      previous.zIndex === node.zIndex &&
      previous.draggable === node.draggable &&
      previous.data === node.data
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

function selectedDomainKeys(nodes: readonly FlowNode[], edges: readonly TeamLabFlowEdge[]) {
  return {
    nodeKeys: nodes.filter((node) => node.selected && !node.id.startsWith('region:')).map((node) => node.id),
    connectionKeys: edges.filter((edge) => edge.selected).map((edge) => edge.id),
  }
}

function selectionSignature(nodeKeys: readonly string[], connectionKeys: readonly string[]) {
  return `${nodeKeys.toSorted().join(',')}|${connectionKeys.toSorted().join(',')}`
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
  selectedNetworkKey: string | null
  onAddNode: (type: TopologyNodeType, position: { x: number; y: number }) => void
  onAutoLayout: () => void
  onConnectNodes: (sourceKey: string, targetKey: string) => void
  onMoveNodes: (positions: ReadonlyMap<string, { x: number; y: number }>) => void
  onMoveRegion: (networkKey: string, delta: { x: number; y: number }) => void
  onToggleRegion: (networkKey: string, collapsed: boolean) => void
  onResizeRegion: (networkKey: string, width: number, height: number) => void
  onSelectionChange: (nodeKeys: readonly string[], connectionKeys: readonly string[]) => void
  onNetworkRegionSelect: (networkKey: string | null) => void
  onUndo: () => void
  onRedo: () => void
  onToggleFocus: () => void
  onToggleLeftPanel: () => void
  onToggleRightPanel: () => void
}

function TeamLabCanvasInner(props: TeamLabCanvasProps) {
  const [selectionToolActive, setSelectionToolActive] = useState(false)
  const regionDragSnapshots = useRef(new Map<string, RegionDragSnapshot>())
  const [nodes, setNodes] = useState(() =>
    applySelection(
      flowNodes(props.document, props.readOnly, {
        onToggleRegion: props.onToggleRegion,
        onResizeRegion: props.onResizeRegion,
        selectedNetworkKey: props.selectedNetworkKey,
      }),
      props.selection.nodeKeys
    )
  )
  const [edges, setEdges] = useState(() => applySelection(flowEdges(props.document), props.selection.connectionKeys))
  const lastProjectedSelection = useRef('|')
  const flow = useReactFlow<FlowNode, TeamLabFlowEdge>()

  useEffect(() => {
    setNodes((current) =>
      reconcileNodes(
        current,
        flowNodes(props.document, props.readOnly, {
          onToggleRegion: props.onToggleRegion,
          onResizeRegion: props.onResizeRegion,
          selectedNetworkKey: props.selectedNetworkKey,
        })
      )
    )
    setEdges((current) => reconcileEdges(current, flowEdges(props.document)))
  }, [props.document, props.readOnly, props.onResizeRegion, props.onToggleRegion, props.selectedNetworkKey])

  useEffect(() => {
    setNodes((current) => applySelection(current, props.selection.nodeKeys))
    setEdges((current) => applySelection(current, props.selection.connectionKeys))
  }, [props.selection.connectionKeys, props.selection.nodeKeys])

  useEffect(() => {
    const { nodeKeys, connectionKeys } = selectedDomainKeys(nodes, edges)
    const nextSignature = selectionSignature(nodeKeys, connectionKeys)
    if (nextSignature === lastProjectedSelection.current) return
    lastProjectedSelection.current = nextSignature
    if (nodeKeys.length > 0 || connectionKeys.length > 0) props.onNetworkRegionSelect(null)
    props.onSelectionChange(nodeKeys, connectionKeys)
  }, [edges, nodes, props.onNetworkRegionSelect, props.onSelectionChange])

  useEffect(() => {
    if (!props.focusNodeKey || !props.document.nodes[props.focusNodeKey]) return
    void flow.fitView({ nodes: [{ id: props.focusNodeKey }], duration: 260, padding: 0.6, maxZoom: 1.2 })
  }, [flow, props.document.nodes, props.focusNodeKey])

  useEffect(() => {
    if (props.layoutRequest === 0) return
    let secondFrame: number | null = null
    const firstFrame = window.requestAnimationFrame(() => {
      secondFrame = window.requestAnimationFrame(() => {
        void flow.fitView({
          padding: 0.2,
          duration: window.matchMedia('(prefers-reduced-motion: reduce)').matches ? 0 : 320,
        })
      })
    })
    return () => {
      window.cancelAnimationFrame(firstFrame)
      if (secondFrame !== null) window.cancelAnimationFrame(secondFrame)
    }
  }, [flow, props.layoutRequest])

  useEffect(() => {
    const firstFrame = window.requestAnimationFrame(() => {
      const secondFrame = window.requestAnimationFrame(() => {
        void flow.fitView({
          padding: 0.2,
          duration: window.matchMedia('(prefers-reduced-motion: reduce)').matches ? 0 : 180,
        })
      })
      return () => window.cancelAnimationFrame(secondFrame)
    })
    return () => window.cancelAnimationFrame(firstFrame)
  }, [flow, props.focusMode, props.leftPanelOpen, props.rightPanelOpen])

  const onNodesChange = useCallback(
    (changes: NodeChange<FlowNode>[]) => {
      // Region containers are not topology items, but their selection changes must
      // still be acknowledged in React Flow's controlled node state. Dropping them
      // leaves React Flow's internal selection different from `nodes`, which causes
      // marquee selection to continuously attempt the same synchronization.
      if (changes.length) setNodes((current) => applyNodeChanges(changes, current))
    },
    []
  )
  const onEdgesChange = useCallback(
    (changes: EdgeChange<TeamLabFlowEdge>[]) => setEdges((current) => applyEdgeChanges(changes, current)),
    []
  )
  const onConnect = useCallback(
    (connection: Connection) => {
      if (!connection.source || !connection.target) return
      if (connection.source.startsWith('region:') || connection.target.startsWith('region:')) return
      props.onConnectNodes(connection.source, connection.target)
    },
    [props.onConnectNodes]
  )
  const onNodeClick = useCallback<NodeMouseHandler<FlowNode>>((_event, node) => {
    if (node.type !== 'region') return
    props.onNetworkRegionSelect(node.data.networkKey)
  }, [props.onNetworkRegionSelect])
  const onPaneClick = useCallback(() => props.onNetworkRegionSelect(null), [props.onNetworkRegionSelect])
  const onNodeDragStart = useCallback<OnNodeDrag<FlowNode>>(
    (_event, node) => {
      if (node.type !== 'region') return
      const memberKeys = networkMemberKeys(props.document).get(node.data.networkKey) ?? []
      regionDragSnapshots.current.set(node.id, {
        regionPosition: { ...node.position },
        memberPositions: new Map(
          memberKeys.flatMap((key) => {
            const member = props.document.nodes[key]
            return member ? [[key, { x: member.position.x, y: member.position.y }] as const] : []
          })
        ),
      })
    },
    [props.document]
  )
  const onNodeDrag = useCallback<OnNodeDrag<FlowNode>>(
    (_event, node) => {
      if (node.type !== 'region') return
      const snapshot = regionDragSnapshots.current.get(node.id)
      if (!snapshot) return
      const delta = {
        x: node.position.x - snapshot.regionPosition.x,
        y: node.position.y - snapshot.regionPosition.y,
      }
      setNodes((current) =>
        current.map((candidate) => {
          const origin = snapshot.memberPositions.get(candidate.id)
          return origin ? { ...candidate, position: { x: origin.x + delta.x, y: origin.y + delta.y } } : candidate
        })
      )
    },
    []
  )
  const onNodeDragStop = useCallback<OnNodeDrag<FlowNode>>(
    (_event, _node, draggedNodes) => {
      const positions = new Map<string, { x: number; y: number }>()
      const memberKeys = networkMemberKeys(props.document)
      for (const node of draggedNodes) {
        if (node.type === 'region') {
          const networkKey = node.data.networkKey
          const layout = props.document.networkLayouts[networkKey]
          const bounds = regionBounds(props.document, memberKeys.get(networkKey) ?? [])
          const startX = layout?.x ?? bounds.x - REGION_PADDING
          const startY = layout?.y ?? bounds.y - REGION_PADDING
          const delta = { x: node.position.x - startX, y: node.position.y - startY }
          if (delta.x === 0 && delta.y === 0) continue
          // moveNetworkRegion moves the layout origin AND every member in one
          // commit; do not emit a second onMoveNodes for the members here.
          props.onMoveRegion(networkKey, delta)
          regionDragSnapshots.current.delete(node.id)
          continue
        }
        positions.set(node.id, node.position)
      }
      if (positions.size > 0) props.onMoveNodes(positions)
    },
    [props.document, props.onMoveNodes, props.onMoveRegion]
  )
  const onNodeDoubleClick = useCallback<NodeMouseHandler<FlowNode>>((_event, node) => {
      if (node.type !== 'region') return
      const networkKey = node.data.networkKey
      const memberKeys = networkMemberKeys(props.document).get(networkKey) ?? []
      if (memberKeys.length === 0) return
      void flow.fitView({
        nodes: memberKeys.map((key) => ({ id: key })),
        padding: 0.4,
        duration: window.matchMedia('(prefers-reduced-motion: reduce)').matches ? 0 : 260,
        maxZoom: 1.2,
      })
    },
    [flow, props.document]
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
      <ReactFlow<FlowNode, TeamLabFlowEdge>
        deleteKeyCode={null}
        edges={edges}
        edgeTypes={teamLabEdgeTypes}
        fitView
        fitViewOptions={{ padding: 0.18 }}
        minZoom={0.1}
        multiSelectionKeyCode={['Meta', 'Control', 'Shift']}
        nodes={nodes}
        nodesConnectable={!props.readOnly}
        nodesDraggable={!props.readOnly}
        nodeTypes={teamLabNodeTypes}
        onConnect={onConnect}
        onDragOver={allowPaletteDrop}
        onDrop={onDrop}
        onEdgesChange={onEdgesChange}
        onNodeDoubleClick={onNodeDoubleClick}
        onNodeClick={onNodeClick}
        onNodeDrag={onNodeDrag}
        onNodeDragStart={onNodeDragStart}
        onNodeDragStop={onNodeDragStop}
        onNodesChange={onNodesChange}
        onPaneClick={onPaneClick}
        onlyRenderVisibleElements
        panActivationKeyCode="Space"
        panOnDrag={selectionToolActive ? [1] : [0, 1]}
        selectionKeyCode={['Meta', 'Control']}
        selectionOnDrag={selectionToolActive}
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
          onToggleSelectionTool={() => setSelectionToolActive((active) => !active)}
          onUndo={props.onUndo}
          readOnly={props.readOnly}
          rightPanelOpen={props.rightPanelOpen}
          selectionToolActive={selectionToolActive}
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
