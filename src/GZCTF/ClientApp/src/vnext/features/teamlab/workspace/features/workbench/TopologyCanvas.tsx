import { useComputedColorScheme } from '@mantine/core'
import { useReducedMotion } from '@mantine/hooks'
import {
  Background,
  BackgroundVariant,
  BaseEdge,
  ConnectionMode,
  Handle,
  MiniMap,
  Panel,
  Position,
  ReactFlow,
  ReactFlowProvider,
  useNodesInitialized,
  useReactFlow,
  useStoreApi,
  type Edge,
  type EdgeProps,
  type XYPosition,
  type NodeProps,
  type OnConnect,
} from '@xyflow/react'
import { Box, Maximize, Minimize, Monitor, Network, Router, Scan, ZoomIn, ZoomOut } from 'lucide-react'
import { Fragment, useEffect, useMemo, useRef, useState } from 'react'
import { stateLabel } from '../../foundation/format'
import styles from './TopologyCanvas.module.css'
import type { TopologyNode } from './topology'
import { absolutePositions, topologyNodeSize } from './topologyLayout'

const nodeTypes = { asset: AssetNode, network: NetworkNode, router: AssetNode }
const edgeTypes = { topology: TopologyEdge }
type Props = {
  nodes: TopologyNode[]
  edges: Edge[]
  selection?: string
  onSelect: (id?: string) => void
  editing: boolean
  onConnect: OnConnect
  onContext: (id: string, x: number, y: number) => void
}

export function TopologyCanvas(props: Props) {
  return (
    <ReactFlowProvider>
      <Canvas {...props} />
    </ReactFlowProvider>
  )
}

function Canvas({
  nodes: initialNodes,
  edges: initialEdges,
  selection,
  onSelect,
  editing,
  onConnect,
  onContext,
}: Props) {
  const nodes = useMemo(
    () =>
      initialNodes.map((node) => ({
        ...node,
        selected: node.id === selection,
      })),
    [initialNodes, selection]
  )
  const selectionParent = initialNodes.find((node) => node.id === selection)?.parentId
  const edges = useMemo(
    () =>
      initialEdges.map((edge) => {
        const connected =
          edge.target === selection ||
          edge.source === selection ||
          edge.target === selectionParent ||
          edge.source === selectionParent
        return {
          ...edge,
          selected: connected,
          style: {
            ...edge.style,
            opacity: selection && !connected ? 0.25 : 1,
            strokeWidth: connected ? 2 : 1.5,
          },
          label: connected ? edge.label : undefined,
        }
      }),
    [initialEdges, selection, selectionParent]
  )
  const scheme = useComputedColorScheme('light')
  const reducedMotion = useReducedMotion()
  const container = useRef<HTMLDivElement>(null)
  const [fullscreen, setFullscreen] = useState(false)
  const [error, setError] = useState<string>()
  const { fitView, getNodes, getNode, getViewport, setCenter, zoomIn, zoomOut } = useReactFlow<TopologyNode>()
  const store = useStoreApi()
  const initialized = useNodesInitialized()
  const fitted = useRef(false)
  useEffect(() => {
    if (!initialized || fitted.current) return
    fitted.current = true
    void fitView({ padding: 0.12, maxZoom: 1, minZoom: 0.05 })
  }, [initialized, fitView])
  useEffect(() => {
    if (!selection) return
    const frame = requestAnimationFrame(() => {
      const node = getNode(selection)
      if (!node) return
      const positions = absolutePositions(getNodes()),
        point = positions[selection]
      const { width, height } = store.getState(),
        viewport = getViewport()
      const size = node.measured ?? topologyNodeSize[node.type!]
      const selectionZoom =
        node.type === 'network'
          ? Math.min(1, (width - 48) / (size.width ?? 236), (height - 48) / (size.height ?? 84))
          : Math.max(viewport.zoom, 0.6)
      const left = point.x * viewport.zoom + viewport.x,
        top = point.y * viewport.zoom + viewport.y
      if (
        left >= 24 &&
        top >= 24 &&
        left + (size.width ?? 184) * viewport.zoom <= width - 24 &&
        top + (size.height ?? 64) * viewport.zoom <= height - 24 &&
        viewport.zoom >= 0.6
      )
        return
      void setCenter(point.x + (size.width ?? 184) / 2, point.y + (size.height ?? 64) / 2, {
        zoom: selectionZoom,
        duration: reducedMotion ? 0 : 180,
      })
    })
    return () => cancelAnimationFrame(frame)
  }, [selection, getNode, getNodes, getViewport, setCenter, store, reducedMotion])
  useEffect(() => {
    const changed = () => setFullscreen(document.fullscreenElement === container.current)
    document.addEventListener('fullscreenchange', changed)
    return () => document.removeEventListener('fullscreenchange', changed)
  }, [])
  return (
    <div ref={container} className={styles.frame} aria-label="拓扑画布">
      <ReactFlow<TopologyNode>
        className={styles.canvas}
        nodes={nodes}
        edges={edges}
        nodeTypes={nodeTypes}
        edgeTypes={edgeTypes}
        onConnect={onConnect}
        connectionMode={ConnectionMode.Loose}
        isValidConnection={(connection) => {
          const source = getNode(connection.source),
            target = getNode(connection.target)
          return Boolean(
            source &&
            target &&
            (source.type === 'network') !== (target.type === 'network') &&
            source.data.kind !== 'external' &&
            target.data.kind !== 'external'
          )
        }}
        nodesConnectable={editing}
        nodesDraggable={false}
        edgesReconnectable={false}
        deleteKeyCode={null}
        minZoom={0.05}
        maxZoom={2}
        colorMode={scheme}
        onNodeClick={(_, node) => onSelect(node.id)}
        onPaneClick={() => onSelect(undefined)}
        onNodeContextMenu={(event, node) => {
          event.preventDefault()
          onContext(node.id, event.clientX, event.clientY)
        }}
        proOptions={{ hideAttribution: true }}
      >
        <Background variant={BackgroundVariant.Dots} gap={28} size={1} color="var(--strong-line)" />
        <Panel position="bottom-left" className={styles.tools}>
          <button title="放大画布" aria-label="放大画布" onClick={() => void zoomIn()}>
            <ZoomIn size={16} />
          </button>
          <button title="缩小画布" aria-label="缩小画布" onClick={() => void zoomOut()}>
            <ZoomOut size={16} />
          </button>
          <button
            title="适应画布"
            aria-label="适应画布"
            onClick={() => void fitView({ padding: 0.12, maxZoom: 1, minZoom: 0.05 })}
          >
            <Scan size={16} />
          </button>
          <button
            title={fullscreen ? '退出全屏拓扑' : '全屏拓扑'}
            aria-label={fullscreen ? '退出全屏拓扑' : '全屏拓扑'}
            onClick={() => {
              setError(undefined)
              void (fullscreen ? document.exitFullscreen() : container.current!.requestFullscreen()).catch(
                (reason: Error) => setError(reason.message)
              )
            }}
          >
            {fullscreen ? <Minimize size={16} /> : <Maximize size={16} />}
          </button>
        </Panel>
        <MiniMap
          pannable
          zoomable
          position="bottom-right"
          nodeColor={(node) => (node.data as TopologyNode['data']).color}
          nodeStrokeWidth={1}
          nodeBorderRadius={5}
        />
        {error && (
          <Panel position="top-center">
            <div className={styles.error} role="alert">
              {error}
            </div>
          </Panel>
        )}
      </ReactFlow>
    </div>
  )
}

