import { CartesianGrid, Line, LineChart, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import type { NodeMetricHistory } from '../api/nodeMetricHistoryApi'
import styles from './NodeTelemetryPanel.module.css'

const dateTime = (value: number) => new Intl.DateTimeFormat('zh-CN', {
  month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', hour12: false,
}).format(value)

export function NodeHistoryChart({ history }: { history: NodeMetricHistory }) {
  const points = history.points.map(point => ({
    time: point.timestamp,
    cpu: point.cpuLoad === null ? null : point.cpuLoad * 100,
    memory: point.memoryLoad === null ? null : point.memoryLoad * 100,
    nodeCount: point.nodeCount,
  }))
  return <>
    <div className={styles.chart} aria-label="CPU 与内存历史负载">
      <ResponsiveContainer width="100%" height="100%" minWidth={0}>
        <LineChart data={points} accessibilityLayer margin={{ top: 12, right: 12, bottom: 12, left: 0 }}>
          <CartesianGrid stroke="var(--yn-color-border)" strokeDasharray="4 5" vertical={false} />
          <XAxis dataKey="time" type="number" domain={[history.fromUtc, history.toUtc]} scale="time"
            tickFormatter={dateTime} minTickGap={48} stroke="var(--yn-color-muted)" tick={{ fontSize: 11 }} />
          <YAxis domain={[0, 100]} ticks={[0, 50, 100]} tickFormatter={value => `${value}%`}
            width={45} stroke="var(--yn-color-muted)" tick={{ fontSize: 11 }} />
          <Tooltip content={({ active, payload, label }) => active && payload?.length ?
            <div className={styles.tooltip}>
              <strong>{dateTime(Number(label))}</strong>
              {payload.map(item => <span key={item.dataKey}>{item.name} {Number(item.value).toFixed(1)}%</span>)}
              <span>{payload[0].payload.nodeCount} 个节点</span>
            </div> : null} />
          <Line dataKey="cpu" name="CPU" stroke="var(--yn-color-brand)" strokeWidth={2}
            type="linear" dot={false} connectNulls={false} isAnimationActive={false} />
          <Line dataKey="memory" name="内存" stroke="var(--yn-color-info)" strokeWidth={2} strokeDasharray="6 3"
            type="linear" dot={false} connectNulls={false} isAnimationActive={false} />
        </LineChart>
      </ResponsiveContainer>
    </div>
    <details className={styles.historyTable}>
      <summary>查看数据</summary>
      <div><table>
        <thead><tr><th>时间</th><th>CPU</th><th>内存</th><th>节点数</th></tr></thead>
        <tbody>{points.map(point => <tr key={point.time}>
          <td>{dateTime(point.time)}</td><td>{point.cpu === null ? '—' : `${point.cpu.toFixed(1)}%`}</td>
          <td>{point.memory === null ? '—' : `${point.memory.toFixed(1)}%`}</td><td>{point.nodeCount}</td>
        </tr>)}</tbody>
      </table></div>
    </details>
  </>
}
