import {
  ASSET_NODE_HEIGHT,
  DIAGRAM_TARGET_ASPECT,
  INFRA_NODE_HEIGHT,
  MEMBER_GAP_X,
  MEMBER_GAP_Y,
  NODE_WIDTH,
  REGION_GAP_X,
  REGION_GAP_Y,
  REGION_HEADER_HEIGHT,
  REGION_PADDING_BOTTOM,
  REGION_PADDING_X,
  TIER_BAND_HEIGHT,
  nodeSize,
  nodeSizeOf,
  planMemberGrid,
  regionSizeForMembers,
  snapToGrid,
} from '../../model/topologyGeometry'
import type { TopologyDocument, TopologyNode, TopologyPosition } from '../../model/topologyDocument'
import { buildTopologyGraph, computeRoutingDepth, type TopologyGraph } from './topologyGraph'

/**
 * Deterministic tiered layout for the TeamLab topology editor.
 *
 * Reading model: routing depth advances **left to right**, so the entry network
 * sits at the left and each further hop is a column to its right. That direction
 * is not arbitrary — device cards expose a target handle on their left edge and a
 * source handle on their right, so a horizontal flow keeps every link travelling
 * the way its handles already point, and it fills a wide (16:9) canvas instead of
 * stacking into a narrow strip. Regions sharing one depth stack vertically inside
 * that column, and border routers sit in the band *between* the two columns they
 * bridge, which is exactly where their links want to cross.
 *
 * Sizing rule: node boxes come from {@link nodeSize} (intrinsic to the node
 * type) and never from persisted editor metadata. Region boxes are recomputed
 * from members every run, so a manual resize cannot compound across saves.
 */

interface RegionPlan {
  networkKey: string
  switchKey: string
  /** Assets the region owns, excluding its switch and any border node. */
  memberKeys: readonly string[]
  width: number
  height: number
  depth: number
  branchRootSwitchKey: string | null
}

interface PlacedRegion extends RegionPlan {
  x: number
  y: number
}

/** Region rectangles a border node must not overlap, indexed on a coarse grid. */
class SpatialIndex {
  private readonly cellSize: number
  private readonly cells = new Map<string, { x: number; y: number; width: number; height: number }[]>()

  constructor(cellSize: number) {
    this.cellSize = Math.max(1, cellSize)
  }

  private *cellKeys(box: { x: number; y: number; width: number; height: number }) {
    const minX = Math.floor(box.x / this.cellSize)
    const maxX = Math.floor((box.x + box.width) / this.cellSize)
    const minY = Math.floor(box.y / this.cellSize)
    const maxY = Math.floor((box.y + box.height) / this.cellSize)
    for (let cx = minX; cx <= maxX; cx += 1) {
      for (let cy = minY; cy <= maxY; cy += 1) yield `${cx}:${cy}`
    }
  }

  insert(box: { x: number; y: number; width: number; height: number }) {
    for (const key of this.cellKeys(box)) {
      const bucket = this.cells.get(key)
      if (bucket) bucket.push(box)
      else this.cells.set(key, [box])
    }
  }

  /** True when `box` overlaps anything already inserted. O(occupied cells). */
  intersects(box: { x: number; y: number; width: number; height: number }) {
    for (const key of this.cellKeys(box)) {
      for (const other of this.cells.get(key) ?? []) {
        if (
          box.x < other.x + other.width &&
          box.x + box.width > other.x &&
          box.y < other.y + other.height &&
          box.y + box.height > other.y
        )
          return true
      }
    }
    return false
  }
}

function regionPlans(document: TopologyDocument, graph: TopologyGraph): RegionPlan[] {
  const routing = computeRoutingDepth(graph)
  return graph.switches.map((node) => {
    const members = graph.membersByNetwork.get(node.networkKey) ?? []
    const memberKeys = members
      .filter((key) => key !== node.key)
      .toSorted((left, right) => left.localeCompare(right))
    const heights = memberKeys.map((key) => nodeSize(document.nodes[key] ?? node).height)
    const size = regionSizeForMembers(heights)
    return {
      networkKey: node.networkKey,
      switchKey: node.key,
      memberKeys,
      width: size.width,
      height: size.height,
      // A network with no route path has no depth. It is grouped after the
      // routed topology instead of consuming a fake, distant tier.
      depth: routing.depths.get(node.key) ?? -1,
      branchRootSwitchKey: routing.branchRoots.get(node.key) ?? null,
    }
  })
}

