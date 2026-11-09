import { Activity, Boxes, Cpu, MemoryStick, Monitor } from 'lucide-react'
import { useEffect, useState } from 'react'
import type { NodeSummary } from '../api'
import { ResourceMeter } from '../shared/AdminWorkbench'
import styles from './NodeTelemetryPanel.module.css'
import {
  appendTelemetrySample,
  liveTelemetryNodes,
  telemetryPoints,
  telemetrySample,
  type NodeTelemetrySample,
} from './nodeTelemetry'

const percent = (value: number | undefined) => (value === undefined ? '—' : `${value.toFixed(1)}%`)
const time = (value: number) =>
  new Intl.DateTimeFormat('zh-CN', { hour: '2-digit', minute: '2-digit', second: '2-digit', hour12: false }).format(
    value
  )

export function NodeTelemetryPanel({
  nodes,
  loading = false,
  failed = false,
}: {
  nodes: NodeSummary[]
  loading?: boolean
  failed?: boolean
}) {
  const current = failed ? null : telemetrySample(nodes)
  const live = failed ? [] : liveTelemetryNodes(nodes)
  const [samples, setSamples] = useState<NodeTelemetrySample[]>([])
  const sampleTime = current?.time
  const coverage = current?.coverage
  const cpu = current?.cpu
  const memory = current?.memory
  useEffect(() => {
    const next =
      sampleTime === undefined || cpu === undefined || memory === undefined || coverage === undefined
        ? null
        : { time: sampleTime, cpu, memory, coverage }
    setSamples((previous) => appendTelemetrySample(previous, next))
  }, [sampleTime, coverage, cpu, memory])

  const dockerUsed = nodes.reduce((sum, node) => sum + node.allocatedContainers + node.reservedContainers, 0)
  const dockerMax = nodes.reduce((sum, node) => sum + node.maxContainers, 0)
  const vmUsed = nodes.reduce((sum, node) => sum + node.allocatedVms + node.reservedVms, 0)
  const vmMax = nodes.reduce((sum, node) => sum + node.maxVms, 0)
  const history = current && samples.at(-1)?.coverage === current.coverage ? samples : []
  const missing = loading ? '读取中' : failed ? '读取失败' : '暂无新鲜心跳'

  return (
    <section aria-label="节点实时监控" aria-busy={loading} className={styles.telemetry}>
      <div className={styles.cards}>
        <article className={styles.card}>
          <span>
            <Cpu size={16} /> CPU 使用率
          </span>
          <strong>{current ? percent(current.cpu) : '—'}</strong>
          <small>{current ? `${live.length} 个节点平均 · 心跳实测` : missing}</small>
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
          <small>{current ? `${live.length} / ${nodes.length} 节点提供有效读数` : missing}</small>
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
          <small>已分配 + 已预留 · 全部节点配置</small>
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
          <small>已分配 + 已预留 · 全部节点配置</small>
          <ResourceMeter label="VM 槽位" max={vmMax} value={vmUsed} />
        </article>
      </div>
      <article className={styles.trend}>
        <header>
          <div>
            <h2>
              <Activity size={17} /> 负载趋势
            </h2>
            <p>本次打开后收到的心跳 · 最多保留 10 分钟 · 节点覆盖变化后重新采样</p>
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
        <div className={styles.chart}>
          <div className={styles.axis}>
            <span>100%</span>
            <span>50%</span>
            <span>0%</span>
          </div>
          <svg
            aria-label={`CPU 与内存使用率，${history.length} 个实际心跳样本`}
            role="img"
            viewBox="0 0 720 180"
            preserveAspectRatio="none"
          >
            {[20, 90, 160].map((y) => (
              <line className={styles.gridLine} key={y} x1="20" x2="700" y1={y} y2={y} />
            ))}
            {history.length > 1 ? (
              <>
                <polyline className={styles.cpuLine} points={telemetryPoints(history, 'cpu')} />
                <polyline className={styles.memoryLine} points={telemetryPoints(history, 'memory')} />
              </>
            ) : null}
            {history.length === 1 ? (
              <>
                <circle className={styles.cpuDot} cx="20" cy={160 - history[0].cpu * 1.4} r="3" />
                <circle className={styles.memoryDot} cx="20" cy={160 - history[0].memory * 1.4} r="3" />
              </>
            ) : null}
          </svg>
          {history.length < 2 ? (
            <p className={styles.waiting}>{current ? '已收到首个读数，等待下一次心跳形成趋势' : missing}</p>
          ) : null}
        </div>
        <footer>
          <span>{history.length ? time(history[0].time) : '—'}</span>
          <span>{history.length} 个样本</span>
          <span>{history.length ? time(history[history.length - 1].time) : '—'}</span>
        </footer>
      </article>
    </section>
  )
}
