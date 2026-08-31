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
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import type { TopologyDocument, TopologyNodeType, TopologySwitchNode } from '../../model/topologyDocument'
import {
  MIN_REGION_HEIGHT,
  REGION_HEADER_HEIGHT,
  REGION_PADDING_X,
  nodeSize,
  nodeSizeOf,
  regionSizeForMembers,
  resolveRegionSize,
} from '../../model/topologyGeometry'
import type { TopologySelection } from '../../model/topologySelection'
import { teamLabEdgeTypes } from '../edges'
import type { TeamLabFlowEdge } from '../edges/edgeTypes'
import { buildTopologyGraph, type TopologyGraph } from '../layout/topologyGraph'
import { teamLabNodeTypes } from '../nodes'
import type { TeamLabFlowNode } from '../nodes/nodeTypes'
import {
  networkRegionNodeId,
  type TeamLabRegionActions,
  type TeamLabRegionFlowNode,
} from '../regions/NetworkRegionNode'
import { teamLabPaletteMime } from '../palette/NodePalette'
import type { CanvasConnectionMode } from '../state/canvasCommands'
import styles from './TeamLabCanvas.module.css'
import { TeamLabCanvasToolbar } from './TeamLabCanvasToolbar'
import { TeamLabMiniMap } from './TeamLabMiniMap'
import { edgeLayer, nodeLayer, regionLayer } from './topologyLayers'
import { detailLevelClass, useCanvasDetailLevel } from './useCanvasDetailLevel'

const paletteTypes = new Set<TopologyNodeType>(['switch', 'router', 'docker', 'linux-vm', 'windows-vm'])

type FlowNode = TeamLabFlowNode | TeamLabRegionFlowNode

interface RegionDragSnapshot {
  regionPosition: { x: number; y: number }
  memberPositions: ReadonlyMap<string, { x: number; y: number }>
}

function allowPaletteDrop(event: React.DragEvent) {
  event.preventDefault()
  event.dataTransfer.dropEffect = 'copy'
}

function switchOf(document: TopologyDocument, key: string): TopologySwitchNode | undefined {
  const value = document.nodes[key]
  return value?.type === 'switch' ? value : undefined
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
    const size = nodeSize(node)
    minX = Math.min(minX, node.position.x)
    minY = Math.min(minY, node.position.y)
    maxX = Math.max(maxX, node.position.x + size.width)
    maxY = Math.max(maxY, node.position.y + size.height)
  }
  if (memberKeys.length === 0) {
    const empty = regionSizeForMembers([])
    return { x: 0, y: 0, width: empty.width, height: empty.height }
  }
  return { x: minX, y: minY, width: maxX - minX, height: maxY - minY }
}

interface RegionNodeCallbacks {
  actions: TeamLabRegionActions
  selectedNetworkKey: string | null
}

function flowNodes(
  document: TopologyDocument,
  graph: TopologyGraph,
  readOnly: boolean,
  callbacks: RegionNodeCallbacks
): FlowNode[] {
  const regionNodes: TeamLabRegionFlowNode[] = []
  for (const [networkKey, memberKeys] of graph.membersByNetwork) {
    const layout = document.networkLayouts[networkKey]
    const bounds = regionBounds(document, memberKeys)
    const assetHeights = memberKeys
      .filter((key) => document.nodes[key]?.type !== 'switch')
      .map((key) => nodeSizeOf(document, key).height)
    const size = resolveRegionSize(layout, assetHeights)
    const position = layout
      ? { x: layout.x, y: layout.y }
      : { x: bounds.x - REGION_PADDING_X, y: bounds.y - REGION_HEADER_HEIGHT }
    const switchKey = graph.switchByNetwork.get(networkKey) ?? ''
    const network = switchOf(document, switchKey)
    const collapsed = layout?.collapsed ?? false
    regionNodes.push({
      id: networkRegionNodeId(networkKey),
      type: 'region',
      position,
      width: size.width,
      height: collapsed ? MIN_REGION_HEIGHT : size.height,
      selected: false,
      draggable: !readOnly,
      selectable: false,
      // Explicit layer: regions are the backdrop every link and device draws
      // over. See `topologyLayers` for the full contract.
      zIndex: regionLayer(),
      // `actions` is one stable object owned by the canvas, so region data has no
      // per-render closures and an untouched region keeps its rendered element.
      data: {
        networkKey,
        switchKey,
        name: network?.networkName ?? network?.name ?? networkKey,
        addressPool: network ? `${network.poolCidr} · 运行 /${network.runtimePrefixLength}` : networkKey,
        isEntry: network?.isEntry ?? false,
        memberCount: memberKeys.filter((key) => key !== switchKey).length,
        readOnly,
        active: callbacks.selectedNetworkKey === networkKey,
        collapsed,
        actions: callbacks.actions,
      },
    })
  }

  const nodeList = Object.values(document.nodes).map((node) => {
    const ownerNetwork = graph.ownerNetworkOfNode.get(node.key)
    // 显式节点几何，避免自动排版重建节点时 React Flow 测量延迟导致边端点
    // 按未测量为 0 计算成 -1000+ 竖直线（headless 真实场景 A/B 锁定）。
    const size = nodeSize(node)
    return {
      id: node.key,
      type: node.type,
      position: { x: node.position.x, y: node.position.y },
      width: size.width,
      height: size.height,
      selected: false,
      draggable: !readOnly,
      zIndex: nodeLayer(false),
      hidden:
        node.type !== 'switch' &&
        ownerNetwork !== undefined &&
        (document.networkLayouts[ownerNetwork]?.collapsed ?? false),
      data: {
        topologyNode: node,
        connectionCount: graph.membershipCounts.get(node.key) ?? 0,
        readOnly,
        isBorder: graph.borderNodeKeys.has(node.key),
      },
    }
  })
  return [...regionNodes, ...nodeList]
}

