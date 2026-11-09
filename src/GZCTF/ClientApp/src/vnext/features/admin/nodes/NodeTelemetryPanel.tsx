import { Activity, Boxes, Cpu, MemoryStick, Monitor } from 'lucide-react'
import { ActionButton, InlineFeedback } from '../../../shared/Interaction'
import { NodeHistoryChart } from './NodeHistoryChart'
import { useNodeMetricHistory } from './useNodeMetricHistory'
import type { NodeSummary } from '../api'
import { ResourceMeter } from '../shared/AdminWorkbench'
import styles from './NodeTelemetryPanel.module.css'
import {
  liveTelemetryNodes,
  telemetrySample,
} from './nodeTelemetry'

const percent = (value: number | undefined) => (value === undefined ? '—' : `${value.toFixed(1)}%`)
export function NodeTelemetryPanel({
  nodes,
  loading = false,
  failed = false,
  nodeId,
}: {
  nodes: NodeSummary[]
  loading?: boolean
  failed?: boolean
  nodeId?: string
}) {
  const current = failed ? null : telemetrySample(nodes)
  const live = failed ? [] : liveTelemetryNodes(nodes)
  const history = useNodeMetricHistory(nodeId)
  const dockerUsed = nodes.reduce((sum, node) => sum + node.allocatedContainers + node.reservedContainers, 0)
  const dockerMax = nodes.reduce((sum, node) => sum + node.maxContainers, 0)
  const vmUsed = nodes.reduce((sum, node) => sum + node.allocatedVms + node.reservedVms, 0)
  const vmMax = nodes.reduce((sum, node) => sum + node.maxVms, 0)
  const missing = loading ? '读取中' : failed ? '读取失败' : '暂无读数'

  return (
    <section aria-label="节点实时监控" aria-busy={loading} className={styles.telemetry}>
      <div className={styles.cards}>
        <article className={styles.card}>
          <span>
            <Cpu size={16} /> CPU 使用率
          </span>
          <strong>{current ? percent(current.cpu) : '—'}</strong>
          <small>{current ? `${live.length} 个节点平均` : missing}</small>
          <ResourceMeter
            label="CPU 使用率"
            max={100}
            value={current?.cpu ?? 0}
            detail={current ? percent(current.cpu) : '未上报'}
          />
        </article>
        <article className={styles.card} data-kind="memory">
          <span>
            <MemoryStick size={16} /> 内存使用率
          </span>
          <strong>{current ? percent(current.memory) : '—'}</strong>
          <small>{current ? `${live.length} / ${nodes.length} 节点` : missing}</small>
          <ResourceMeter
            label="内存使用率"
            max={100}
            value={current?.memory ?? 0}
            detail={current ? percent(current.memory) : '未上报'}
          />
        </article>
        <article className={styles.card}>
          <span>
            <Boxes size={16} /> Docker 槽位
          </span>
          <strong>
            {failed || loading ? '—' : dockerUsed}
            <em> / {failed || loading ? '—' : dockerMax}</em>
          </strong>
          <small>已分配 + 已预留</small>
          <ResourceMeter label="Docker 槽位" max={dockerMax} value={dockerUsed} />
        </article>
        <article className={styles.card}>
          <span>
            <Monitor size={16} /> VM 槽位
          </span>
          <strong>
            {failed || loading ? '—' : vmUsed}
            <em> / {failed || loading ? '—' : vmMax}</em>
          </strong>
          <small>已分配 + 已预留</small>
          <ResourceMeter label="VM 槽位" max={vmMax} value={vmUsed} />
        </article>
      </div>
      <article className={styles.trend}>
        <header>
          <div>
            <h2>
              <Activity size={17} /> 负载趋势
            </h2>
          </div>
          <div className={styles.legend}>
            <span>
              CPU <b>{percent(current?.cpu)}</b>
            </span>
            <span>
              内存 <b>{percent(current?.memory)}</b>
            </span>
          </div>
        </header>
        <div className={styles.rangeControls} role="group" aria-label="历史时间范围">
          {(['1h', '24h', '7d'] as const).map((range, index) => (
            <ActionButton key={range} aria-pressed={history.range === range}
              tone={history.range === range ? 'primary' : undefined}
              onClick={() => history.setRange(range)} type="button">
              {['近 1 小时', '近 24 小时', '近 7 天'][index]}
            </ActionButton>
          ))}
          <ActionButton onClick={() => history.mutate()} loading={history.isValidating} type="button">刷新趋势</ActionButton>
        </div>
        {history.error ? <InlineFeedback tone="danger">历史负载读取失败，请重试。</InlineFeedback> :
          history.isLoading ? <p className={styles.emptyHistory} role="status">正在加载历史负载…</p> :
          history.data?.points.some(point => point.cpuLoad !== null || point.memoryLoad !== null) ?
            <NodeHistoryChart history={history.data} /> :
            <p className={styles.emptyHistory}>该时段暂无记录</p>}
      </article>
    </section>
  )
}
