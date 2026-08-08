import { networkMembersOf } from '../../model/topologyCommands'
import type { TopologyDocument, TopologyNode, TopologyPosition, TopologySwitchNode } from '../../model/topologyDocument'

const NODE_WIDTH = 208
const NODE_HEIGHT = 102
const GRID_SIZE = 8
const REGION_PADDING = 48
const MEMBER_GAP = 36
const REGION_COLUMN_GAP = 216
const REGION_ROW_GAP = 120

interface NetworkRegionPlan {
  networkKey: string
  switchKey: string
  memberKeys: readonly string[]
  rank: number
  rootSwitchKey: string | null
  width: number
  height: number
}

interface RegionMemberGrid {
  assetKeys: readonly string[]
  columns: number
  columnWidths: readonly number[]
  rowHeights: readonly number[]
  contentWidth: number
  contentHeight: number
}

interface RouteTopology {
  distances: ReadonlyMap<string, number>
  rootSwitches: ReadonlyMap<string, string | null>
}

function nodeSize(node: TopologyNode) {
  return {
    width: Math.max(1, node.position.width ?? NODE_WIDTH),
    height: Math.max(1, node.position.height ?? NODE_HEIGHT),
  }
}

function snap(value: number) {
  return Math.round(value / GRID_SIZE) * GRID_SIZE
}

function routeTopology(document: TopologyDocument): RouteTopology {
  const switches = Object.values(document.nodes)
    .filter((node): node is TopologySwitchNode => node.type === 'switch')
    .toSorted((left, right) => left.key.localeCompare(right.key))
  const adjacency = new Map(switches.map((node) => [node.key, new Set<string>()]))
  for (const connection of Object.values(document.connections)) {
    if (connection.type !== 'route') continue
    adjacency.get(connection.fromSwitchKey)?.add(connection.toSwitchKey)
    adjacency.get(connection.toSwitchKey)?.add(connection.fromSwitchKey)
  }

  const entry = switches.find((node) => node.isEntry) ?? switches[0]
  const distances = new Map<string, number>()
  const rootSwitches = new Map<string, string | null>()
  if (!entry) return { distances, rootSwitches }
  distances.set(entry.key, 0)
  rootSwitches.set(entry.key, null)
  const queue = [entry.key]
  for (let index = 0; index < queue.length; index += 1) {
    const current = queue[index]
    const distance = distances.get(current) ?? 0
    for (const next of [...(adjacency.get(current) ?? [])].sort()) {
      if (distances.has(next)) continue
      distances.set(next, distance + 1)
      rootSwitches.set(next, current === entry.key ? next : (rootSwitches.get(current) ?? current))
      queue.push(next)
    }
  }
  return { distances, rootSwitches }
}

function memberGrid(document: TopologyDocument, memberKeys: readonly string[]): RegionMemberGrid {
  const assetKeys = memberKeys.filter((key) => document.nodes[key]?.type !== 'switch' && !isBoundaryNode(document, key))
  const columns = Math.max(1, Math.min(3, Math.ceil(Math.sqrt(Math.max(assetKeys.length, 1)))))
  const rows = Math.max(1, Math.ceil(assetKeys.length / columns))
  const columnWidths = Array.from({ length: columns }, () => 0)
  const rowHeights = Array.from({ length: rows }, () => 0)
  assetKeys.forEach((key, index) => {
    const node = document.nodes[key]
    if (!node) return
    const size = nodeSize(node)
    const column = index % columns
    const row = Math.floor(index / columns)
    columnWidths[column] = Math.max(columnWidths[column], size.width)
    rowHeights[row] = Math.max(rowHeights[row], size.height)
  })
  return {
    assetKeys,
    columns,
    columnWidths,
    rowHeights,
    contentWidth: columnWidths.reduce((total, width) => total + width, 0) + Math.max(0, columns - 1) * MEMBER_GAP,
    contentHeight: rowHeights.reduce((total, height) => total + height, 0) + Math.max(0, rows - 1) * MEMBER_GAP,
  }
}