/** Structural fields of a region's data; the callbacks are compared separately. */
const sameRegionData = (left: TeamLabRegionFlowNode['data'], right: TeamLabRegionFlowNode['data']) =>
  left.networkKey === right.networkKey &&
  left.switchKey === right.switchKey &&
  left.name === right.name &&
  left.addressPool === right.addressPool &&
  left.isEntry === right.isEntry &&
  left.memberCount === right.memberCount &&
  left.readOnly === right.readOnly &&
  left.active === right.active &&
  left.collapsed === right.collapsed &&
  left.actions === right.actions

function reconcileNodes(current: FlowNode[], next: FlowNode[]) {
  const byId = new Map(current.map((node) => [node.id, node]))
  let changed = current.length !== next.length
  const reconciled = next.map((node) => {
    const previous = byId.get(node.id)
    if (!previous || previous.type !== node.type) {
      changed = true
      return { ...node, selected: previous?.selected ?? false }
    }
    if (node.type === 'region' && previous.type === 'region') {
      // Region data carries callbacks bound to the render-time document, so the
      // object identity always differs. Comparing the *structural* fields lets an
      // unchanged region keep its element (no full re-render of every region on
      // each drag frame) while the fresh callbacks still replace the stale ones.
      if (
        previous.position.x === node.position.x &&
        previous.position.y === node.position.y &&
        previous.width === node.width &&
        previous.height === node.height &&
        previous.draggable === node.draggable &&
        sameRegionData(previous.data, node.data)
      )
        return previous
      changed = true
      return { ...node, selected: previous.selected ?? false }
    }
    if (
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
    return { ...node, selected: previous.selected ?? false }
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

/**
 * Applies selection and the layer that selection implies. Under
 * `zIndexMode="manual"` React Flow no longer elevates a selected object for us,
 * so the projection owns that: a selected node must rise above its neighbours
 * without ever dropping below the link layer.
 */
function applySelection<T extends { id: string; type?: string; selected?: boolean; zIndex?: number }>(
  items: T[],
  selectedIds: ReadonlySet<string>
) {
  let changed = false
  const next = items.map((item) => {
    const selected = selectedIds.has(item.id)
    const zIndex = item.type === 'region' ? regionLayer() : nodeLayer(selected)
    if (Boolean(item.selected) === selected && item.zIndex === zIndex) return item
    changed = true
    return { ...item, selected, zIndex }
  })
  return changed ? next : items
}

/** Selection + layer for links, kept separate so edges never enter the node band. */
function applyEdgeSelection(items: TeamLabFlowEdge[], selectedIds: ReadonlySet<string>) {
  let changed = false
  const next = items.map((item) => {
    const selected = selectedIds.has(item.id)
    const zIndex = edgeLayer(selected)
    if (Boolean(item.selected) === selected && item.zIndex === zIndex) return item
    changed = true
    return { ...item, selected, zIndex }
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
        zIndex: edgeLayer(false),
        // A membership link inside one region needs no label: it would repeat the
        // region title on every device. Only the primary NIC is worth marking.
        data: { connection, label: connection.primary ? '主网卡' : '', tone: 'membership' },
      }
    }
    if (connection.type === 'route') {
      return {
        id: connection.key,
        source: connection.fromSwitchKey,
        target: connection.toSwitchKey,
        type: 'network',
        selected: false,
        zIndex: edgeLayer(false),
        markerStart: connection.direction === 'bidirectional' ? { type: MarkerType.ArrowClosed } : undefined,
        markerEnd: { type: MarkerType.ArrowClosed },
        data: { connection, label: connection.direction === 'bidirectional' ? '双向路由' : '单向路由', tone: 'route' },
      }
    }
    return {
      id: connection.key,
      source: connection.dependsOnKey,
      target: connection.assetKey,
      type: 'dependency',
      selected: false,
      zIndex: edgeLayer(false),
      markerEnd: { type: MarkerType.ArrowClosed },
      data: { connection, label: connection.condition, tone: 'dependency' },
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
  onFitRegion: (networkKey: string) => void
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

  // One adjacency index per document, shared by projection and drag handling.
  // Recomputing ownership per node per render was the canvas's quadratic hot spot.
  const graph = useMemo(() => buildTopologyGraph(props.document), [props.document])

  // Region actions are routed through a ref so their identity never changes:
  // region node data therefore holds no per-render closures.
  const handlers = useRef(props)
  handlers.current = props
  const regionActions = useMemo<TeamLabRegionActions>(
    () => ({
      toggleCollapsed: (networkKey, collapsed) => handlers.current.onToggleRegion(networkKey, collapsed),
      resize: (networkKey, width, height) => handlers.current.onResizeRegion(networkKey, width, height),
      fitToMembers: (networkKey) => handlers.current.onFitRegion(networkKey),
    }),
    []
  )

  const [nodes, setNodes] = useState(() =>
    applySelection(
      flowNodes(props.document, graph, props.readOnly, {
        actions: regionActions,
        selectedNetworkKey: props.selectedNetworkKey,
      }),
      props.selection.nodeKeys
    )
  )
  const [edges, setEdges] = useState(() =>
    applyEdgeSelection(flowEdges(props.document), props.selection.connectionKeys)
  )
  const lastProjectedSelection = useRef('|')
  const flow = useReactFlow<FlowNode, TeamLabFlowEdge>()
  const detailLevel = useCanvasDetailLevel()

  useEffect(() => {
    setNodes((current) =>
      reconcileNodes(
        current,
        flowNodes(props.document, graph, props.readOnly, {
          actions: regionActions,
          selectedNetworkKey: props.selectedNetworkKey,
        })
      )
    )
    setEdges((current) => reconcileEdges(current, flowEdges(props.document)))
  }, [graph, props.document, props.readOnly, regionActions, props.selectedNetworkKey])

  useEffect(() => {
    setNodes((current) => applySelection(current, props.selection.nodeKeys))
    setEdges((current) => applyEdgeSelection(current, props.selection.connectionKeys))
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
      const memberKeys = graph.membersByNetwork.get(node.data.networkKey) ?? []
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
    [graph, props.document]
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
      for (const node of draggedNodes) {
        if (node.type === 'region') {
          const networkKey = node.data.networkKey
          const layout = props.document.networkLayouts[networkKey]
          const bounds = regionBounds(props.document, graph.membersByNetwork.get(networkKey) ?? [])
          const startX = layout?.x ?? bounds.x - REGION_PADDING_X
          const startY = layout?.y ?? bounds.y - REGION_HEADER_HEIGHT
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
    [graph, props.document, props.onMoveNodes, props.onMoveRegion]
  )
  const onNodeDoubleClick = useCallback<NodeMouseHandler<FlowNode>>((_event, node) => {
      if (node.type !== 'region') return
      const memberKeys = graph.membersByNetwork.get(node.data.networkKey) ?? []
      if (memberKeys.length === 0) return
      void flow.fitView({
        nodes: memberKeys.map((key) => ({ id: key })),
        padding: 0.4,
        duration: window.matchMedia('(prefers-reduced-motion: reduce)').matches ? 0 : 260,
        maxZoom: 1.2,
      })
    },
    [flow, graph]
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
    <div
      className={`${styles.canvas} ${detailLevelClass[detailLevel]}`.trimEnd()}
      data-connection-mode={props.connectionMode}
      data-detail-level={detailLevel}
    >
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
        // 不要开启 onlyRenderVisibleElements：自动排版 + fitView 动画期间会裁剪边或
        // 以旧坐标闪现（headless A/B 实测：关闭后点击瞬间边即完整渲染）。
        panActivationKeyCode="Space"
        panOnDrag={selectionToolActive ? [1] : [0, 1]}
        selectionKeyCode={['Meta', 'Control']}
        selectionOnDrag={selectionToolActive}
        // The projection assigns every object an explicit layer, so React Flow
        // must not re-derive one: 'manual' is what keeps links above regions.
        zIndexMode="manual"
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
