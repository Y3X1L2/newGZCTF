import { useCallback, useState } from 'react';
import {
  ReactFlow, Controls, Background, MiniMap, addEdge, useNodesState, useEdgesState,
  type Connection, type Node, type Edge, BackgroundVariant, Panel,
} from '@xyflow/react';
import '@xyflow/react/dist/style.css';
import { Select, Button, Group, Text } from '@mantine/core';
import TopologyNode from './TopologyNode';
import type { TopologyNodeData } from './TopologyNode';

const nodeTypes = { topologyNode: TopologyNode };

const NODE_TYPE_OPTIONS = [
  { value: 'entry', label: '入口节点' },
  { value: 'dmz', label: 'DMZ 节点' },
  { value: 'internal', label: '内网主机' },
  { value: 'dc', label: '域控制器' },
  { value: 'core', label: '核心节点' },
  { value: 'custom', label: '自定义' },
];

interface TopologyEditorProps {
  initialNodes?: Node[];
  initialEdges?: Edge[];
  onChange?: (nodes: Node[], edges: Edge[]) => void;
  stages?: { index: number; title: string }[];
}

let nodeIdCounter = 0;

export default function TopologyEditor({ initialNodes, initialEdges, onChange, stages }: TopologyEditorProps) {
  const [nodes, setNodes, onNodesChange] = useNodesState(initialNodes ?? []);
  const [edges, setEdges, onEdgesChange] = useEdgesState(initialEdges ?? []);
  const [selectedType, setSelectedType] = useState<string>('entry');

  const onConnect = useCallback((params: Connection) => {
    setEdges(eds => addEdge({ ...params, animated: true }, eds));
  }, [setEdges]);

  const addNode = () => {
    const newNode: Node = {
      id: `node_${++nodeIdCounter}`,
      type: 'topologyNode',
      position: { x: Math.random() * 400 + 50, y: Math.random() * 300 + 50 },
      data: {
        label: `节点 ${nodeIdCounter}`,
        nodeType: selectedType,
        status: 'unlocked',
        stageIndex: nodeIdCounter,
      } as unknown as Record<string, unknown>,
    };
    const updated = [...nodes, newNode];
    setNodes(updated);
    onChange?.(updated, edges);
  };

  const handleChange = () => {
    onChange?.(nodes, edges);
  };

  return (
    <div style={{ height: 500, border: '1px solid #dee2e6', borderRadius: 8 }}>
      <ReactFlow
        nodes={nodes}
        edges={edges}
        onNodesChange={onNodesChange}
        onEdgesChange={onEdgesChange}
        onConnect={onConnect}
        nodeTypes={nodeTypes}
        fitView
        onMoveEnd={handleChange}
      >
        <Controls />
        <MiniMap />
        <Background variant={BackgroundVariant.Dots} gap={12} size={1} />
        <Panel position="top-left">
          <Group gap="xs" p="sm" style={{ background: 'white', borderRadius: 4, boxShadow: '0 1px 3px rgba(0,0,0,0.1)' }}>
            <Select data={NODE_TYPE_OPTIONS} value={selectedType}
              onChange={v => setSelectedType(v ?? 'entry')} w={130} />
            <Button size="xs" onClick={addNode} data-testid="add-topology-node">+ 添加节点</Button>
          </Group>
        </Panel>
      </ReactFlow>
    </div>
  );
}
