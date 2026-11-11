import { useComputedColorScheme } from '@mantine/core'
import { LineChart } from 'echarts/charts'
import {
  GridComponent,
  LegendComponent,
  TooltipComponent,
  AriaComponent,
  AxisPointerComponent,
  DataZoomInsideComponent,
  DataZoomSliderComponent,
  MarkLineComponent,
} from 'echarts/components'
import * as echarts from 'echarts/core'
import { CanvasRenderer } from 'echarts/renderers'
import { useEffect, useRef } from 'react'
import styles from './MetricChart.module.css'
import { clockTime } from './format'
import type { History, Metric } from './metrics'
import { assetMetricSeries } from './metrics'

echarts.use([
  LineChart,
  GridComponent,
  LegendComponent,
  TooltipComponent,
  AriaComponent,
  AxisPointerComponent,
  DataZoomInsideComponent,
  DataZoomSliderComponent,
  MarkLineComponent,
  CanvasRenderer,
])
export const metricTitles: Partial<Record<Metric, string>> = {
  cpu: 'CPU',
  memory: '内存',
  disk_read: '读取',
  disk_write: '写入',
  receive: '接收',
  transmit: '发送',
  cpu_capacity: 'CPU 容量',
  memory_capacity: '内存容量',
  memory_available: '可用内存',
  disk_used: '磁盘占用',
  disk_capacity: '磁盘容量',
  receive_packets: '接收包',
  transmit_packets: '发送包',
  receive_drops: '接收丢包',
  transmit_drops: '发送丢包',
}
const titles = metricTitles
const emptyNames = new Map<string, string>()

const metricNumber = (value: number, digits = 4) => value.toLocaleString('zh-CN', { maximumSignificantDigits: digits })

