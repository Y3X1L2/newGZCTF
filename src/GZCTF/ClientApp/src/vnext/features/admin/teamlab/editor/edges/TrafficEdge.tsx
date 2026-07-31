import { BaseEdge, getBezierPath, type EdgeProps } from '@xyflow/react'
import type { TeamLabFlowEdge } from './edgeTypes'
import styles from './TopologyEdge.module.css'

export function TrafficEdge({
  sourceX,
  sourceY,
  targetX,
  targetY,
  sourcePosition,
  targetPosition,
  markerEnd,
}: EdgeProps<TeamLabFlowEdge>) {
  const [path] = getBezierPath({ sourceX, sourceY, targetX, targetY, sourcePosition, targetPosition })
  return <BaseEdge className={`${styles.edge} ${styles.traffic}`} markerEnd={markerEnd} path={path} />
}
