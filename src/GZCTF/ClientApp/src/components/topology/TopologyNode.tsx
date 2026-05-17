import { memo } from 'react';
import { Handle, Position, type NodeProps } from '@xyflow/react';
import { Badge, Text, Paper } from '@mantine/core';

type StageStatus = 'locked' | 'unlocked' | 'completed';

interface TopologyNodeData {
  label: string;
  nodeType: 'entry' | 'internal' | 'dc' | 'dmz' | 'core' | 'custom';
  status: StageStatus;
  skillDescription?: string;
  stageIndex: number;
}

const NODE_COLORS: Record<string, string> = {
  entry: '#4caf50',
  internal: '#2196f3',
  dc: '#9c27b0',
  dmz: '#ff9800',
  core: '#f44336',
  custom: '#607d8b',
};

const NODE_LABELS: Record<string, string> = {
  entry: '入口',
  internal: '内网',
  dc: '域控',
  dmz: 'DMZ',
  core: '核心',
  custom: '自定义',
};

const STATUS_COLORS: Record<StageStatus, string> = {
  locked: 'gray',
  unlocked: 'blue',
  completed: 'green',
};

function TopologyNode({ data }: NodeProps) {
  const nodeData = data as unknown as TopologyNodeData;
  const bgColor = NODE_COLORS[nodeData.nodeType] || NODE_COLORS.custom;
  const statusColor = STATUS_COLORS[nodeData.status];

  return (
    <Paper
      shadow={nodeData.status === 'unlocked' ? 'md' : 'sm'}
      style={{
        padding: '0.75rem 1rem',
        borderRadius: 8,
        border: `2px solid ${bgColor}`,
        background: nodeData.status === 'locked' ? '#f0f0f0' : 'white',
        minWidth: 140,
        textAlign: 'center',
        opacity: nodeData.status === 'locked' ? 0.5 : 1,
      }}
    >
      <Handle type="target" position={Position.Top} />
      <Badge size="xs" color={statusColor} mb={4}>
        {nodeData.status === 'locked' ? '锁定' : nodeData.status === 'unlocked' ? '进行中' : '已完成'}
      </Badge>
      <Text fw={600} size="sm">{nodeData.label}</Text>
      <Text size="xs" c="dimmed">阶段 {nodeData.stageIndex}</Text>
      <Badge size="xs" color={bgColor} variant="light" mt={4}>
        {NODE_LABELS[nodeData.nodeType] || nodeData.nodeType}
      </Badge>
      <Handle type="source" position={Position.Bottom} />
    </Paper>
  );
}

export { NODE_COLORS, NODE_LABELS, STATUS_COLORS };
export type { TopologyNodeData, StageStatus };
export default memo(TopologyNode);