export function MetricChart({
  history,
  measures,
  unit,
  scale = 1,
  names = emptyNames,
  seriesBy = 'interface',
  colors,
  group,
  compact = false,
  timeline = false,
  onTimeRange,
  timeWindow,
  annotations,
  showLegend = true,
  focusedAsset,
}: {
  history: History
  measures: Metric[]
  unit: string
  scale?: number
  names?: ReadonlyMap<string, string>
  seriesBy?: 'metric' | 'asset' | 'node' | 'interface'
  colors?: string[]
  group?: string
  compact?: boolean
  timeline?: boolean
  onTimeRange?: (start: string, end: string) => void
  timeWindow?: { start: string; end: string }
  annotations?: { time: string; label: string; color: string }[]
  showLegend?: boolean
  focusedAsset?: string
}) {
  const host = useRef<HTMLDivElement>(null)
  const chart = useRef<echarts.EChartsType>(null)
  const theme = useComputedColorScheme()
  const rangeCallback = useRef(onTimeRange)
  rangeCallback.current = onTimeRange
  useEffect(() => {
    const current = echarts.init(host.current!)
    chart.current = current
    current.on('datazoom', () => {
      const option = current.getOption() as {
        dataZoom?: { startValue?: number; endValue?: number }[]
      }
      const zoom = option.dataZoom?.[0]
      if (zoom?.startValue !== undefined && zoom.endValue !== undefined)
        rangeCallback.current?.(new Date(zoom.startValue).toISOString(), new Date(zoom.endValue).toISOString())
    })
    if (group) {
      current.group = group
      echarts.connect(group)
    }
    const observer = new ResizeObserver(() => current.resize())
    observer.observe(host.current!)
    return () => {
      observer.disconnect()
      current.dispose()
      chart.current = null
    }
  }, [theme, group])
  useEffect(() => {
    const css = getComputedStyle(host.current!)
    const muted = css.getPropertyValue('--muted').trim()
    const line = css.getPropertyValue('--line').trim()
    const text = css.getPropertyValue('--text').trim()
    const surface = css.getPropertyValue('--surface').trim()
    const subtle = css.getPropertyValue('--surface-subtle').trim()
    const colorValue = (color: string) =>
      color.startsWith('var(') ? css.getPropertyValue(color.slice(4, -1)).trim() : color
    const palette = (colors ?? Array.from({ length: 6 }, (_, i) => `var(--chart-${i + 1})`)).map(colorValue)
    const readings: string[] = []
    const plotted = timeline ? assetMetricSeries(history.series) : history.series
    const identities = [
      ...names.keys(),
      ...new Set(
        plotted.map((item) =>
          seriesBy === 'asset' ? item.assetId : seriesBy === 'node' ? item.nodeId : item.interfaceId
        )
      ),
    ]
    const series = plotted
      .filter((item) => measures.includes(item.metric) && (!focusedAsset || item.assetId === focusedAsset))
      .map((item, index) => {
        const identity = seriesBy === 'asset' ? item.assetId : seriesBy === 'node' ? item.nodeId : item.interfaceId
        const color = palette[(timeline ? identities.indexOf(identity) : index) % palette.length]
        const points: [number, number | null][] = []
        const step = history.stepSeconds * 1000
        let previous = Date.parse(history.start)
        for (const point of item.points) {
          const time = Date.parse(point.time)
          if (time - previous > step * 1.5) points.push([previous + step, null])
          points.push([time, point.value / scale])
          previous = time
        }
        if (Date.parse(history.end) - previous > step * 1.5) points.push([previous + step, null])
        const name = [
          seriesBy !== 'metric' &&
            names.get(
              seriesBy === 'node' ? item.nodeId : seriesBy === 'asset' ? item.assetId : (item.interfaceId ?? '')
            ),
          titles[item.metric],
        ]
          .filter(Boolean)
          .join(' · ')
        const latest = item.points.at(-1)
        if (latest)
          readings.push(name + ' ' + metricNumber(latest.value / scale, 5) + ' ' + unit + '，' + clockTime(latest.time))
        return {
          id: [item.nodeId, item.assetId, item.interfaceId, item.metric].join(':'),
          name: timeline ? identity : name,
          type: 'line' as const,
          data: points,
          showSymbol: false,
          smooth: timeline ? 0.15 : false,
          smoothMonotone: 'x' as const,
          sampling: 'lttb' as const,
          connectNulls: false,
          lineStyle: {
            width: timeline ? 1.5 : 2,
            color,
            type: timeline && item.metric === 'receive' ? 'dashed' : 'solid',
          },
          itemStyle: { color },
          markLine:
            timeline && index === 0
              ? {
                  silent: false,
                  symbol: 'none',
                  label: { show: false },
                  data:
                    annotations?.map((event) => ({
                      name: event.label,
                      xAxis: Date.parse(event.time),
                      lineStyle: {
                        color: colorValue(event.color),
                        width: 1,
                        type: 'dashed',
                      },
                      tooltip: { formatter: () => event.label },
                    })) ?? [],
                }
              : undefined,
          symbolSize: 5,
          areaStyle: {
            color: new echarts.graphic.LinearGradient(0, 0, 0, 1, [
              {
                offset: 0,
                color: echarts.color.modifyAlpha(color, timeline ? 0.1 : theme === 'dark' ? 0.22 : 0.14),
              },
              {
                offset: 1,
                color: echarts.color.modifyAlpha(color, 0.01),
              },
            ]),
          },
          emphasis: { focus: 'series' as const },
        }
      })
    chart.current!.setOption(
      {
        animation: false,
        textStyle: { fontFamily: css.fontFamily },
        aria: {
          enabled: true,
          label: {
            description:
              measures.map((item) => titles[item]).join('、') +
              '历史曲线。' +
              (readings.length ? '最近记录：' + readings.join('；') : '暂无记录'),
          },
        },
        color: palette,
        grid: {
          left: 12,
          right: 18,
          top: timeline && !showLegend ? 12 : compact ? 25 : 30,
          bottom: timeline ? 34 : compact ? 12 : 36,
          containLabel: true,
        },
        tooltip: {
          trigger: 'axis',
          confine: true,
          renderMode: 'richText',
          backgroundColor: surface,
          borderColor: line,
          padding: [10, 14],
          textStyle: { color: text, fontSize: 12 },
          order: 'valueDesc',
          formatter: timeline
            ? (items: unknown) => {
                const points = items as {
                  axisValue: number
                  seriesId: string
                  seriesName: string
                  value: [number, number | null]
                }[]
                return [
                  new Date(points[0].axisValue).toLocaleString('zh-CN', {
                    hour12: false,
                  }),
                  ...points.map((point) => {
                    const measure = point.seriesId.split(':').at(-1) as Metric
                    const value = point.value[1]
                    return `${names.get(point.seriesName) ?? '历史资产'} · ${titles[measure]}    ${value === null ? '—' : metricNumber(value, 5) + ' ' + unit}`
                  }),
                ].join('\n')
              }
            : undefined,
          axisPointer: {
            type: 'cross',
            animation: false,
            lineStyle: { color: muted, type: 'dashed', opacity: 0.5 },
            crossStyle: { color: muted, type: 'dashed', opacity: 0.5 },
            label: {
              backgroundColor: surface,
              borderColor: line,
              borderWidth: 1,
              color: text,
              formatter: (point: { axisDimension: string; value: number }) =>
                point.axisDimension === 'x'
                  ? new Date(point.value).toLocaleString('zh-CN', {
                      hour12: false,
                    })
                  : metricNumber(Number(point.value)) + ' ' + unit,
            },
          },
          valueFormatter: (value: number | null) =>
            value === null ? '—' : metricNumber(Number(value), 5) + ' ' + unit,
        },
        xAxis: {
          type: 'time',
          min: Date.parse(history.start),
          max: Date.parse(history.end),
          axisLine: { lineStyle: { color: line } },
          axisLabel: {
            color: muted,
            hideOverlap: true,
            fontSize: 11,
            margin: 12,
          },
          axisTick: { show: false },
          splitLine: { show: false },
        },
        yAxis: {
          type: 'value',
          min: 0,
          splitNumber: 3,
          name: timeline ? '' : unit,
          nameTextStyle: {
            color: muted,
            align: 'left',
            fontSize: 11,
            padding: [0, 0, 2, 0],
          },
          axisLabel: {
            color: muted,
            fontSize: 11,
            formatter: (value: number) => metricNumber(value, 3),
          },
          splitLine: {
            lineStyle: { color: line, type: 'dashed', opacity: 0.7 },
          },
        },
        legend: {
          show: showLegend && !compact && (measures.length > 1 || series.length > 1),
          top: timeline ? 4 : undefined,
          bottom: timeline ? undefined : 0,
          left: 12,
          type: 'scroll',
          formatter: timeline ? (name: string) => `{pill|${names.get(name) ?? '历史资产'}}` : undefined,
          textStyle: {
            color: muted,
            fontSize: 12,
            rich: {
              pill: {
                color: text,
                fontSize: 11,
                backgroundColor: subtle,
                borderColor: line,
                borderWidth: 1,
                borderRadius: 5,
                padding: [4, 8],
              },
            },
          },
          itemGap: 12,
          icon: timeline ? 'circle' : 'roundRect',
          itemWidth: timeline ? 6 : 12,
          itemHeight: timeline ? 6 : 3,
        },
        dataZoom: [
          {
            id: 'time-window',
            type: 'inside',
            xAxisIndex: 0,
            filterMode: 'none',
            zoomOnMouseWheel: 'ctrl',
            moveOnMouseWheel: false,
            moveOnMouseMove: false,
            preventDefaultMouseMove: false,
            ...(timeline
              ? timeWindow
                ? {
                    startValue: Date.parse(timeWindow.start),
                    endValue: Date.parse(timeWindow.end),
                    rangeMode: ['value', 'value'],
                  }
                : { start: 0, end: 100, rangeMode: ['percent', 'percent'] }
              : {}),
          },
          ...(timeline
            ? [
                {
                  id: 'time-slider',
                  type: 'slider',
                  xAxisIndex: 0,
                  filterMode: 'none',
                  height: 22,
                  bottom: 6,
                  borderColor: line,
                  borderRadius: 5,
                  backgroundColor: subtle,
                  fillerColor: echarts.color.modifyAlpha(palette[0], 0.12),
                  handleSize: '90%',
                  handleStyle: {
                    color: surface,
                    borderColor: muted,
                    borderWidth: 1,
                  },
                  dataBackground: {
                    lineStyle: { color: muted, opacity: 0.35 },
                    areaStyle: { color: muted, opacity: 0.06 },
                  },
                  selectedDataBackground: {
                    lineStyle: { color: palette[0], opacity: 0.5 },
                    areaStyle: { color: palette[0], opacity: 0.1 },
                  },
                  showDetail: false,
                  textStyle: { color: muted },
                  brushSelect: true,
                  realtime: false,
                },
              ]
            : []),
        ],
        series,
      },
      { replaceMerge: ['series'] }
    )
  }, [
    history,
    measures,
    unit,
    scale,
    names,
    seriesBy,
    theme,
    colors,
    group,
    compact,
    timeline,
    timeWindow,
    annotations,
    showLegend,
    focusedAsset,
  ])
  return (
    <div
      ref={host}
      className={styles.chart}
      data-timeline={timeline || undefined}
      role="img"
      aria-label={measures.map((item) => titles[item]).join('、') + '历史曲线'}
    />
  )
}
