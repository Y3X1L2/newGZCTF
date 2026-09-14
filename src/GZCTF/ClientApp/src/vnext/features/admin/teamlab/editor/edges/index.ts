import type { EdgeTypes } from '@xyflow/react'
import { NetworkEdge } from './NetworkEdge'
import { TrafficEdge } from './TrafficEdge'

export const teamLabEdgeTypes = {
  network: NetworkEdge,
  traffic: TrafficEdge,
} satisfies EdgeTypes