/**
 * Splits one depth's regions into stacks (vertical runs). A depth with many
 * networks would otherwise become one very tall column; splitting it into a few
 * side-by-side stacks keeps the whole diagram near its target aspect.
 */
function packDepth(plans: readonly RegionPlan[], heightBudget: number) {
  const stacks: RegionPlan[][] = []
  let current: RegionPlan[] = []
  let currentHeight = 0
  for (const plan of plans) {
    const nextHeight = currentHeight === 0 ? plan.height : currentHeight + REGION_GAP_Y + plan.height
    if (current.length > 0 && nextHeight > heightBudget) {
      stacks.push(current)
      current = [plan]
      currentHeight = plan.height
      continue
    }
    current.push(plan)
    currentHeight = nextHeight
  }
  if (current.length > 0) stacks.push(current)
  return stacks
}

const stackHeight = (stack: readonly RegionPlan[]) =>
  stack.reduce((total, plan) => total + plan.height, 0) + Math.max(0, stack.length - 1) * REGION_GAP_Y

const stackWidth = (stack: readonly RegionPlan[]) => Math.max(...stack.map((plan) => plan.width), 0)

/**
 * Height budget for one depth column, chosen so the finished diagram approaches
 * {@link DIAGRAM_TARGET_ASPECT}: tall enough that a shallow topology is not a
 * single wide strip, short enough that a broad one is not a single tall column.
 */
function depthHeightBudget(plans: readonly RegionPlan[], depthCount: number) {
  const tallest = Math.max(...plans.map((plan) => plan.height), ASSET_NODE_HEIGHT)
  const totalArea = plans.reduce((total, plan) => total + plan.width * plan.height, 0)
  // Width the diagram would take if every depth were a single stack.
  const estimatedWidth =
    Math.max(1, depthCount) * (Math.max(...plans.map((plan) => plan.width), 1) + TIER_BAND_HEIGHT)
  const idealHeight = Math.max(tallest, Math.sqrt(Math.max(totalArea, 1) / DIAGRAM_TARGET_ASPECT))
  return Math.max(tallest, Math.min(idealHeight, estimatedWidth / DIAGRAM_TARGET_ASPECT))
}

/** Places a region's switch on its header row and its assets in the member grid. */
function placeRegionMembers(
  document: TopologyDocument,
  region: PlacedRegion,
  positions: Record<string, TopologyPosition>
) {
  // Vertical stack inside a region, matching `regionSizeForMembers` exactly:
  // header band -> switch -> gap -> member grid -> bottom padding.
  const switchNode = document.nodes[region.switchKey]
  if (switchNode) {
    const size = nodeSize(switchNode)
    positions[region.switchKey] = {
      ...switchNode.position,
      x: snapToGrid(region.x + (region.width - size.width) / 2),
      y: snapToGrid(region.y + REGION_HEADER_HEIGHT),
      width: null,
      height: null,
    }
  }

  const heights = region.memberKeys.map((key) => nodeSizeOf(document, key).height)
  const grid = planMemberGrid(heights)
  if (grid.rows === 0) return

  const gridTop = region.y + REGION_HEADER_HEIGHT + INFRA_NODE_HEIGHT + MEMBER_GAP_Y
  const gridLeft = region.x + Math.max(REGION_PADDING_X, (region.width - grid.contentWidth) / 2)
  let rowTop = gridTop
  for (let row = 0; row < grid.rows; row += 1) {
    const rowKeys = region.memberKeys.slice(row * grid.columns, row * grid.columns + grid.columns)
    const rowTallest = Math.max(...rowKeys.map((key) => nodeSizeOf(document, key).height), 0)
    rowKeys.forEach((key, column) => {
      const node = document.nodes[key]
      if (!node) return
      const size = nodeSize(node)
      positions[key] = {
        ...node.position,
        x: snapToGrid(gridLeft + column * (NODE_WIDTH + MEMBER_GAP_X) + (NODE_WIDTH - size.width) / 2),
        y: snapToGrid(rowTop + (rowTallest - size.height) / 2),
        width: null,
        height: null,
      }
    })
    rowTop += rowTallest + MEMBER_GAP_Y
  }
}

