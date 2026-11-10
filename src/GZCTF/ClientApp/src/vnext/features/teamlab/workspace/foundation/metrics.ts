import type { Schema } from '../api/client'

export type History = Schema<'MetricHistory'>
export type Metric = Schema<'MetricSeries'>['metric']

export function assetMetricSeries(series: History['series']): History['series'] {
  const grouped = new Map<string, { series: History['series'][number]; points: Map<string, number> }>()
  for (const item of series) {
    const key = `${item.assetId}:${item.metric}`
    let group = grouped.get(key)
    if (!group) {
      group = {
        series: { ...item, interfaceId: undefined },
        points: new Map(),
      }
      grouped.set(key, group)
    }
    for (const point of item.points) group.points.set(point.time, (group.points.get(point.time) ?? 0) + point.value)
  }
  return [...grouped.values()].map(({ series, points }) => ({
    ...series,
    points: [...points].sort(([a], [b]) => a.localeCompare(b)).map(([time, value]) => ({ time, value })),
  }))
}

export function latestMetric(history: History | undefined, metric: Metric, nodeId?: string): number | undefined {
  const points = history?.series
    .filter((series) => series.metric === metric && (nodeId === undefined || series.nodeId === nodeId))
    .map((series) => series.points.at(-1))
    .filter((point) => point && Date.parse(history!.end) - Date.parse(point.time) <= 20_000)
  if (!points?.length) return undefined
  const time = Math.max(...points.map((point) => Date.parse(point!.time)))
  return points.reduce((total, point) => total + (Date.parse(point!.time) === time ? point!.value : 0), 0)
}