function isBoundaryNode(document: TopologyDocument, nodeKey: string) {
  let memberships = 0
  for (const connection of Object.values(document.connections)) {
    if (connection.type === 'membership' && connection.nodeKey === nodeKey) memberships += 1
    if (connection.type === 'route' && connection.viaNodeKey === nodeKey) return true
  }
  return memberships > 1
}

function regionSize(document: TopologyDocument, memberKeys: readonly string[]) {
  const grid = memberGrid(document, memberKeys)
  const switchNode = memberKeys.map((key) => document.nodes[key]).find((node) => node?.type === 'switch')
  const switchSize = switchNode ? nodeSize(switchNode) : { width: NODE_WIDTH, height: NODE_HEIGHT }
  const minimumWidth = Math.max(switchSize.width + REGION_PADDING * 2, grid.contentWidth + REGION_PADDING * 2)
  const minimumHeight = REGION_PADDING * 2 + switchSize.height +
    (grid.assetKeys.length ? MEMBER_GAP + grid.contentHeight : 0)
  return {
    width: minimumWidth,
    height: minimumHeight,
  }
}

function networkPlans(document: TopologyDocument): NetworkRegionPlan[] {
  const switches = Object.values(document.nodes)
    .filter((node): node is TopologySwitchNode => node.type === 'switch')
    .toSorted((left, right) => left.networkKey.localeCompare(right.networkKey) || left.key.localeCompare(right.key))
  const routes = routeTopology(document)
  const distances = routes.distances
  return switches.map((node) => {
    const memberKeys = networkMembersOf(document, node.networkKey).toSorted((left, right) => {
      const leftNode = document.nodes[left]
      const rightNode = document.nodes[right]
      if (leftNode?.type === 'switch') return -1
      if (rightNode?.type === 'switch') return 1
      return left.localeCompare(right)
    })
    const size = regionSize(document, memberKeys)
    const rank = distances.get(node.key)
    return {
      networkKey: node.networkKey,
      switchKey: node.key,
      memberKeys,
      // Disconnected networks are placed in their own compact group after the
      // routed topology. They have no route depth and must not consume a fake,
      // distant routing ring.
      rank: rank ?? -1,
      rootSwitchKey: routes.rootSwitches.get(node.key) ?? null,
      ...size,
    }
  })
}

/**
 * Returns deterministic grid-ring coordinates around the entry network. A grid
 * ring keeps variable-size regions from colliding while retaining the visual
 * reading order of a radial topology: centre -> routed neighbours -> edge.
 */
function ringSlots(ring: number, directionOffset = 0) {
  if (ring === 0) return [{ x: 0, y: 0 }]
  const perimeter: { x: number; y: number }[] = []
  for (let x = -ring; x <= ring; x += 1) perimeter.push({ x, y: -ring }, { x, y: ring })
  for (let y = -ring + 1; y < ring; y += 1) perimeter.push({ x: -ring, y }, { x: ring, y })
  const cardinalBase = [{ x: 0, y: -ring }, { x: ring, y: 0 }, { x: 0, y: ring }, { x: -ring, y: 0 }]
  const offset = ((directionOffset % cardinalBase.length) + cardinalBase.length) % cardinalBase.length
  const cardinal = [...cardinalBase.slice(offset), ...cardinalBase.slice(0, offset)]
  const cardinalKeys = new Set(cardinal.map((slot) => `${slot.x}:${slot.y}`))
  const remaining = perimeter
    .filter((slot) => !cardinalKeys.has(`${slot.x}:${slot.y}`))
    .toSorted((left, right) =>
      (Math.atan2(left.y, left.x) + Math.PI / 2 + Math.PI * 2) % (Math.PI * 2) -
      (Math.atan2(right.y, right.x) + Math.PI / 2 + Math.PI * 2) % (Math.PI * 2)
    )
  return [...cardinal, ...remaining]
}

