import { describe, expect, it } from 'vitest'
import type { UserActivityPoint, UserProfileTrendPoint, UserSkillDimension } from './api/userProfileApi'
import {
  activityLevel,
  buildHeatmapCells,
  buildTrendGeometry,
  orderedDimensions,
  profileDateRange,
  radarPolygon,
  resolveProfileTab,
  resolveProfileWindow,
} from './profileDomain'

describe('profile domain', () => {
  it('normalizes URL state and date windows', () => {
    expect(resolveProfileTab('games')).toBe('games')
    expect(resolveProfileTab('invalid')).toBe('overview')
    expect(resolveProfileWindow('90d')).toBe('90d')
    expect(resolveProfileWindow('7d')).toBe('365d')
    expect(profileDateRange('90d', new Date('2026-07-17T09:00:00Z'))).toEqual({
      from: '2026-04-19',
      to: '2026-07-17',
    })
  })

  it('builds complete week heatmap cells without inventing activity', () => {
    const point: UserActivityPoint = {
      date: '2026-07-15',
      ctf: 2,
      training: 1,
      theory: 0,
      awdp: 0,
      penetration: 0,
      total: 3,
    }
    const cells = buildHeatmapCells([point], '2026-07-13', '2026-07-17')
    expect(cells).toHaveLength(7)
    expect(cells.find((cell) => cell.date === point.date)?.level).toBe(3)
    expect(cells.filter((cell) => cell.date === null)).toHaveLength(2)
    expect(activityLevel(8)).toBe(4)
  })

  it('keeps skill dimensions in registry order and creates bounded geometry', () => {
    const dimension = (id: string, radarValue: number): UserSkillDimension => ({
      id,
      label: id,
      solved: 1,
      attempted: 1,
      submissions: 1,
      acceptedSubmissions: 1,
      successRate: 100,
      benchmarkP90: 5,
      radarValue,
      sampleSufficient: false,
    })
    const ordered = orderedDimensions([dimension('crypto', 50), dimension('web', 100)])
    expect(ordered.map((item) => item.id)).toEqual(['web', 'crypto'])
    expect(radarPolygon(ordered)).toContain(',')
  })

  it('creates a cumulative line inside the SVG plot', () => {
    const trend: UserProfileTrendPoint[] = [
      { date: '2026-07-15', cumulativeSolved: 0, delta: 0 },
      { date: '2026-07-16', cumulativeSolved: 2, delta: 2 },
      { date: '2026-07-17', cumulativeSolved: 3, delta: 1 },
    ]
    const geometry = buildTrendGeometry(trend)
    expect(geometry.points).toHaveLength(3)
    expect(geometry.maximum).toBe(3)
    expect(geometry.points[2].y).toBeLessThan(geometry.points[1].y)
  })
})
