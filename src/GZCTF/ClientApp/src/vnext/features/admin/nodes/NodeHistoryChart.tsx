import { useCallback, useEffect, useRef, useState, type KeyboardEvent } from 'react'
import { CartesianGrid, Dot, Line, LineChart, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import type { NodeMetricHistory } from '../api/nodeMetricHistoryApi'
import styles from './NodeTelemetryPanel.module.css'

const historyDateTime = new Intl.DateTimeFormat('zh-CN', {
  month: '2-digit',
  day: '2-digit',
  hour: '2-digit',
  minute: '2-digit',
  hour12: false,
})
const dateTime = (value: number) => historyDateTime.format(value)

type TimeWindow = [start: number, end: number]
const margin = { top: 12, right: 12, bottom: 12, left: 0 }
const yAxisWidth = 40
const xAxisHeight = 30

function fitWindow(start: number, duration: number, from: number, to: number): TimeWindow {
  const left = Math.max(from, Math.min(start, to - duration))
  return [left, left + duration]
}

function IsolatedSampleDot({
  cx,
  cy,
  stroke,
  index = 0,
  points = [],
}: {
  cx?: number
  cy?: number
  stroke?: string
  index?: number
  points?: { value: number | null }[]
}) {
  if (points[index]?.value == null || points[index - 1]?.value != null || points[index + 1]?.value != null) {
    return null
  }
  return <Dot cx={cx} cy={cy} r={2.5} fill={stroke} />
}

export function NodeHistoryChart({ history }: { history: NodeMetricHistory }) {
  const chart = useRef<HTMLDivElement>(null)
  const [zoomed, setZoomed] = useState<TimeWindow | null>(null)
  const { fromUtc, toUtc, intervalSeconds } = history
  const fullDuration = toUtc - fromUtc
  const bucketSize = intervalSeconds * 1000
  const minDuration = Math.min(fullDuration, bucketSize * 4)
  const [start, end] = zoomed
    ? fitWindow(zoomed[0], Math.min(zoomed[1] - zoomed[0], fullDuration), fromUtc, toUtc)
    : [fromUtc, toUtc]

  const zoom = useCallback(
    (factor: number, anchor: number) => {
      setZoomed((previous) => {
        const [left, right] = previous
          ? fitWindow(previous[0], Math.min(previous[1] - previous[0], fullDuration), fromUtc, toUtc)
          : [fromUtc, toUtc]
        const duration = right - left
        const nextDuration = Math.round(Math.max(minDuration, duration * factor))
        if (nextDuration >= fullDuration) return null
        const nextStart = left + Math.round((duration - nextDuration) * anchor)
        return fitWindow(nextStart, nextDuration, fromUtc, toUtc)
      })
    },
    [fromUtc, toUtc, fullDuration, minDuration]
  )

  useEffect(() => {
    const element = chart.current
    if (!element) return
    const onWheel = (event: WheelEvent) => {
      const mode = event.deltaMode
      if (event.ctrlKey || event.metaKey || Math.abs(event.deltaY) <= Math.abs(event.deltaX)) return
      const bounds = element.getBoundingClientRect()
      const width = bounds.width - yAxisWidth - margin.left - margin.right
      const x = event.clientX - bounds.left - margin.left - yAxisWidth
      const y = event.clientY - bounds.top
      if (width <= 0 || x < 0 || x > width || y < margin.top || y > bounds.height - xAxisHeight - margin.bottom) return

      event.preventDefault()
      const unit = mode === 1 ? 16 : mode === 2 ? bounds.height : 1
      const delta = Math.max(-200, Math.min(200, event.deltaY * unit))
      zoom(Math.exp(delta * 0.002), x / width)
    }
    element.addEventListener('wheel', onWheel, { passive: false })
    return () => element.removeEventListener('wheel', onWheel)
  }, [zoom])

  const handleKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    if (event.ctrlKey || event.metaKey || event.altKey) return
    if (event.key === '+' || event.key === '=') zoom(0.8, 0.5)
    else if (event.key === '-') zoom(1.25, 0.5)
    else if (event.key === '0') setZoomed(null)
    else return
    event.preventDefault()
  }

  // Keep the neighboring buckets so lines still reach the edges of the clipped plot.
  const firstIndex = Math.max(0, Math.ceil((start - fromUtc) / bucketSize) - 1)
  const endIndex = Math.floor((end - fromUtc) / bucketSize) + 2
  const points = history.points.slice(firstIndex, endIndex).map((point) => ({
    time: point.timestamp,
    cpu: point.cpuLoad === null ? null : point.cpuLoad * 100,
    memory: point.memoryLoad === null ? null : point.memoryLoad * 100,
    nodeCount: point.nodeCount,
  }))
  return (
    <div ref={chart} className={styles.chart} onKeyDown={handleKeyDown}>
      <ResponsiveContainer width="100%" height="100%" minWidth={0}>
        <LineChart
          data={points}
          accessibilityLayer
          margin={margin}
          aria-label="CPU 与内存历史负载"
          aria-description="滚轮或加减键缩放时间轴，0 键恢复范围，左右方向键读取数值。"
          aria-keyshortcuts="+ - 0"
        >
          <CartesianGrid stroke="var(--yn-color-border)" strokeDasharray="4 5" vertical={false} />
          <XAxis
            dataKey="time"
            type="number"
            domain={[start, end]}
            scale="time"
            allowDataOverflow
            ticks={[0, 1, 2, 3].map((index) => start + ((end - start) * index) / 3)}
            height={xAxisHeight}
            className={styles.chartAxis}
            tickFormatter={dateTime}
            minTickGap={20}
            tickLine={false}
            axisLine={{ stroke: 'var(--yn-color-border)' }}
            stroke="var(--yn-color-muted)"
          />
          <YAxis
            domain={[0, 100]}
            ticks={[0, 50, 100]}
            tickFormatter={(value) => `${value}%`}
            className={styles.chartAxis}
            width={yAxisWidth}
            axisLine={false}
            tickLine={false}
            stroke="var(--yn-color-muted)"
          />
          <Tooltip
            isAnimationActive={false}
            content={({ active, payload, label }) =>
              active && payload?.length ? (
                <div className={styles.tooltip} role="status">
                  <span className={styles.tooltipMeta}>{dateTime(Number(label))}</span>
                  {payload.map((item) => (
                    <span key={item.dataKey}>
                      {item.name} {Number(item.value).toFixed(1)}%
                    </span>
                  ))}
                  <span className={styles.tooltipMeta}>{payload[0].payload.nodeCount} 个节点</span>
                </div>
              ) : null
            }
          />
          <Line
            dataKey="cpu"
            name="CPU"
            stroke="var(--yn-color-brand)"
            strokeWidth={2}
            type="linear"
            dot={<IsolatedSampleDot />}
            activeDot={{ r: 3 }}
            connectNulls={false}
            isAnimationActive={false}
          />
          <Line
            dataKey="memory"
            name="内存"
            stroke="var(--yn-color-info)"
            strokeWidth={2}
            strokeDasharray="6 3"
            type="linear"
            dot={<IsolatedSampleDot />}
            activeDot={{ r: 3 }}
            connectNulls={false}
            isAnimationActive={false}
          />
        </LineChart>
      </ResponsiveContainer>
    </div>
  )
}
