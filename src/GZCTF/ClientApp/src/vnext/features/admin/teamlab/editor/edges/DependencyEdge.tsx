import { BaseEdge, getBezierPath, type EdgeProps } from '@xyflow/react'
import { EdgeLabel } from './EdgeLabel'
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
  data,
  selected,
}: EdgeProps<TeamLabFlowEdge>) {
  const [path, labelX, labelY] = getBezierPath({
    sourceX,
    sourceY,
    targetX,
    targetY,
    sourcePosition,
    targetPosition,
  })
  return (
    <>
      <BaseEdge
        className={`${styles.edge} ${styles.dependency} ${selected ? styles.selected : ''}`}
        markerEnd={markerEnd}
        path={path}
      />
      {/* A start-order dependency is meaningless without its condition, so the
          label ships with the link instead of living only in the inspector. */}
      {data?.label ? <EdgeLabel label={data.label} x={labelX} y={labelY} /> : null}
    </>
  )
}
