import { ActionIcon, Progress, SegmentedControl, Switch } from '@mantine/core'
import { useQuery } from '@tanstack/react-query'
import { Activity, Cpu, Gauge, HardDrive, Maximize2, MemoryStick, Minimize2, RefreshCw } from 'lucide-react'
import { useState, type ReactNode, type CSSProperties } from 'react'
import { useWorkspaceApi, type Asset, type Node } from '../../api/client'
import { Empty, ErrorMessage, Loading } from '../../foundation/Feedback'
import { MetricChart } from '../../foundation/MetricChart'
import { byteUnit, reading } from '../../foundation/format'
import { latestMetric, type History, type Metric } from '../../foundation/metrics'
import { metricRanges, useMetricWindow } from '../../foundation/useMetricWindow'
import styles from './PerformanceWorkspace.module.css'

type Scope =
  | { environmentId: string; assets: Asset[]; onAsset: (asset: Asset) => void }
  | { node?: Node; nodes?: Node[]; onNode?: (node: Node) => void }
type Plot = 'cpu' | 'memory' | 'network' | 'disk' | 'storage'
const plots: Record<
  Plot,
  {
    title: string
    unit: string
    scale: number
    measures: Metric[]
    icon: ReactNode
    color: string
  }
> = {
  cpu: {
    title: 'CPU',
    unit: '核',
    scale: 1,
    measures: ['cpu'],
    icon: <Cpu size={17} />,
    color: 'var(--chart-1)',
  },
  memory: {
    title: '内存',
    unit: 'GiB',
    scale: 2 ** 30,
    measures: ['memory'],
    icon: <MemoryStick size={17} />,
    color: 'var(--chart-2)',
  },
  network: {
    title: '资产网络',
    unit: 'B/s',
    scale: 1,
    measures: ['receive', 'transmit'],
    icon: <Activity size={17} />,
    color: 'var(--chart-4)',
  },
  disk: {
    title: '磁盘 I/O',
    unit: 'B/s',
    scale: 1,
    measures: ['disk_read', 'disk_write'],
    icon: <HardDrive size={17} />,
    color: 'var(--chart-3)',
  },
  storage: {
    title: '节点工作盘',
    unit: 'GiB',
    scale: 2 ** 30,
    measures: ['disk_used'],
    icon: <HardDrive size={17} />,
    color: 'var(--chart-6)',
  },
}

