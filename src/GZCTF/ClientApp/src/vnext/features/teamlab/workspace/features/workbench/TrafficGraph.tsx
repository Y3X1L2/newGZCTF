import { useComputedColorScheme } from '@mantine/core'
import { createSmartEdge, SmartEdgeProvider } from '@tisoap/react-flow-smart-edge'
import {
  Background,
  Handle,
  MarkerType,
  Position,
  ReactFlow,
  ReactFlowProvider,
  useNodesInitialized,
  useNodesState,
  useReactFlow,
  useStore,
  type Node,
  type NodeProps,
} from '@xyflow/react'
import { Network, Router, Server, Shrink, ZoomIn, ZoomOut } from 'lucide-react'
import { Fragment, useEffect, useMemo, useRef, useState, type CSSProperties } from 'react'
import { createPortal } from 'react-dom'
import type { EnvironmentSpec } from '../../api/client'
import { byteUnit, reading } from '../../foundation/format'
import styles from './TrafficGraph.module.css'
import { trafficTopology } from './trafficModel'
import { useTopology } from './useTopology'

export type TrafficSelection = { asset: string } | { source: string; destination: string }
export type EndpointKind = 'asset' | 'router' | 'network' | 'device'
export type TrafficLink = {
  source: string
  target: string
  sourceName: string
  targetName: string
  sourceKind?: EndpointKind
  targetKind?: EndpointKind
  sourceNetworkId?: string
  targetNetworkId?: string
  value: number
  rate?: number
  severity?: number
  alerts?: number
}
export const riskLevels = [
  { value: 'high', label: '高风险', color: 'var(--negative)' },
  { value: 'medium', label: '中风险', color: 'var(--warning)' },
  { value: 'low', label: '低风险', color: 'var(--accent)' },
] as const
export const riskLevel = (severity: number) => riskLevels[severity === 1 ? 0 : severity === 2 ? 1 : 2]

type TrafficNode = Node<{ name: string; color: string; kind: EndpointKind }, 'trafficAsset' | 'trafficNetwork'>
const icons = {
  asset: Server,
  router: Router,
  network: Network,
  device: Server,
}
function TrafficAsset({ data, selected }: NodeProps<TrafficNode>) {
  const Icon = icons[data.kind]
  return (
    <div
      className={styles.node}
      data-selected={selected}
      data-kind={data.kind}
      style={{ '--node-color': data.color } as CSSProperties}
    >
      <TrafficPorts />
      <span className={styles.symbol}>
        <Icon size={20} strokeWidth={1.6} />
      </span>
      <strong>{data.name}</strong>
    </div>
  )
}
function TrafficPorts() {
  return Object.entries({
    left: Position.Left,
    right: Position.Right,
    top: Position.Top,
    bottom: Position.Bottom,
  }).map(([id, position]) => (
    <Fragment key={id}>
      <Handle id={id} type="source" position={position} />
      <Handle id={id} type="target" position={position} />
    </Fragment>
  ))
}
function TrafficNetwork({ data, selected }: NodeProps<TrafficNode>) {
  return (
    <div className={styles.network} data-selected={selected} style={{ '--node-color': data.color } as CSSProperties}>
      <Handle id="header" type="target" position={Position.Left} />
      <header>
        <Network size={17} />
        <strong>{data.name}</strong>
      </header>
      <Handle id="header" type="source" position={Position.Left} />
    </div>
  )
}
const nodeTypes = {
  trafficAsset: TrafficAsset,
  trafficNetwork: TrafficNetwork,
}
const routingGeometry = { nodePadding: 5, gridRatio: 4 }
const edgeTypes = {
  communication: createSmartEdge('smoothstep', {
    ...routingGeometry,
    hops: { radius: 4, borderRadius: 6 },
  }),
}
const routingOptions = { ...routingGeometry, routeOnlyWhenBlocked: false }

function GraphActions({ toolsTarget }: { toolsTarget: HTMLElement | null }) {
  const initialized = useNodesInitialized(),
    fitted = useRef(false)
  const visible = useStore((state) => state.width > 0 && state.height > 0)
  const { fitView, zoomIn, zoomOut } = useReactFlow()
  useEffect(() => {
    if (!initialized || !visible || fitted.current) return
    fitted.current = true
    void fitView({ padding: 0.18, maxZoom: 1 })
  }, [initialized, visible, fitView])
  return (
    toolsTarget &&
    createPortal(
      <div className={styles.tools}>
        <button title="放大通信图" aria-label="放大通信图" onClick={() => void zoomIn()}>
          <ZoomIn size={15} />
        </button>
        <button title="缩小通信图" aria-label="缩小通信图" onClick={() => void zoomOut()}>
          <ZoomOut size={15} />
        </button>
        <button title="适应画布" aria-label="适应画布" onClick={() => void fitView({ padding: 0.18, maxZoom: 1 })}>
          <Shrink size={15} />
        </button>
      </div>,
      toolsTarget
    )
  )
}

