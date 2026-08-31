import type { TopologyDocument, TopologyNode, TopologyNodeType, TopologyPosition } from './topologyDocument'

/**
 * Single source of truth for topology canvas geometry.
 *
 * Node sizes are *intrinsic to the node type* and are never read back from
 * persisted editor metadata: `TopologyPosition` is one wire shape reused for
 * nodes and for network regions, and only regions are user-resizable. Trusting
 * `position.width` for a node let a resized region size leak onto its implicit
 * switch and then inflate the region again on every auto-layout round.
 *
 * Every consumer (auto layout, canvas projection, region commands, CSS) derives
 * its numbers from here so the rendered box and the laid-out box cannot drift.
 */

/** Canvas snap grid. Every emitted coordinate is a multiple of this value. */
export const TOPOLOGY_GRID = 8

/** Shared node width. A single width keeps grid columns visually aligned. */
export const NODE_WIDTH = 224

/** Infrastructure nodes (switch, router) carry fewer detail rows than assets. */
export const INFRA_NODE_HEIGHT = 92

/** Asset nodes (docker, linux-vm, windows-vm) show an extra resource row. */
export const ASSET_NODE_HEIGHT = 108

/** Reserved band at the top of a region for its title bar. */
export const REGION_HEADER_HEIGHT = 56

/** Horizontal inset between a region border and its members. */
export const REGION_PADDING_X = 28

/** Vertical inset between the last member row and the region's bottom border. */
export const REGION_PADDING_BOTTOM = 28

/** Gap between member columns inside a region. */
export const MEMBER_GAP_X = 28

/** Gap between member rows inside a region. */
export const MEMBER_GAP_Y = 24

/** Gap between sibling regions placed on the same tier row. */
export const REGION_GAP_X = 96

/** Gap between two region rows inside one routing tier. */
export const REGION_GAP_Y = 72

/**
 * Height of the band between two routing tiers. Border routers live here, so it
 * must fit one infrastructure node plus breathing room above and below.
 */
export const TIER_BAND_HEIGHT = INFRA_NODE_HEIGHT + 96

/** Widest member grid a region may use before it wraps to another row. */
export const REGION_MAX_COLUMNS = 4

/** Region aspect ratio the member grid aims for; matches a comfortable card. */
export const REGION_TARGET_ASPECT = 4 / 3

/** Whole-diagram aspect ratio the tier packer aims for; matches a 16:9 canvas. */
export const DIAGRAM_TARGET_ASPECT = 16 / 9

export interface TopologySize {
  width: number
  height: number
}

/** Rounds a coordinate onto the canvas snap grid. */
export const snapToGrid = (value: number) => Math.round(value / TOPOLOGY_GRID) * TOPOLOGY_GRID

/** Intrinsic rendered size of a node type. Never derived from persisted metadata. */
export function nodeSizeOfType(type: TopologyNodeType): TopologySize {
  return {
    width: NODE_WIDTH,
    height: type === 'switch' || type === 'router' ? INFRA_NODE_HEIGHT : ASSET_NODE_HEIGHT,
  }
}

/** Intrinsic rendered size of a node. */
export const nodeSize = (node: TopologyNode): TopologySize => nodeSizeOfType(node.type)

/** Intrinsic rendered size of a node key, falling back to the asset box. */
export function nodeSizeOf(document: TopologyDocument, key: string): TopologySize {
  const node = document.nodes[key]
  return node ? nodeSize(node) : { width: NODE_WIDTH, height: ASSET_NODE_HEIGHT }
}

/** Smallest region box that can still present a title bar and one member. */
export const MIN_REGION_WIDTH = NODE_WIDTH + REGION_PADDING_X * 2
export const MIN_REGION_HEIGHT =
  REGION_HEADER_HEIGHT + INFRA_NODE_HEIGHT + REGION_PADDING_BOTTOM

/** Largest region box a user may drag a region handle to. */
export const MAX_REGION_WIDTH = 4000
export const MAX_REGION_HEIGHT = 3000

export function clampRegionSize(size: TopologySize): TopologySize {
  return {
    width: Math.min(MAX_REGION_WIDTH, Math.max(MIN_REGION_WIDTH, size.width)),
    height: Math.min(MAX_REGION_HEIGHT, Math.max(MIN_REGION_HEIGHT, size.height)),
  }
}

export interface MemberGridPlan {
  columns: number
  rows: number
  /** Content box of the member grid, excluding region padding. */
  contentWidth: number
  contentHeight: number
}

/**
 * Chooses a member column count whose resulting block is closest to
 * {@link REGION_TARGET_ASPECT}. A fixed `ceil(sqrt(n))` produced very tall,
 * narrow regions for small networks and very wide ones for large networks.
 */
export function planMemberGrid(memberHeights: readonly number[]): MemberGridPlan {
  const count = memberHeights.length
  if (count === 0) return { columns: 1, rows: 0, contentWidth: NODE_WIDTH, contentHeight: 0 }

  let best: MemberGridPlan | null = null
  for (let columns = 1; columns <= Math.min(REGION_MAX_COLUMNS, count); columns += 1) {
    const rows = Math.ceil(count / columns)
    const contentWidth = columns * NODE_WIDTH + (columns - 1) * MEMBER_GAP_X
    let contentHeight = 0
    for (let row = 0; row < rows; row += 1) {
      const heights = memberHeights.slice(row * columns, row * columns + columns)
      contentHeight += Math.max(...heights, 0) + (row === 0 ? 0 : MEMBER_GAP_Y)
    }
    const candidate: MemberGridPlan = { columns, rows, contentWidth, contentHeight }
    if (!best) {
      best = candidate
      continue
    }
    const score = (plan: MemberGridPlan) =>
      Math.abs(plan.contentWidth / Math.max(plan.contentHeight, 1) - REGION_TARGET_ASPECT)
    if (score(candidate) < score(best)) best = candidate
  }
  return best ?? { columns: 1, rows: 0, contentWidth: NODE_WIDTH, contentHeight: 0 }
}

/**
 * Region box that exactly contains a title bar, the region's switch and its
 * member grid. This is the size auto layout emits and the size the "fit region
 * to members" command restores.
 */
export function regionSizeForMembers(memberHeights: readonly number[]): TopologySize {
  const grid = planMemberGrid(memberHeights)
  const width = Math.max(MIN_REGION_WIDTH, grid.contentWidth + REGION_PADDING_X * 2)
  const height =
    REGION_HEADER_HEIGHT +
    INFRA_NODE_HEIGHT +
    (grid.rows > 0 ? MEMBER_GAP_Y + grid.contentHeight : 0) +
    REGION_PADDING_BOTTOM
  return clampRegionSize({ width, height })
}

/** Effective region box, preferring a persisted user size over the derived one. */
export function resolveRegionSize(
  layout: TopologyPosition | undefined,
  memberHeights: readonly number[]
): TopologySize {
  const derived = regionSizeForMembers(memberHeights)
  return clampRegionSize({
    width: layout?.width ?? derived.width,
    height: layout?.height ?? derived.height,
  })
}
