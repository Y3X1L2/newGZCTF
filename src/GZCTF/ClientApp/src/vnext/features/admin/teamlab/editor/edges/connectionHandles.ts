import { getSmoothStepPath, Position } from '@xyflow/react'
import type { TopologyNode } from '../../model/topologyDocument'
import { nodeSize } from '../../model/topologyGeometry'

export const networkPathOptions = { borderRadius: 10, offset: 16 } as const

interface Point { x: number; y: number }
interface Box extends Point { width: number; height: number }

const sides = [Position.Top, Position.Right, Position.Bottom, Position.Left] as const
const sourceHandles = { top: 'source-top', right: 'source', bottom: 'source-bottom', left: 'source-left' }
const targetHandles = { top: 'target-top', right: 'target-right', bottom: 'target-bottom', left: 'target' }

function anchor(box: Box, side: Position): Point {
  return {
    x: box.x + (side === Position.Left ? 0 : side === Position.Right ? box.width : box.width / 2),
    y: box.y + (side === Position.Top ? 0 : side === Position.Bottom ? box.height : box.height / 2),
  }
}

/** Zero-radius SmoothStep has only orthogonal segments; Q controls repeat the corner. */
function orthogonalPoints(path: string): Point[] {
  const values = (path.match(/-?\d*\.?\d+(?:e[-+]?\d+)?/gi) ?? []).map(Number)
  const points: Point[] = []
  for (let index = 0; index < values.length; index += 2) {
    const next = { x: values[index], y: values[index + 1] }
    const last = points.at(-1)
    if (!last || last.x !== next.x || last.y !== next.y) points.push(next)
  }
  return points
}

function crosses(points: Point[], box: Box, padding: number) {
  const left = box.x - padding
  const right = box.x + box.width + padding
  const top = box.y - padding
  const bottom = box.y + box.height + padding
  return points.some((end, index) => {
    if (!index) return false
    const start = points[index - 1]
    // Touching a card boundary is permitted only at its own anchor; entering
    // the interior counts as a collision, including the source/target cards.
    if (start.x === end.x)
      return start.x > left && start.x < right && Math.max(start.y, end.y) > top && Math.min(start.y, end.y) < bottom
    return start.y > top && start.y < bottom && Math.max(start.x, end.x) > left && Math.min(start.x, end.x) < right
  })
}

function lowerScore(candidate: number[], current: number[]) {
  for (let index = 0; index < candidate.length; index++) {
    if (candidate[index] !== current[index]) return candidate[index] < current[index]
  }
  return false
}

/** Prefer a clear, short orthogonal route over the centre-to-centre direction. */
export function connectionHandles(
  source: TopologyNode | undefined,
  target: TopologyNode | undefined,
  nodes: readonly TopologyNode[] = []
) {
  if (!source || !target) return { sourceHandle: 'source', targetHandle: 'target' }
  const a = { ...source.position, ...nodeSize(source) }
  const b = { ...target.position, ...nodeSize(target) }
  const obstacles = nodes.filter(node => node.key !== source.key && node.key !== target.key)
    .map(node => ({ ...node.position, ...nodeSize(node) }))
  let best: { sourceHandle: string; targetHandle: string; score: number[] } | undefined
  for (const from of sides) {
    for (const to of sides) {
      const start = anchor(a, from)
      const end = anchor(b, to)
      const [path] = getSmoothStepPath({
        sourceX: start.x, sourceY: start.y, targetX: end.x, targetY: end.y,
        sourcePosition: from, targetPosition: to, ...networkPathOptions, borderRadius: 0,
      })
      const points = orthogonalPoints(path)
      const hits = [a, b, ...obstacles].filter(box => crosses(points, box, 0)).length
      const tight = obstacles.filter(box => crosses(points, box, 6)).length
      const length = points.reduce((total, point, index) => index
        ? total + Math.abs(point.x - points[index - 1].x) + Math.abs(point.y - points[index - 1].y) : total, 0)
      const bends = points.slice(1, -1).filter((point, index) => {
        const before = points[index]
        const after = points[index + 2]
        return (point.x - before.x) * (after.y - point.y) !== (point.y - before.y) * (after.x - point.x)
      }).length
      const score = [hits, tight, length + bends * 24, bends]
      if (!best || lowerScore(score, best.score))
        best = { sourceHandle: sourceHandles[from], targetHandle: targetHandles[to], score }
    }
  }
  return { sourceHandle: best!.sourceHandle, targetHandle: best!.targetHandle }
}