function Ports({ connectable = true }: { connectable?: boolean }) {
  return Object.entries({
    top: Position.Top,
    right: Position.Right,
    bottom: Position.Bottom,
    left: Position.Left,
  }).map(([id, position]) => (
    <Fragment key={id}>
      <Handle id={id} type="source" position={position} isConnectable={connectable} data-editable={connectable} />
    </Fragment>
  ))
}

function TopologyEdge({ id, data, style, label, labelStyle, labelBgStyle }: EdgeProps) {
  const points = data!.points as XYPosition[]
  const path = points.map((point, index) => `${index ? 'L' : 'M'}${point.x},${point.y}`).join(' ')
  let middle = points[0],
    longest = 0
  for (let i = 1; i < points.length; i++) {
    const a = points[i - 1],
      b = points[i],
      length = Math.abs(b.x - a.x) + Math.abs(b.y - a.y)
    if (length <= longest) continue
    longest = length
    middle = { x: (a.x + b.x) / 2, y: (a.y + b.y) / 2 }
  }
  return (
    <BaseEdge
      id={id}
      path={path}
      style={{ ...style, strokeLinejoin: 'round', strokeLinecap: 'round' }}
      label={label}
      labelX={middle.x}
      labelY={middle.y}
      labelStyle={labelStyle}
      labelBgStyle={labelBgStyle}
      interactionWidth={20}
    />
  )
}

function AssetNode({ data, selected, isConnectable }: NodeProps<TopologyNode>) {
  return (
    <div
      className={styles.asset}
      data-selected={selected}
      data-kind={data.kind}
      style={{ '--network-color': data.color } as React.CSSProperties}
    >
      <Ports connectable={isConnectable && data.kind !== 'external'} />
      <span className={styles.glyph}>
        {data.kind === 'external' || data.kind === 'router' ? (
          <Router size={19} />
        ) : data.kind === 'vm' ? (
          <Monitor size={19} />
        ) : (
          <Box size={19} />
        )}
      </span>
      <div className={styles.assetIdentity}>
        <strong>{data.label}</strong>
        <span className={styles.address}>
          {data.secondary || (data.kind === 'router' ? '原生路由器' : data.kind === 'vm' ? '虚拟机' : '容器')}
        </span>
      </div>
      {data.kind !== 'external' && data.kind !== 'router' && (
        <span className={styles.light} data-state={data.state} title={stateLabel(data.state)} />
      )}
    </div>
  )
}

function NetworkNode({ data, selected, isConnectable }: NodeProps<TopologyNode>) {
  return (
    <div
      className={styles.network}
      data-selected={selected}
      style={{ '--network-color': data.color } as React.CSSProperties}
    >
      <Ports connectable={isConnectable} />
      <div className={styles.networkHeading}>
        <span className={styles.networkGlyph}>
          <Network size={18} />
        </span>
        <div>
          <strong>{data.label}</strong>
          <span className={styles.cidr}>{data.secondary}</span>
        </div>
        <span className={styles.networkCount}>{data.count}</span>
      </div>
    </div>
  )
}
