import { BaseEdge, getSmoothStepPath, type EdgeProps } from '@xyflow/react'
import { networkColorSlot } from '../networkColor'
import { EdgeLabel } from './EdgeLabel'
import styles from './TopologyEdge.module.css'
import type { TeamLabFlowEdge } from './edgeTypes'

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
    borderRadius: 10,
  })
  // A route between networks is the meaningful link; an intra-network membership
  // is structural context and stays visually quieter.
  const tone = data?.tone === 'route' ? styles.route : styles.membership
  const colorSlot = data?.tone === 'membership' && data.networkKey ? networkColorSlot(data.networkKey) : undefined
  return (
    <>
      <BaseEdge
        className={`${styles.edge} ${tone} ${selected ? styles.selected : ''}`}
        data-color-slot={colorSlot}
        markerStart={markerStart}
        markerEnd={markerEnd}
        path={path}
      />
      {data?.label ? <EdgeLabel label={data.label} x={labelX} y={labelY} /> : null}
    </>
  )
}
