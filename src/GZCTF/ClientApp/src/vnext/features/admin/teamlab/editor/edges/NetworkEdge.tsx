import { BaseEdge, EdgeText, getSmoothStepPath, type EdgeProps } from '@xyflow/react'
import type { TeamLabFlowEdge } from './edgeTypes'
import styles from './TopologyEdge.module.css'

export function NetworkEdge({
  sourceX,
  sourceY,
  targetX,
  targetY,
  sourcePosition,
  targetPosition,
  markerStart,
  markerEnd,
  data,
  selected,
}: EdgeProps<TeamLabFlowEdge>) {
  const [path, labelX, labelY] = getSmoothStepPath({
    sourceX,
    sourceY,
    targetX,
    targetY,
    sourcePosition,
    targetPosition,
    borderRadius: 8,
  })
  return (
    <>
      <BaseEdge
        className={`${styles.edge} ${styles.network} ${selected ? styles.selected : ''}`}
        markerStart={markerStart}
        markerEnd={markerEnd}
        path={path}
      />
      {data?.label ? (
        <EdgeText className={styles.label} label={data.label} x={labelX} y={labelY} />
      ) : null}
    </>
  )
}
