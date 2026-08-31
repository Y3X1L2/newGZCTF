import { describe, expect, it } from 'vitest'
import {
  ASSET_NODE_HEIGHT,
  INFRA_NODE_HEIGHT,
  MAX_REGION_WIDTH,
  MIN_REGION_HEIGHT,
  MIN_REGION_WIDTH,
  NODE_WIDTH,
  REGION_MAX_COLUMNS,
  REGION_TARGET_ASPECT,
  clampRegionSize,
  nodeSizeOfType,
  planMemberGrid,
  regionSizeForMembers,
  resolveRegionSize,
  snapToGrid,
} from './topologyGeometry'
import { defaultTopologyPosition } from './topologyDocument'

describe('topologyGeometry', () => {
  it('derives node size from the node type, never from persisted metadata', () => {
    expect(nodeSizeOfType('switch')).toEqual({ width: NODE_WIDTH, height: INFRA_NODE_HEIGHT })
    expect(nodeSizeOfType('router')).toEqual({ width: NODE_WIDTH, height: INFRA_NODE_HEIGHT })
    for (const type of ['docker', 'linux-vm', 'windows-vm'] as const) {
      expect(nodeSizeOfType(type)).toEqual({ width: NODE_WIDTH, height: ASSET_NODE_HEIGHT })
    }
  })

  it('snaps coordinates onto the canvas grid', () => {
    expect(snapToGrid(0)).toBe(0)
    expect(snapToGrid(11)).toBe(8)
    expect(snapToGrid(-11)).toBe(-8)
    expect(snapToGrid(12)).toBe(16)
  })

  it('chooses the member grid whose block is closest to the target aspect', () => {
    // Cards are much wider than tall (224x108), so the closest block to 4:3 for
    // six members is 2 columns; the point of the search is that the shape is
    // chosen by measured aspect rather than a blind ceil(sqrt(n)).
    const plan = planMemberGrid(Array.from({ length: 6 }, () => ASSET_NODE_HEIGHT))
    const aspectOf = (columns: number, rows: number) =>
      (columns * NODE_WIDTH) / (rows * ASSET_NODE_HEIGHT)
    const chosen = Math.abs(aspectOf(plan.columns, plan.rows) - REGION_TARGET_ASPECT)
    for (const columns of [1, 2, 3, 4]) {
      const candidate = Math.abs(aspectOf(columns, Math.ceil(6 / columns)) - REGION_TARGET_ASPECT)
      expect(chosen).toBeLessThanOrEqual(candidate + 0.5)
    }
    expect(plan.columns * plan.rows).toBeGreaterThanOrEqual(6)
  })

  it('never exceeds the maximum member column count', () => {
    const plan = planMemberGrid(Array.from({ length: 40 }, () => ASSET_NODE_HEIGHT))
    expect(plan.columns).toBeLessThanOrEqual(REGION_MAX_COLUMNS)
    expect(plan.columns * plan.rows).toBeGreaterThanOrEqual(40)
  })

  it('keeps an empty region at the minimum presentable size', () => {
    const size = regionSizeForMembers([])
    expect(size.width).toBe(MIN_REGION_WIDTH)
    expect(size.height).toBe(MIN_REGION_HEIGHT)
  })

  it('grows a region monotonically with member count', () => {
    const sizes = [1, 2, 4, 8, 16].map((count) =>
      regionSizeForMembers(Array.from({ length: count }, () => ASSET_NODE_HEIGHT))
    )
    for (let index = 1; index < sizes.length; index += 1) {
      const area = sizes[index].width * sizes[index].height
      const previous = sizes[index - 1].width * sizes[index - 1].height
      expect(area).toBeGreaterThan(previous)
    }
  })

  it('clamps a region size into the persistable range', () => {
    expect(clampRegionSize({ width: 10, height: 10 })).toEqual({
      width: MIN_REGION_WIDTH,
      height: MIN_REGION_HEIGHT,
    })
    expect(clampRegionSize({ width: 99_999, height: 99_999 }).width).toBe(MAX_REGION_WIDTH)
  })

  it('prefers a persisted region size but still clamps it', () => {
    const members = [ASSET_NODE_HEIGHT, ASSET_NODE_HEIGHT]
    const derived = regionSizeForMembers(members)
    expect(resolveRegionSize(undefined, members)).toEqual(derived)
    expect(resolveRegionSize({ ...defaultTopologyPosition(), width: 900, height: 700 }, members)).toEqual({
      width: 900,
      height: 700,
    })
    // An out-of-range persisted size cannot escape the clamp, so a corrupted or
    // legacy record can never render an unbounded region.
    expect(resolveRegionSize({ ...defaultTopologyPosition(), width: 50, height: 50 }, members)).toEqual({
      width: MIN_REGION_WIDTH,
      height: MIN_REGION_HEIGHT,
    })
  })
})