function radialDirection(index: number, total: number) {
  // Prefer cardinal directions whenever they are sufficient: users can read
  // the first routing layer immediately as up, right, down and left. Wider
  // fan-outs still receive evenly distributed, deterministic bearings.
  const cardinal = [
    { x: 0, y: -1 },
    { x: 1, y: 0 },
    { x: 0, y: 1 },
    { x: -1, y: 0 },
  ]
  if (total <= cardinal.length) return cardinal[index]
  const angle = -Math.PI / 2 + (Math.PI * 2 * index) / total
  return { x: Math.cos(angle), y: Math.sin(angle) }
}

function directionSlotOrder(
  slots: readonly { x: number; y: number }[],
  direction: { x: number; y: number }
) {
  return slots.toSorted((left, right) => {
    const leftProjection = left.x * direction.x + left.y * direction.y
    const rightProjection = right.x * direction.x + right.y * direction.y
    if (rightProjection !== leftProjection) return rightProjection - leftProjection

    // For siblings on one layer, keep the closest points to the branch's
    // centre ray first. This forms a compact fan instead of a loose strip.
    const leftDeviation = Math.abs(left.x * direction.y - left.y * direction.x)
    const rightDeviation = Math.abs(right.x * direction.y - right.y * direction.x)
    if (leftDeviation !== rightDeviation) return leftDeviation - rightDeviation
    const leftAngle = (Math.atan2(left.y, left.x) + Math.PI * 2) % (Math.PI * 2)
    const rightAngle = (Math.atan2(right.y, right.x) + Math.PI * 2) % (Math.PI * 2)
    return leftAngle - rightAngle
  })
}

function placeRegionMembers(
  document: TopologyDocument,
  plan: NetworkRegionPlan,
  origin: { x: number; y: number },
  positions: Record<string, TopologyPosition>
) {
  const grid = memberGrid(document, plan.memberKeys)
  const assets = grid.assetKeys
  const switchNode = document.nodes[plan.switchKey]
  const switchSize = switchNode ? nodeSize(switchNode) : { width: NODE_WIDTH, height: NODE_HEIGHT }
  if (switchNode) {
    const size = nodeSize(switchNode)
    positions[plan.switchKey] = {
      ...switchNode.position,
      x: snap(origin.x + (plan.width - size.width) / 2),
      y: snap(origin.y + REGION_PADDING),
    }
  }
  const columnOffsets = grid.columnWidths.reduce<number[]>((offsets, width, index) => {
    offsets.push(index === 0 ? 0 : offsets[index - 1] + grid.columnWidths[index - 1] + MEMBER_GAP)
    return offsets
  }, [])
  const rowOffsets = grid.rowHeights.reduce<number[]>((offsets, height, index) => {
    offsets.push(index === 0 ? 0 : offsets[index - 1] + grid.rowHeights[index - 1] + MEMBER_GAP)
    return offsets
  }, [])
  const startX = origin.x + Math.max(REGION_PADDING, (plan.width - grid.contentWidth) / 2)
  assets.forEach((key, index) => {
    const node = document.nodes[key]
    if (!node) return
    const size = nodeSize(node)
    const row = Math.floor(index / grid.columns)
    const column = index % grid.columns
    positions[key] = {
      ...node.position,
      x: snap(startX + columnOffsets[column] + (grid.columnWidths[column] - size.width) / 2),
      y: snap(origin.y + REGION_PADDING + switchSize.height + MEMBER_GAP + rowOffsets[row]),
    }
  })
}

function hasNodeOverlap(
  document: TopologyDocument,
  positions: Readonly<Record<string, TopologyPosition>>,
  nodeKey: string,
  position: TopologyPosition
) {
  const node = document.nodes[nodeKey]
  if (!node) return false
  const size = nodeSize(node)
  return Object.entries(positions).some(([otherKey, otherPosition]) => {
    const other = document.nodes[otherKey]
    if (!other || otherKey === nodeKey) return false
    const otherSize = nodeSize(other)
    return position.x < otherPosition.x + otherSize.width &&
      position.x + size.width > otherPosition.x &&
      position.y < otherPosition.y + otherSize.height &&
      position.y + size.height > otherPosition.y
  })
}