/**
 * Places border nodes (routers, dual-homed assets) between the regions they
 * bridge, nudging along the perpendicular of the region-to-region axis until the
 * node clears every region box.
 */
function placeBorderNodes(
  document: TopologyDocument,
  graph: TopologyGraph,
  placed: readonly PlacedRegion[],
  positions: Record<string, TopologyPosition>,
  fallbackOrigin: { x: number; y: number }
) {
  const regionByNetwork = new Map(placed.map((region) => [region.networkKey, region]))
  const index = new SpatialIndex(Math.max(NODE_WIDTH, ASSET_NODE_HEIGHT) * 2)
  for (const region of placed) index.insert(region)

  const pending = Object.values(document.nodes)
    .filter((node) => !positions[node.key])
    .toSorted((left, right) => left.key.localeCompare(right.key))

  let fallbackIndex = 0
  for (const node of pending) {
    const size = nodeSize(node)
    const attached = [...(graph.networksOfNode.get(node.key) ?? [])]
      .toSorted((a, b) => a.localeCompare(b))
      .flatMap((networkKey) => {
        const region = regionByNetwork.get(networkKey)
        return region ? [region] : []
      })

    if (attached.length === 0) {
      // Orphan node: park it in a tidy trailing row rather than at the origin.
      positions[node.key] = {
        ...node.position,
        x: snapToGrid(fallbackOrigin.x + fallbackIndex * (NODE_WIDTH + MEMBER_GAP_X)),
        y: snapToGrid(fallbackOrigin.y),
        width: null,
        height: null,
      }
      index.insert({ x: positions[node.key].x, y: positions[node.key].y, ...size })
      fallbackIndex += 1
      continue
    }

    const centre = attached.reduce(
      (total, region) => ({
        x: total.x + region.x + region.width / 2,
        y: total.y + region.y + region.height / 2,
      }),
      { x: 0, y: 0 }
    )
    const midpoint = { x: centre.x / attached.length, y: centre.y / attached.length }

    // Perpendicular of the axis between the two extreme attached regions keeps a
    // border node beside its own link rather than drifting across the diagram.
    const first = attached[0]
    const last = attached[attached.length - 1]
    const axis = {
      x: last.x + last.width / 2 - (first.x + first.width / 2),
      y: last.y + last.height / 2 - (first.y + first.height / 2),
    }
    const length = Math.hypot(axis.x, axis.y) || 1
    const perpendicular = { x: -axis.y / length, y: axis.x / length }
    const step = size.height + MEMBER_GAP_Y

    let resolved: TopologyPosition | null = null
    for (const lane of [0, 1, -1, 2, -2, 3, -3, 4, -4, 5, -5, 6, -6]) {
      const candidate = {
        x: snapToGrid(midpoint.x + perpendicular.x * lane * step - size.width / 2),
        y: snapToGrid(midpoint.y + perpendicular.y * lane * step - size.height / 2),
      }
      if (index.intersects({ ...candidate, ...size })) continue
      resolved = { ...node.position, ...candidate, width: null, height: null }
      break
    }

    positions[node.key] = resolved ?? {
      ...node.position,
      x: snapToGrid(fallbackOrigin.x + fallbackIndex * (NODE_WIDTH + MEMBER_GAP_X)),
      y: snapToGrid(fallbackOrigin.y),
      width: null,
      height: null,
    }
    if (!resolved) fallbackIndex += 1
    index.insert({ x: positions[node.key].x, y: positions[node.key].y, ...size })
  }
}

