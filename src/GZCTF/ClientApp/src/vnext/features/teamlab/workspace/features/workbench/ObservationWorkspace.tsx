import { ActionIcon, SegmentedControl, Switch } from '@mantine/core'
import { useQuery } from '@tanstack/react-query'
import { ArrowLeft, Activity, RefreshCw } from 'lucide-react'
import { useWorkspaceApi, type Asset, type Network, type Schema } from '../../api/client'
import { Empty, ErrorMessage, Loading } from '../../foundation/Feedback'
import { MetricChart } from '../../foundation/MetricChart'
import { byteUnit, reading } from '../../foundation/format'
import { latestMetric } from '../../foundation/metrics'
import { metricRanges, useMetricWindow } from '../../foundation/useMetricWindow'
import styles from './ObservationWorkspace.module.css'

type Metric = Schema<'MetricSeries'>['metric']
const plots = [
  { title: 'CPU', unit: '核', measures: ['cpu'], scale: 1 },
  { title: '内存', unit: 'GiB', measures: ['memory'], scale: 2 ** 30 },
  {
    title: '网络',
    unit: 'MiB/s',
    measures: ['receive', 'transmit'],
    scale: 2 ** 20,
  },
  {
    title: '磁盘',
    unit: 'MiB/s',
    measures: ['disk_read', 'disk_write'],
    scale: 2 ** 20,
  },
] satisfies {
  title: string
  unit: string
  measures: Metric[]
  scale: number
}[]

export default function ObservationWorkspace({
  environmentId,
  asset,
  networks,
  onClose,
  backLabel = '返回拓扑',
}: {
  environmentId: string
  asset: Asset
  networks: Network[]
  onClose: () => void
  backLabel?: string
}) {
  const api = useWorkspaceApi()
  const { range, setRange, live, setLive } = useMetricWindow()
  const query = useQuery({
    queryKey: ['metrics', environmentId, asset.id, range],
    queryFn: ({ signal }) => api.metrics(environmentId, asset.id, Number(range), signal),
    refetchInterval: live ? 10_000 : false,
    refetchOnWindowFocus: live,
  })
  const snapshot = useQuery({
    queryKey: ['metrics', environmentId, asset.id, '60'],
    queryFn: ({ signal }) => api.metrics(environmentId, asset.id, 60, signal),
    refetchInterval: live ? 10_000 : false,
    refetchOnWindowFocus: live,
  })
  const current = snapshot.isError ? undefined : snapshot.data
  const interfaces = new Map(
    asset.interfaces.map((iface, index) => [
      iface.id,
      (networks.find((network) => network.id === iface.networkId)?.name ?? '网卡') + ' · 网卡' + (index + 1),
    ])
  )
  const history = query.data
  return (
    <div className={styles.workspace} aria-label="资源观察">
      <header className={styles.toolbar}>
        <div className={styles.title}>
          <ActionIcon variant="subtle" color="gray" aria-label={backLabel} onClick={onClose}>
            <ArrowLeft size={18} />
          </ActionIcon>
          <strong>{asset.name}</strong>
          <span>资源</span>
        </div>
        <div className={styles.controls}>
          <SegmentedControl size="xs" value={range} onChange={setRange} data={metricRanges} aria-label="时间范围" />
          <Switch
            size="xs"
            label="实时"
            aria-label="实时刷新"
            checked={live}
            onChange={(event) => setLive(event.currentTarget.checked)}
          />
          <ActionIcon
            variant="subtle"
            color="gray"
            aria-label="刷新资源数据"
            loading={query.isFetching || snapshot.isFetching}
            onClick={() => {
              void query.refetch()
              void snapshot.refetch()
            }}
          >
            <RefreshCw size={16} />
          </ActionIcon>
        </div>
      </header>
      <ErrorMessage error={query.error ?? snapshot.error} />
      {query.isLoading ? (
        <Loading />
      ) : (
        !query.error &&
        history &&
        (history.series.some((item) => item.points.length) ? (
          <>
            <div className={styles.grid}>
              {plots.map((plot) => {
                let peak = 0
                for (const series of history.series)
                  if ((plot.measures as Metric[]).includes(series.metric))
                    for (const point of series.points) peak = Math.max(peak, point.value)
                const unit = plot.unit === 'MiB/s' ? byteUnit(peak, true) : plot
                return (
                  <section key={plot.title} className={styles.plot} aria-label={plot.title + '指标'}>
                    <h3>
                      {plot.title}
                      <span>{unit.unit}</span>
                    </h3>
                    <MetricChart
                      history={history}
                      measures={plot.measures}
                      unit={unit.unit}
                      scale={unit.scale}
                      names={interfaces}
                    />
                  </section>
                )
              })}
            </div>
            <div className={styles.interfaceStats}>
              <table>
                <caption>接口速率</caption>
                <thead>
                  <tr>
                    <th>网卡</th>
                    <th>接收包/s</th>
                    <th>发送包/s</th>
                    <th>接收丢包/s</th>
                    <th>发送丢包/s</th>
                  </tr>
                </thead>
                <tbody>
                  {asset.interfaces.map((iface) => {
                    const detail = current && {
                      ...current,
                      series: current.series.filter((item) => item.interfaceId === iface.id),
                    }
                    const value = (metric: Metric) => reading(latestMetric(detail, metric))
                    return (
                      <tr key={iface.id}>
                        <td>{interfaces.get(iface.id)}</td>
                        <td>{value('receive_packets')}</td>
                        <td>{value('transmit_packets')}</td>
                        <td>{value('receive_drops')}</td>
                        <td>{value('transmit_drops')}</td>
                      </tr>
                    )
                  })}
                </tbody>
              </table>
            </div>
          </>
        ) : (
          <Empty icon={<Activity size={32} />} title="暂无资源记录" />
        ))
      )}
    </div>
  )
}
