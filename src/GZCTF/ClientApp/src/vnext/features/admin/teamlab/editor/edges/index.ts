import type { EdgeTypes } from '@xyflow/react'
import { DependencyEdge } from './DependencyEdge'
import { NetworkEdge } from './NetworkEdge'
import { TrafficEdge } from './TrafficEdge'

export const teamLabEdgeTypes = {
  network: NetworkEdge,
  dependency: DependencyEdge,
  traffic: TrafficEdge,
} satisfies EdgeTypes
