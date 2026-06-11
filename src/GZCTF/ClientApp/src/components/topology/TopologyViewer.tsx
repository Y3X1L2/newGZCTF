import { ReactFlow, Controls, Background, MiniMap, BackgroundVariant } from '@xyflow/react'
import type { Node, Edge } from '@xyflow/react'
import '@xyflow/react/dist/style.css'
import TopologyNode, { type StageStatus } from './TopologyNode'

const nodeTypes = { topologyNode: TopologyNode }

interface TopologyViewerProps {
  nodes: Node[]
  edges: Edge[]
  currentStageIndex: number
  completedStageIds: number[]
}

export default function TopologyViewer({ nodes, edges, currentStageIndex, completedStageIds }: TopologyViewerProps) {
  // Filter nodes: locked nodes greyed, completed highlighted, current stage active
  const viewerNodes = nodes.map((node) => {
    const data = node.data as { stageIndex: number; status: StageStatus }
    let status: StageStatus = 'locked'

    if (completedStageIds.includes(data.stageIndex)) {
      status = 'completed'
    } else if (data.stageIndex === currentStageIndex) {
      status = 'unlocked'
    } else if (data.stageIndex < currentStageIndex) {
      status = 'completed'
    }

    return {
      ...node,
      data: { ...data, status },
    }
  })

  // Show only edges where both connected nodes are visible
  const unlockedNodeIds = new Set(viewerNodes.filter((n) => n.data.status !== 'locked').map((n) => n.id))
  const viewerEdges = edges.filter((e) => unlockedNodeIds.has(e.source) && unlockedNodeIds.has(e.target))

  return (
    <div style={{ height: 400, border: '1px solid #dee2e6', borderRadius: 8 }}>
      <ReactFlow
        nodes={viewerNodes}
        edges={viewerEdges}
        nodeTypes={nodeTypes}
        fitView
        nodesDraggable={false}
        nodesConnectable={false}
        elementsSelectable={false}
      >
        <Controls showInteractive={false} />
        <MiniMap />
        <Background variant={BackgroundVariant.Dots} gap={12} size={1} />
      </ReactFlow>
    </div>
  )
}