/**
 * Arranges logical networks first, then their members. This avoids the former
 * node-only dagre layout where valid node positions could still make region
 * containers overlap each other.
 */
export function autoLayoutTopology(document: TopologyDocument): TopologyDocument {
  const nodes = Object.values(document.nodes).toSorted((left, right) => left.key.localeCompare(right.key))
  if (nodes.length < 2) return document

  const plans = networkPlans(document)
  if (plans.length === 0) {
    const columns = Math.max(1, Math.ceil(Math.sqrt(nodes.length)))
    const rows = Math.ceil(nodes.length / columns)
    const columnWidths = Array.from({ length: columns }, () => 0)
    const rowHeights = Array.from({ length: rows }, () => 0)
    nodes.forEach((node, index) => {
      const size = nodeSize(node)
      columnWidths[index % columns] = Math.max(columnWidths[index % columns], size.width)
      rowHeights[Math.floor(index / columns)] = Math.max(rowHeights[Math.floor(index / columns)], size.height)
    })
    const columnOffsets = columnWidths.reduce<number[]>((offsets, _, index) => {
      offsets.push(index === 0 ? 0 : offsets[index - 1] + columnWidths[index - 1] + MEMBER_GAP)
      return offsets
    }, [])
    const rowOffsets = rowHeights.reduce<number[]>((offsets, _, index) => {
      offsets.push(index === 0 ? 0 : offsets[index - 1] + rowHeights[index - 1] + MEMBER_GAP)
      return offsets
    }, [])
    const totalWidth = columnWidths.reduce((total, width) => total + width, 0) + (columns - 1) * MEMBER_GAP
    const totalHeight = rowHeights.reduce((total, height) => total + height, 0) + (rows - 1) * MEMBER_GAP
    const nextNodes = Object.fromEntries(nodes.map((node, index) => {
      const size = nodeSize(node)
      const column = index % columns
      const row = Math.floor(index / columns)
      return [node.key, {
        ...node,
        position: {
          ...node.position,
          x: snap(columnOffsets[column] - totalWidth / 2 + (columnWidths[column] - size.width) / 2),
          y: snap(rowOffsets[row] - totalHeight / 2 + (rowHeights[row] - size.height) / 2),
        },
      }]
    }))
    return { ...document, nodes: nextNodes }
  }
  const positions: Record<string, TopologyPosition> = {}
  const networkLayouts: Record<string, TopologyPosition> = { ...document.networkLayouts }
  const plansByRank = new Map<number, NetworkRegionPlan[]>()
  for (const plan of plans.filter((item) => item.rank >= 0)) {
    const group = plansByRank.get(plan.rank) ?? []
    group.push(plan)
    plansByRank.set(plan.rank, group)
  }

  const widestRegion = Math.max(...plans.map((plan) => plan.width))
  const tallestRegion = Math.max(...plans.map((plan) => plan.height))
  const slotWidth = widestRegion + REGION_COLUMN_GAP
  const slotHeight = tallestRegion + REGION_ROW_GAP
  const rootPlans = plans
    .filter((plan) => plan.rank === 1 && plan.rootSwitchKey === plan.switchKey)
    .toSorted((left, right) => left.networkKey.localeCompare(right.networkKey))
  const rootDirections = new Map(
    rootPlans.map((plan, index) => [plan.switchKey, radialDirection(index, rootPlans.length)])
  )
  const occupiedSlots = new Set<string>()
  let outerRing = 0
  for (const rank of [...plansByRank.keys()].sort((left, right) => left - right)) {
    const plansAtRank = plansByRank.get(rank)!.toSorted((left, right) => left.networkKey.localeCompare(right.networkKey))
    let planIndex = 0
    // One routing hop equals one visual ring. It is tempting to pack several
    // hops into the same ring, but doing so hides the actual route depth and
    // turns a routed branch into an arbitrary line or spiral.
    let ring = Math.max(0, rank)
    // A broad routing layer can exceed one ring's slots. Continue on the next
    // ring rather than allowing regions to overlap or reverting to a strip.
    while (planIndex < plansAtRank.length) {
      const candidates = ringSlots(ring).filter((slot) => !occupiedSlots.has(`${ring}:${slot.x}:${slot.y}`))
      const plan = plansAtRank[planIndex]
      if (!plan) break
      const direction = plan.rootSwitchKey ? rootDirections.get(plan.rootSwitchKey) : undefined
      const slots = direction ? directionSlotOrder(candidates, direction) : candidates
      const slot = slots[0]
      if (!slot) {
        ring += 1
        continue
      }
      const origin = {
        x: slot.x * slotWidth - plan.width / 2,
        y: slot.y * slotHeight - plan.height / 2,
      }
      networkLayouts[plan.networkKey] = {
        x: snap(origin.x),
        y: snap(origin.y),
        width: snap(plan.width),
        height: snap(plan.height),
        collapsed: document.networkLayouts[plan.networkKey]?.collapsed ?? false,
      }
      placeRegionMembers(document, plan, origin, positions)
      occupiedSlots.add(`${ring}:${slot.x}:${slot.y}`)
      planIndex += 1
    }
    outerRing = Math.max(outerRing, ring - 1)
  }

  const disconnectedPlans = plans.filter((plan) => plan.rank < 0).toSorted((left, right) => left.networkKey.localeCompare(right.networkKey))
  if (disconnectedPlans.length > 0) {
    const placedRegions = plans
      .filter((plan) => plan.rank >= 0)
      .map((plan) => networkLayouts[plan.networkKey])
      .filter((layout): layout is TopologyPosition => layout !== undefined)
    const groupStartX = Math.max(0, ...placedRegions.map((layout) => layout.x + (layout.width ?? 0))) + REGION_COLUMN_GAP
    const groupStartY = Math.min(0, ...placedRegions.map((layout) => layout.y))
    const groupColumns = Math.max(1, Math.ceil(Math.sqrt(disconnectedPlans.length)))
    const groupCellWidth = Math.max(...disconnectedPlans.map((plan) => plan.width)) + REGION_COLUMN_GAP
    const groupCellHeight = Math.max(...disconnectedPlans.map((plan) => plan.height)) + REGION_ROW_GAP
    disconnectedPlans.forEach((plan, index) => {
      const column = index % groupColumns
      const row = Math.floor(index / groupColumns)
      const origin = {
        x: groupStartX + column * groupCellWidth,
        y: groupStartY + row * groupCellHeight,
      }
      networkLayouts[plan.networkKey] = {
        x: snap(origin.x),
        y: snap(origin.y),
        width: snap(plan.width),
        height: snap(plan.height),
        collapsed: document.networkLayouts[plan.networkKey]?.collapsed ?? false,
      }
      placeRegionMembers(document, plan, origin, positions)
    })
  }

  const regionBySwitch = new Map(plans.map((plan) => [plan.switchKey, networkLayouts[plan.networkKey]]))
  const unplaced = nodes.filter((node) => !positions[node.key])
  let fallbackIndex = 0
  for (const node of unplaced) {
    const route = Object.values(document.connections).find(
      (connection): connection is Extract<(typeof document.connections)[string], { type: 'route' }> =>
        connection.type === 'route' && connection.viaNodeKey === node.key
    )
    const attachedSwitches = [...new Set(
      Object.values(document.connections)
        .filter((connection): connection is Extract<(typeof document.connections)[string], { type: 'membership' }> =>
          connection.type === 'membership' && connection.nodeKey === node.key)
        .map((connection) => connection.switchKey)
    )].sort()
    const attachedRegions = attachedSwitches
      .map((switchKey) => regionBySwitch.get(switchKey))
      .filter((region): region is TopologyPosition => region !== undefined)
    const source = route
      ? regionBySwitch.get(route.fromSwitchKey)
      : regionBySwitch.get(attachedSwitches[0] ?? '')
    const target = route
      ? regionBySwitch.get(route.toSwitchKey)
      : regionBySwitch.get(attachedSwitches[1] ?? '')
    const size = nodeSize(node)
    if (!route && attachedRegions.length >= 3) {
      const centre = attachedRegions.reduce(
        (total, region) => ({
          x: total.x + region.x + (region.width ?? 0) / 2,
          y: total.y + region.y + (region.height ?? 0) / 2,
        }),
        { x: 0, y: 0 }
      )
      const midpoint = { x: centre.x / attachedRegions.length, y: centre.y / attachedRegions.length }
      const candidate = [
        { x: 0, y: 0 }, { x: 1, y: 0 }, { x: -1, y: 0 }, { x: 0, y: 1 }, { x: 0, y: -1 },
        { x: 1, y: 1 }, { x: -1, y: 1 }, { x: 1, y: -1 }, { x: -1, y: -1 },
      ].map((offset) => ({
        ...node.position,
        x: snap(midpoint.x + offset.x * (size.width + MEMBER_GAP) - size.width / 2),
        y: snap(midpoint.y + offset.y * (size.height + MEMBER_GAP) - size.height / 2),
      })).find((position) => !hasNodeOverlap(document, positions, node.key, position))
      positions[node.key] = candidate ?? {
        ...node.position,
        x: snap((outerRing + 2) * slotWidth + fallbackIndex * (NODE_WIDTH + MEMBER_GAP)),
        y: 0,
      }
    } else if (source && target) {
      const sourceWidth = source.width ?? 0
      const sourceHeight = source.height ?? 0
      const targetWidth = target.width ?? 0
      const targetHeight = target.height ?? 0
      const sourceCenter = { x: source.x + sourceWidth / 2, y: source.y + sourceHeight / 2 }
      const targetCenter = { x: target.x + targetWidth / 2, y: target.y + targetHeight / 2 }
      const dx = targetCenter.x - sourceCenter.x
      const dy = targetCenter.y - sourceCenter.y
      const length = Math.hypot(dx, dy) || 1
      const midpoint = { x: (sourceCenter.x + targetCenter.x) / 2, y: (sourceCenter.y + targetCenter.y) / 2 }
      const candidate = [0, 1, -1, 2, -2, 3, -3, 4, -4]
        .map((laneOffset) => ({
          ...node.position,
          x: snap(midpoint.x - dy / length * laneOffset * (NODE_HEIGHT + MEMBER_GAP) - size.width / 2),
          y: snap(midpoint.y + dx / length * laneOffset * (NODE_HEIGHT + MEMBER_GAP) - size.height / 2),
        }))
        .find((position) => !hasNodeOverlap(document, positions, node.key, position))
      positions[node.key] = candidate ?? {
        ...node.position,
        x: snap((outerRing + 2) * slotWidth + fallbackIndex * (NODE_WIDTH + MEMBER_GAP)),
        y: 0,
      }
    } else {
      const column = fallbackIndex % 2
      const row = Math.floor(fallbackIndex / 2)
      positions[node.key] = {
        ...node.position,
        x: snap((outerRing + 2) * slotWidth + column * (NODE_WIDTH + MEMBER_GAP)),
        y: snap(row * (NODE_HEIGHT + MEMBER_GAP)),
      }
    }
    fallbackIndex += 1
  }

  const nextNodes = Object.fromEntries(
    nodes.map((node) => [node.key, { ...node, position: positions[node.key] ?? node.position }])
  )
  return { ...document, nodes: nextNodes, networkLayouts }
}
