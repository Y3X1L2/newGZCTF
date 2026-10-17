import type { TopologyNode } from '../../model/topologyDocument'
import { nodeSize } from '../../model/topologyGeometry'

/** Face the other card instead of forcing every link out to the right and back in from the left. */
export function connectionHandles(source: TopologyNode | undefined, target: TopologyNode | undefined) {
  if (!source || !target) return { sourceHandle: 'source', targetHandle: 'target' }
  const a = { ...source.position, ...nodeSize(source) }
  const b = { ...target.position, ...nodeSize(target) }
  const dx = b.x + b.width / 2 - (a.x + a.width / 2)
  const dy = b.y + b.height / 2 - (a.y + a.height / 2)
  // Prefer the axis with the greatest clear space between the actual card bounds.
  // In a member grid this keeps links in the gap above cards, instead of wrapping
  // round the right-hand side of the whole network region.
  const horizontalGap = Math.abs(dx) - (a.width + b.width) / 2
  const verticalGap = Math.abs(dy) - (a.height + b.height) / 2
  if (horizontalGap > verticalGap)
    return dx >= 0
      ? { sourceHandle: 'source', targetHandle: 'target' }
      : { sourceHandle: 'source-left', targetHandle: 'target-right' }
  return dy >= 0
    ? { sourceHandle: 'source-bottom', targetHandle: 'target-top' }
    : { sourceHandle: 'source-top', targetHandle: 'target-bottom' }
}
