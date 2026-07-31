import { BaseEdge, getBezierPath, type EdgeProps } from '@xyflow/react'
import type { TeamLabFlowEdge } from './edgeTypes'
import styles from './TopologyEdge.module.css'

export function DependencyEdge({
  sourceX,
  sourceY,
  targetX,
  targetY,
  sourcePosition,
  targetPosition,
  markerEnd,
  selected,
}: EdgeProps<TeamLabFlowEdge>) {
  const [path] = getBezierPath({ sourceX, sourceY, targetX, targetY, sourcePosition, targetPosition })
  return (
    <BaseEdge
      className={`${styles.edge} ${styles.dependency} ${selected ? styles.selected : ''}`}
      markerEnd={markerEnd}
      path={path}
    />
  )
}