function Graph({
  spec,
  links,
  groups,
  colors,
  selection,
  onSelect,
  toolsTarget,
}: {
  spec: EnvironmentSpec
  links: TrafficLink[]
  groups: Map<string, string>
  colors: Map<string, string>
  selection?: TrafficSelection
  onSelect: (selection?: TrafficSelection) => void
  toolsTarget: HTMLElement | null
}) {
  const theme = useComputedColorScheme()
  const merged = useMemo(() => {
    const pairs = new Map<string, TrafficLink & { forward: boolean; reverse: boolean }>()
    for (const link of links) {
      if (link.value <= 0 && !link.alerts) continue
      const [source, target] = [link.source, link.target].sort()
      const forward = source === link.source,
        key = JSON.stringify([source, target]),
        previous = pairs.get(key)
      if (previous) {
        previous.value += link.value
        previous.rate = (previous.rate ?? 0) + (link.rate ?? 0)
        previous.alerts = (previous.alerts ?? 0) + (link.alerts ?? 0)
        previous.forward ||= forward
        previous.reverse ||= !forward
        if (link.severity !== undefined) previous.severity = Math.min(previous.severity ?? 255, link.severity)
      } else
        pairs.set(key, {
          ...link,
          source,
          target,
          forward,
          reverse: !forward,
          sourceName: forward ? link.sourceName : link.targetName,
          targetName: forward ? link.targetName : link.sourceName,
          sourceKind: forward ? link.sourceKind : link.targetKind,
          targetKind: forward ? link.targetKind : link.sourceKind,
        })
    }
    return [...pairs.values()].sort((a, b) => a.source.localeCompare(b.source) || a.target.localeCompare(b.target))
  }, [links])
  const [hovered, setHovered] = useState<string>()
  const activeSpec = useMemo(() => trafficTopology(spec, links), [spec, links])
  const layout = useTopology(activeSpec, [])
  const models = useMemo<TrafficNode[]>(() => {
    const related = new Set<string>()
    if (selection && 'asset' in selection)
      for (const link of merged)
        if (link.source === selection.asset || link.target === selection.asset) {
          related.add(link.source)
          related.add(link.target)
        }
    return (layout.data?.nodes ?? []).map((node) => {
      const involved =
        !selection ||
        ('asset' in selection
          ? selection.asset === node.id || related.has(node.id) || node.type === 'network'
          : selection.source === node.id || selection.destination === node.id || node.type === 'network')
      const kind: EndpointKind =
        node.type === 'network'
          ? 'network'
          : node.type === 'router'
            ? 'router'
            : node.data.kind === 'external'
              ? 'device'
              : 'asset'
      return {
        id: node.id,
        parentId: node.parentId,
        type: node.type === 'network' ? 'trafficNetwork' : 'trafficAsset',
        position: node.position,
        measured: {
          width: Number(node.style!.width),
          height: Number(node.style!.height),
        },
        selected: Boolean(selection && 'asset' in selection && selection.asset === node.id),
        ariaLabel: node.data.label,
        zIndex: node.type === 'network' ? 0 : 3,
        style: { ...node.style, opacity: involved ? 1 : 0.3 },
        data: {
          name: node.data.label,
          kind,
          color: colors.get(groups.get(node.id) ?? '') ?? 'var(--chart-1)',
        },
      }
    })
  }, [layout.data, merged, selection, groups, colors])
  const [nodes, setNodes, onNodesChange] = useNodesState<TrafficNode>([])
  const current = useRef(nodes)
  current.current = nodes
  useEffect(() => {
    const roots = models.filter((node) => !node.parentId)
    const placed = new Map(
      current.current
        .filter((node) => !node.parentId && roots.some((root) => root.id === node.id))
        .map((node) => [node.id, node])
    )
    const step = {
      width: Math.max(0, ...roots.map((node) => Number(node.style!.width))) + 48,
      height: Math.max(0, ...roots.map((node) => Number(node.style!.height))) + 48,
    }
    const columns = Math.ceil(Math.sqrt(roots.length))
    const offset = (n: number) => (n % 2 ? (n + 1) / 2 : -n / 2)
    const arranged = roots.map((model) => {
      const previous = placed.get(model.id)
      const anchor = previous?.position ?? model.position
      placed.delete(model.id)
      let point = anchor,
        slot = 0
      while (
        [...placed.values()].some(
          (other) =>
            point.x < other.position.x + Number(other.style!.width) + 32 &&
            point.x + Number(model.style!.width) + 32 > other.position.x &&
            point.y < other.position.y + Number(other.style!.height) + 32 &&
            point.y + Number(model.style!.height) + 32 > other.position.y
        )
      ) {
        slot++
        point = {
          x: anchor.x + offset(slot % columns) * step.width,
          y: anchor.y + offset(Math.floor(slot / columns)) * step.height,
        }
      }
      const node = { ...model, position: point }
      placed.set(node.id, node)
      return node
    })
    setNodes([...arranged, ...models.filter((node) => node.parentId)])
  }, [models, setNodes])
  const byId = new Map(nodes.map((node) => [node.id, node]))
  const center = (node: TrafficNode) => ({
    x: node.position.x + (node.parentId ? byId.get(node.parentId)!.position.x : 0) + Number(node.style!.width) / 2,
    y: node.position.y + (node.parentId ? byId.get(node.parentId)!.position.y : 0) + Number(node.style!.height) / 2,
  })
  const edges = merged
    .filter((link) => byId.has(link.source) && byId.has(link.target))
    .map((link) => {
      const sourceNode = byId.get(link.source)!,
        targetNode = byId.get(link.target)!
      const a = center(sourceNode),
        b = center(targetNode)
      const horizontal = Math.abs(b.x - a.x) > Math.abs(b.y - a.y)
      const handles = horizontal
        ? a.x < b.x
          ? ['right', 'left']
          : ['left', 'right']
        : a.y < b.y
          ? ['bottom', 'top']
          : ['top', 'bottom']
      const id = JSON.stringify([link.source, link.target])
      const active = Boolean(
        selection &&
        ('asset' in selection
          ? selection.asset === link.source || selection.asset === link.target
          : (selection.source === link.source && selection.destination === link.target) ||
            (selection.source === link.target && selection.destination === link.source))
      )
      const color =
        link.severity !== undefined
          ? riskLevel(link.severity).color
          : (colors.get(groups.get(link.source) ?? groups.get(link.target) ?? '') ?? 'var(--accent)')
      const amount = link.rate && link.rate > 0 ? link.rate : link.value,
        unit = byteUnit(amount)
      const label =
        amount > 0
          ? `${reading(amount, unit.scale)} ${unit.unit}${link.rate && link.rate > 0 ? '/s' : ''}`
          : `${link.alerts} 次告警`
      return {
        id,
        source: link.source,
        target: link.target,
        sourceHandle:
          sourceNode.type === 'trafficNetwork' ? 'header' : sourceNode.parentId === targetNode.id ? 'top' : handles[0],
        targetHandle:
          targetNode.type === 'trafficNetwork' ? 'header' : targetNode.parentId === sourceNode.id ? 'top' : handles[1],
        type: 'communication',
        selected: active,
        zIndex: link.sourceKind === 'network' || link.targetKind === 'network' ? 1 : 2,
        label: active || hovered === id ? label : undefined,
        markerEnd: link.forward ? { type: MarkerType.ArrowClosed, color, width: 14, height: 14 } : undefined,
        markerStart: link.reverse ? { type: MarkerType.ArrowClosed, color, width: 14, height: 14 } : undefined,
        style: {
          stroke: color,
          strokeWidth: active ? 2.5 : 1.5,
          opacity: selection && !active ? 0.15 : 0.7,
        },
        labelStyle: { fill: 'var(--text)', fontSize: 11 },
        labelBgStyle: { fill: 'var(--surface)' },
        labelBgPadding: [6, 4] as [number, number],
        labelBgBorderRadius: 3,
        ariaLabel: `${link.sourceName} ↔ ${link.targetName}，${label}`,
      }
    })
  return (
    <div
      className={styles.graph}
      role="region"
      aria-label="资产通信图"
      onKeyDownCapture={(event) => {
        if (event.key === 'Escape') {
          event.stopPropagation()
          onSelect(undefined)
        }
      }}
    >
      <SmartEdgeProvider nodes={nodes} options={routingOptions}>
        <ReactFlow<TrafficNode>
          nodes={nodes}
          edges={edges}
          nodeTypes={nodeTypes}
          edgeTypes={edgeTypes}
          onNodesChange={(changes) => {
            onNodesChange(changes)
            for (const change of changes)
              if (change.type === 'select' && change.selected) onSelect({ asset: change.id })
          }}
          onNodeClick={(_, node) => onSelect({ asset: node.id })}
          onEdgeClick={(_, edge) => onSelect({ source: edge.source, destination: edge.target })}
          onEdgesChange={(changes) => {
            for (const change of changes)
              if (change.type === 'select' && change.selected) {
                const edge = edges.find((edge) => edge.id === change.id)!
                onSelect({ source: edge.source, destination: edge.target })
              }
          }}
          onEdgeMouseEnter={(_, edge) => setHovered(edge.id)}
          onEdgeMouseLeave={() => setHovered(undefined)}
          onPaneClick={() => onSelect(undefined)}
          nodesConnectable={false}
          nodesDraggable={false}
          deleteKeyCode={null}
          minZoom={0.1}
          maxZoom={2}
          colorMode={theme}
          proOptions={{ hideAttribution: true }}
        >
          <Background color="var(--line)" gap={24} size={1} />
          <GraphActions toolsTarget={toolsTarget} />
        </ReactFlow>
      </SmartEdgeProvider>
      {!models.length && <div className={styles.empty}>当前范围内无通信</div>}
      {layout.error && (
        <div className={styles.error} role="alert">
          {layout.error.message}
        </div>
      )}
    </div>
  )
}
export function TrafficGraph(props: Parameters<typeof Graph>[0]) {
  return (
    <ReactFlowProvider>
      <Graph {...props} />
    </ReactFlowProvider>
  )
}
