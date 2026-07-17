import type { UserActivityPoint, UserProfileTrendPoint, UserSkillDimension } from './api/userProfileApi'
import { dimensionDefinition } from './skillDimensionRegistry'

export const profileTabs = ['overview', 'challenges', 'games', 'training'] as const
export type ProfileTab = (typeof profileTabs)[number]
export type ProfileWindow = '90d' | '365d'

export const profileTabLabels: Record<ProfileTab, string> = {
  overview: '概览',
  challenges: '做题',
  games: '赛事',
  training: '培训',
}

export function resolveProfileTab(value: string | null): ProfileTab {
  return profileTabs.includes(value as ProfileTab) ? (value as ProfileTab) : 'overview'
}

export function resolveProfileWindow(value: string | null): ProfileWindow {
  return value === '90d' ? '90d' : '365d'
}

export function historyTypeForTab(tab: ProfileTab) {
  if (tab === 'overview') return 'all'
  return tab
}

function dateOnly(date: Date) {
  return date.toISOString().slice(0, 10)
}

export function profileDateRange(window: ProfileWindow, now = new Date()) {
  const to = new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), now.getUTCDate()))
  const from = new Date(to)
  from.setUTCDate(from.getUTCDate() - (window === '90d' ? 89 : 364))
  return { from: dateOnly(from), to: dateOnly(to) }
}

export interface HeatmapCell {
  key: string
  date: string | null
  point: UserActivityPoint | null
  level: 0 | 1 | 2 | 3 | 4
}

export function activityLevel(total: number): HeatmapCell['level'] {
  if (total <= 0) return 0
  if (total === 1) return 1
  if (total === 2) return 2
  if (total <= 4) return 3
  return 4
}

export function buildHeatmapCells(points: UserActivityPoint[], from: string, to: string): HeatmapCell[] {
  const source = new Map(points.map((point) => [point.date, point]))
  const start = new Date(`${from}T00:00:00Z`)
  const end = new Date(`${to}T00:00:00Z`)
  start.setUTCDate(start.getUTCDate() - start.getUTCDay())
  end.setUTCDate(end.getUTCDate() + (6 - end.getUTCDay()))

  const cells: HeatmapCell[] = []
  for (const cursor = new Date(start); cursor <= end; cursor.setUTCDate(cursor.getUTCDate() + 1)) {
    const date = dateOnly(cursor)
    if (date < from || date > to) {
      cells.push({ key: `outside-${date}`, date: null, point: null, level: 0 })
      continue
    }
    const point = source.get(date) ?? null
    cells.push({ key: date, date, point, level: activityLevel(point?.total ?? 0) })
  }
  return cells
}

function polygonPoint(index: number, count: number, radius: number, center = 120) {
  const angle = (-90 + (360 / count) * index) * (Math.PI / 180)
  return `${(center + Math.cos(angle) * radius).toFixed(2)},${(center + Math.sin(angle) * radius).toFixed(2)}`
}

export function radarPolygon(dimensions: UserSkillDimension[], radius = 86) {
  if (!dimensions.length) return ''
  return dimensions
    .map((dimension, index) => polygonPoint(index, dimensions.length, (dimension.radarValue / 100) * radius))
    .join(' ')
}

export function radarGrid(count: number, radius: number) {
  return Array.from({ length: count }, (_, index) => polygonPoint(index, count, radius)).join(' ')
}

export function radarLabelPoint(index: number, count: number) {
  const [x, y] = polygonPoint(index, count, 108).split(',').map(Number)
  return { x, y }
}

export function orderedDimensions(dimensions: UserSkillDimension[]) {
  const source = new Map(dimensions.map((item) => [item.id, item]))
  return ['web', 'pwn', 'reverse', 'crypto', 'forensics-ir', 'pentest-osint', 'misc-ai-ppc', 'other']
    .map((id) => source.get(id))
    .filter((item): item is UserSkillDimension => Boolean(item))
}

export interface TrendGeometry {
  line: string
  area: string
  points: Array<{ x: number; y: number; point: UserProfileTrendPoint }>
  maximum: number
}

export function buildTrendGeometry(trend: UserProfileTrendPoint[], width = 720, height = 220): TrendGeometry {
  const plotTop = 16
  const plotBottom = height - 24
  const maximum = Math.max(1, ...trend.map((item) => item.cumulativeSolved))
  const points = trend.map((point, index) => ({
    x: trend.length <= 1 ? 0 : (index / (trend.length - 1)) * width,
    y: plotBottom - (point.cumulativeSolved / maximum) * (plotBottom - plotTop),
    point,
  }))
  const line = points.map((point) => `${point.x.toFixed(2)},${point.y.toFixed(2)}`).join(' ')
  const area = points.length ? `0,${plotBottom} ${line} ${width},${plotBottom}` : ''
  return { line, area, points, maximum }
}

export function profileDimensionLabel(id: string) {
  return dimensionDefinition(id).shortLabel
}