export default function PerformanceWorkspace(props: Scope) {
  const api = useWorkspaceApi()
  const environmentId = 'environmentId' in props ? props.environmentId : undefined
  const node = 'node' in props ? props.node : undefined
  const { range, setRange, live, setLive } = useMetricWindow()
  const [expanded, setExpanded] = useState<Plot>()
  const [ranking, setRanking] = useState<'cpu' | 'memory'>('cpu')
  const query = useQuery({
    queryKey: ['resource-history', environmentId ?? 'nodes', node?.id ?? 'all', range],
    queryFn: ({ signal }) =>
      environmentId
        ? api.environmentMetrics(environmentId, Number(range), signal)
        : api.nodeMetrics(Number(range), node?.id, signal, true),
    refetchInterval: live ? 10_000 : false,
    refetchOnWindowFocus: live,
  })
  const snapshot = useQuery({
    queryKey: ['resource-snapshot', environmentId ?? 'nodes', node?.id ?? 'all'],
    queryFn: ({ signal }) =>
      environmentId ? api.metrics(environmentId, '', 60, signal) : api.nodeMetrics(60, node?.id, signal),
    refetchInterval: live ? 10_000 : false,
    refetchOnWindowFocus: live,
  })
  const history = query.data
  const current = snapshot.isError ? undefined : snapshot.data
  const total = (metric: Metric) => latestMetric(current, metric)
  const assets = 'assets' in props ? props.assets : []
  const choices: Plot[] = environmentId ? ['cpu', 'memory', 'network', 'disk'] : ['cpu', 'memory', 'disk', 'storage']
  const cpuCapacity = environmentId
    ? assets.reduce((sum, asset) => sum + asset.resources.cpu, 0)
    : total('cpu_capacity')
  const memoryCapacity = environmentId
    ? assets.reduce((sum, asset) => sum + asset.resources.memoryMiB * 2 ** 20, 0)
    : total('memory_capacity')
  const capacity = (key: Plot) =>
    key === 'cpu'
      ? cpuCapacity
      : key === 'memory'
        ? memoryCapacity
        : key === 'storage'
          ? total('disk_capacity')
          : undefined
  const units = (key: Plot) => {
    const item = plots[key]
    if (key !== 'disk' && key !== 'network') return item
    let peak = Math.max(...item.measures.map((metric) => total(metric) ?? 0))
    for (const series of history?.series ?? [])
      if (item.measures.includes(series.metric)) for (const point of series.points) peak = Math.max(peak, point.value)
    return { ...item, ...byteUnit(peak, true) }
  }
  const byAsset = new Map<string, History['series']>()
  for (const series of current?.series ?? [])
    if (series.metric === 'cpu' || series.metric === 'memory') {
      const rows = byAsset.get(series.assetId) ?? []
      rows.push(series)
      byAsset.set(series.assetId, rows)
    }
  const ranked = (
    environmentId
      ? assets.map((asset) => {
          const detail = current && {
            ...current,
            series: byAsset.get(asset.id) ?? [],
          }
          return {
            id: asset.id,
            name: asset.name,
            cpu: latestMetric(detail, 'cpu'),
            memory: latestMetric(detail, 'memory'),
            cpuCapacity: asset.resources.cpu,
            memoryCapacity: asset.resources.memoryMiB * 2 ** 20,
            select: () => 'onAsset' in props && props.onAsset(asset),
          }
        })
      : ('nodes' in props ? (props.nodes ?? []) : []).map((item) => ({
          id: item.id,
          name: item.name,
          cpu: latestMetric(current, 'cpu', item.id),
          memory: latestMetric(current, 'memory', item.id),
          cpuCapacity: latestMetric(current, 'cpu_capacity', item.id),
          memoryCapacity: latestMetric(current, 'memory_capacity', item.id),
          select: () => 'onNode' in props && props.onNode?.(item),
        }))
  ).sort((a, b) => (b[ranking] ?? -1) - (a[ranking] ?? -1))
  const failures = [query.error, snapshot.error].filter(
    (error, i, all) => error && all.findIndex((value) => value?.message === error.message) === i
  )
  return (
    <section className={styles.workspace} aria-label={environmentId ? '环境性能' : node ? '节点性能' : '集群性能'}>
      <header className={styles.toolbar}>
        <div className={styles.heading}>
          <Gauge size={18} />
          <strong>{environmentId ? '环境性能' : node ? '节点性能' : '集群性能'}</strong>
          <span className={styles.live} data-live={live}>
            {live ? '实时' : '手动'}
          </span>
        </div>
        <div className={styles.controls}>
          <SegmentedControl size="xs" aria-label="性能时间范围" value={range} onChange={setRange} data={metricRanges} />
          <Switch
            size="xs"
            aria-label="实时刷新"
            checked={live}
            onChange={(event) => setLive(event.currentTarget.checked)}
          />
          <ActionIcon
            variant="subtle"
            aria-label="刷新性能"
            loading={query.isFetching || snapshot.isFetching}
            onClick={() => {
              void query.refetch()
              void snapshot.refetch()
            }}
          >
            <RefreshCw size={15} />
          </ActionIcon>
        </div>
      </header>
      {failures.map((error) => (
        <ErrorMessage key={error!.message} error={error} />
      ))}
      {query.isPending ? (
        <Loading />
      ) : query.error ? null : !history?.series.some((series) => series.points.length) ? (
        <Empty icon={<Gauge size={32} />} title="暂无性能记录" />
      ) : (
        <>
          <div className={styles.metrics}>
            {choices.map((key) => {
              const item = units(key),
                used = total(item.measures[0]),
                limit = capacity(key)
              return (
                <button
                  key={key}
                  className={styles.metric}
                  aria-pressed={expanded === key}
                  style={{ '--metric-color': item.color } as CSSProperties}
                  onClick={() => setExpanded(expanded === key ? undefined : key)}
                >
                  <span className={styles.metricLabel}>
                    {item.icon}
                    {item.title}
                  </span>
                  <span className={styles.metricValue}>
                    {reading(used, item.scale)}
                    <small>{item.unit}</small>
                  </span>
                  {limit !== undefined ? (
                    <>
                      <Progress
                        size={3}
                        color={item.color}
                        radius="xl"
                        value={used === undefined || !limit ? 0 : Math.min(100, (used / limit) * 100)}
                      />
                      <span className={styles.secondary}>
                        {environmentId ? '已配置' : '总容量'} {reading(limit, item.scale)} {item.unit}
                      </span>
                    </>
                  ) : (
                    <span className={styles.secondary}>
                      {key === 'network' ? '发送' : '写入'} {reading(total(item.measures[1]), item.scale)} {item.unit}
                    </span>
                  )}
                </button>
              )
            })}
          </div>
          <div className={styles.details}>
            <div className={styles.charts} data-expanded={Boolean(expanded)}>
              {choices
                .filter((key) => !expanded || expanded === key)
                .map((key) => {
                  const item = units(key)
                  return (
                    <section key={key} className={styles.trend} aria-label={item.title + '趋势'}>
                      <div className={styles.chartHeading}>
                        <span className={styles.chartMark} style={{ background: item.color }} />
                        <h3>{!environmentId && key === 'disk' ? '工作盘 I/O' : item.title}</h3>
                        <span>{item.unit}</span>
                        <ActionIcon
                          size="sm"
                          variant="subtle"
                          color="gray"
                          aria-label={(expanded ? '收起' : '放大') + item.title}
                          onClick={() => setExpanded(expanded ? undefined : key)}
                        >
                          {expanded ? <Minimize2 size={14} /> : <Maximize2 size={14} />}
                        </ActionIcon>
                      </div>
                      <MetricChart
                        history={history}
                        measures={item.measures}
                        unit={item.unit}
                        scale={item.scale}
                        seriesBy="metric"
                        colors={[item.color, 'var(--chart-6)']}
                      />
                    </section>
                  )
                })}
            </div>
            {ranked.length > 0 && (
              <aside className={styles.ranking}>
                <div className={styles.chartHeading}>
                  <h3>{environmentId ? '资产负载' : '节点负载'}</h3>
                  <SegmentedControl
                    size="xs"
                    aria-label="负载排序"
                    value={ranking}
                    onChange={(value) => setRanking(value as 'cpu' | 'memory')}
                    data={[
                      { value: 'cpu', label: 'CPU' },
                      { value: 'memory', label: '内存' },
                    ]}
                  />
                </div>
                <div className={styles.assetList}>
                  {ranked.map((item) => {
                    const used = item[ranking],
                      limit = ranking === 'cpu' ? item.cpuCapacity : item.memoryCapacity
                    return (
                      <button className={styles.asset} key={item.id} onClick={item.select}>
                        <div className={styles.assetHeading}>
                          <span>{item.name}</span>
                          <strong>
                            {reading(used, ranking === 'cpu' ? 1 : 2 ** 30)}{' '}
                            <small>{ranking === 'cpu' ? '核' : 'GiB'}</small>
                          </strong>
                        </div>
                        <Progress
                          size={3}
                          radius="xl"
                          color={plots[ranking].color}
                          value={used === undefined || !limit ? 0 : Math.min(100, (used / limit) * 100)}
                        />
                      </button>
                    )
                  })}
                </div>
              </aside>
            )}
          </div>
        </>
      )}
    </section>
  )
}