/** Grid fallback for an incomplete draft that has no switch to anchor regions. */
function layoutWithoutRegions(document: TopologyDocument): TopologyDocument {
  const nodes = Object.values(document.nodes).toSorted((left, right) => left.key.localeCompare(right.key))
  const columns = Math.max(1, Math.min(6, Math.round(Math.sqrt(nodes.length * DIAGRAM_TARGET_ASPECT))))
  const rows = Math.ceil(nodes.length / columns)
  const rowHeights = Array.from({ length: rows }, (_unused, row) =>
    Math.max(...nodes.slice(row * columns, row * columns + columns).map((node) => nodeSize(node).height), 0)
  )
  const totalWidth = columns * NODE_WIDTH + (columns - 1) * MEMBER_GAP_X
  const totalHeight = rowHeights.reduce((total, height) => total + height, 0) + (rows - 1) * MEMBER_GAP_Y

  let rowTop = -totalHeight / 2
  const nextNodes: Record<string, TopologyNode> = {}
  for (let row = 0; row < rows; row += 1) {
    const rowNodes = nodes.slice(row * columns, row * columns + columns)
    rowNodes.forEach((node, column) => {
      const size = nodeSize(node)
      nextNodes[node.key] = {
        ...node,
        position: {
          ...node.position,
          x: snapToGrid(-totalWidth / 2 + column * (NODE_WIDTH + MEMBER_GAP_X) + (NODE_WIDTH - size.width) / 2),
          y: snapToGrid(rowTop + (rowHeights[row] - size.height) / 2),
          width: null,
          height: null,
        },
      } as TopologyNode
    })
    rowTop += rowHeights[row] + MEMBER_GAP_Y
  }
  return { ...document, nodes: nextNodes }
}

export function autoLayoutTopology(document: TopologyDocument): TopologyDocument {
  if (Object.keys(document.nodes).length < 2) return document

  const graph = buildTopologyGraph(document)
  const plans = regionPlans(document, graph)
  if (plans.length === 0) return layoutWithoutRegions(document)

  const routed = plans.filter((plan) => plan.depth >= 0)
  const isolated = plans
    .filter((plan) => plan.depth < 0)
    .toSorted((left, right) => left.networkKey.localeCompare(right.networkKey))

  const depths = [...new Set(routed.map((plan) => plan.depth))].sort((left, right) => left - right)
  const budget = depthHeightBudget(plans, depths.length + (isolated.length > 0 ? 1 : 0))

  const placed: PlacedRegion[] = []
  let columnLeft = 0

  const placeStacks = (stacks: readonly RegionPlan[][]) => {
    for (const stack of stacks) {
      const width = stackWidth(stack)
      const height = stackHeight(stack)
      let cursor = -height / 2
      for (const plan of stack) {
        placed.push({ ...plan, x: snapToGrid(columnLeft + (width - plan.width) / 2), y: snapToGrid(cursor) })
        cursor += plan.height + REGION_GAP_Y
      }
      columnLeft += width + REGION_GAP_X
    }
    // Replace the trailing column gap with a full band so border routers get a
    // clear lane between the depths they bridge.
    columnLeft += TIER_BAND_HEIGHT - REGION_GAP_X
  }

  for (const depth of depths) {
    const column = routed
      .filter((plan) => plan.depth === depth)
      // Group one depth by branch so a routed chain stays visually together.
      .toSorted(
        (left, right) =>
          (left.branchRootSwitchKey ?? '').localeCompare(right.branchRootSwitchKey ?? '') ||
          left.networkKey.localeCompare(right.networkKey)
      )
    placeStacks(packDepth(column, budget))
  }

  if (isolated.length > 0) placeStacks(packDepth(isolated, budget))

  const positions: Record<string, TopologyPosition> = {}
  const networkLayouts: Record<string, TopologyPosition> = {}
  for (const region of placed) {
    networkLayouts[region.networkKey] = {
      x: region.x,
      y: region.y,
      width: region.width,
      height: region.height,
      collapsed: document.networkLayouts[region.networkKey]?.collapsed ?? false,
    }
    placeRegionMembers(document, region, positions)
  }

  // Orphans park in a trailing row beneath the diagram, never at the origin.
  const diagramBottom = Math.max(...placed.map((region) => region.y + region.height), 0)
  const diagramLeft = Math.min(...placed.map((region) => region.x), 0)
  placeBorderNodes(document, graph, placed, positions, {
    x: diagramLeft,
    y: diagramBottom + TIER_BAND_HEIGHT,
  })

  const nextNodes = Object.fromEntries(
    Object.values(document.nodes).map((node) => [
      node.key,
      positions[node.key] ? ({ ...node, position: positions[node.key] } as TopologyNode) : node,
    ])
  )
  return { ...document, nodes: nextNodes, networkLayouts }
}

/** Region content box, exported so the inspector can describe the derived size. */
export const regionContentInset = {
  top: REGION_HEADER_HEIGHT,
  bottom: REGION_PADDING_BOTTOM,
  horizontal: REGION_PADDING_X,
}
