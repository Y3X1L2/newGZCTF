import { ActionIcon, Button, Menu, Modal, Select, TextInput, Tooltip, useComputedColorScheme } from '@mantine/core'
import { useDebouncedValue } from '@mantine/hooks'
import { useInfiniteQuery, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  ArrowLeft,
  Check,
  Download,
  Ellipsis,
  Filter,
  Maximize,
  Minimize,
  Network,
  ShieldCheck,
  Pause,
  Play,
  X,
} from 'lucide-react'
import { useEffect, useMemo, useRef, useState, type CSSProperties } from 'react'
import { useWorkspaceApi, type Environment, type Schema } from '../../api/client'
import { byteUnit, clockTime, dateTime, reading } from '../../foundation/format'
import { useSearchParams, useWorkspaceRoute } from '../../routing'
import { PacketDetailDialog } from './PacketDetailDialog'
import { PacketList, RiskRecords } from './PacketWorkspace'
import { TrafficGraph, riskLevels, riskLevel, type TrafficSelection } from './TrafficGraph'
import { TrafficTimeline } from './TrafficTimeline'
import styles from './TrafficWorkspace.module.css'
import {
  buildTrafficLinks,
  selectionLabel,
  alertFilter,
  selectionNetwork,
  selectionAsset,
  selectionFilter,
  selectionMatches,
} from './trafficModel'
import { useTrafficObservation } from './useTrafficObservation'

const ranges = [
  { label: '15 分钟', value: '900' },
  { label: '1 小时', value: '3600' },
  { label: '24 小时', value: '86400' },
  { label: '7 天', value: '604800' },
]
const measures: Schema<'MetricSeries'>['metric'][] = ['receive', 'transmit']
const volume = (bytes: number) => {
  const unit = byteUnit(bytes)
  return `${reading(bytes, unit.scale)} ${unit.unit}`
}
const saveBlob = (blob: Blob, name: string) => {
  const url = URL.createObjectURL(blob),
    a = document.createElement('a')
  a.href = url
  a.download = name
  a.click()
  setTimeout(() => URL.revokeObjectURL(url), 1000)
}

export default function TrafficWorkspace({
  environment,
  onView,
}: {
  environment: Environment
  onView?: (view: Schema<'CanvasView'>) => Promise<unknown>
}) {
  const api = useWorkspaceApi()
  const scheme = useComputedColorScheme()
  const { element } = useWorkspaceRoute()
  const palette = useMemo(() => {
    const css = getComputedStyle(element.current!)
    return Array.from({ length: 8 }, (_, i) => css.getPropertyValue(`--chart-${i + 1}`).trim())
  }, [scheme, element])
  const host = useRef<HTMLElement>(null)
  const [fullscreen, setFullscreen] = useState(false)
  const [fullscreenError, setFullscreenError] = useState<string>()
  useEffect(() => {
    const changed = () => setFullscreen(document.fullscreenElement === host.current)
    document.addEventListener('fullscreenchange', changed)
    return () => document.removeEventListener('fullscreenchange', changed)
  }, [])
  const [range, setRange] = useState('3600'),
    [paused, setPaused] = useState(false),
    [input, setInput] = useState(''),
    [filter] = useDebouncedValue(input, 300)
  const [selection, setSelection] = useState<TrafficSelection>(),
    [level, setLevel] = useState<string>(),
    [protocol, setProtocol] = useState<string | null>(null)
  const [timeRange, setTimeRange] = useState<{ start: string; end: string }>()
  const [packetTimeWindow] = useDebouncedValue(timeRange, 250)
  const [selectedId, setSelectedId] = useState<string>()
  const [detailOpen, setDetailOpen] = useState(false)
  const [searchParams, setSearchParams] = useSearchParams()
  const requestedView = searchParams.get('traffic')
  const recordView = requestedView === 'packets' || requestedView === 'alerts' ? requestedView : 'overview'
  const setRecordView = (value: 'overview' | 'packets' | 'alerts') =>
    setSearchParams((params) => {
      if (value === 'overview') params.delete('traffic')
      else params.set('traffic', value)
      return params
    })
  const [chosenAlert, setChosenAlert] = useState<Schema<'SecurityAlert'>>()
  const [filesOpen, setFilesOpen] = useState(false),
    [rolesOpen, setRolesOpen] = useState(false),
    [roles, setRoles] = useState<Record<string, string>>({})
  const [graphTools, setGraphTools] = useState<HTMLSpanElement | null>(null)
  const graphVisible = recordView === 'overview'
  const packetsVisible = recordView === 'packets'
  const spec = environment.appliedSpec ?? environment.spec
  const client = useQueryClient()
  const names = useMemo(() => new Map(spec.assets.map((asset) => [asset.id, asset.name])), [spec.assets])
  const interfaceNames = useMemo(
    () =>
      new Map(
        spec.assets.flatMap((asset) =>
          asset.interfaces.map((iface, index) => [iface.id, `${asset.name} / 网卡 ${index + 1}`])
        )
      ),
    [spec.assets]
  )
  const groups = useMemo(
    () =>
      new Map<string, string>([
        ...spec.assets.map((asset): [string, string] => [
          asset.id,
          environment.view.roles?.[asset.id] ??
            spec.networks.find((network) => network.id === asset.interfaces[0]?.networkId)?.name ??
            '未分组',
        ]),
        ...spec.networks.flatMap((network): [string, string][] => [
          [network.id, network.name],
          ...(network.devices ?? []).map((device): [string, string] => [device.id, network.name]),
        ]),
        ...(spec.routers ?? []).map((router): [string, string] => [
          router.id,
          spec.networks.find((network) => network.id === router.interfaces[0]?.networkId)?.name ?? router.name,
        ]),
      ]),
    [spec, environment.view.roles]
  )
  const colors = useMemo(
    () => new Map([...new Set(groups.values())].map((role, index) => [role, palette[index % palette.length]])),
    [groups, palette]
  )
  const securityKey = ['security', environment.id, Number(range), level, protocol ?? undefined]
  const security = useInfiniteQuery({
    queryKey: securityKey,
    initialPageParam: undefined as number | undefined,
    queryFn: ({ signal, pageParam }) =>
      api.security(
        environment.id,
        {
          range: Number(range),
          level,
          protocol: protocol ?? undefined,
          before: pageParam,
        },
        signal
      ),
    refetchInterval: paused ? false : 5000,
    getNextPageParam: (page) => page.nextCursor,
  })
  const alerts = [
    ...new Map(security.data?.pages.flatMap((page) => page.alerts).map((alert) => [alert.id, alert]) ?? []).values(),
  ]
  const windowAlerts = alerts.filter(
    (alert) =>
      !timeRange ||
      (Date.parse(alert.lastSeen) >= Date.parse(timeRange.start) &&
        Date.parse(alert.firstSeen) <= Date.parse(timeRange.end))
  )
  const visibleAlerts = windowAlerts.filter((alert) => selectionMatches(spec, selection, alert))
  const displayFilter = [
    selectionFilter(spec, selection),
    protocol ? (protocol === 'ICMP' ? '(icmp || icmpv6)' : protocol.toLowerCase()) : '',
    chosenAlert ? alertFilter(chosenAlert) : '',
    filter ? `(${filter})` : '',
  ]
    .filter(Boolean)
    .join(' && ')
  const filters = useMemo(
    () => ({
      ...(chosenAlert
        ? {
            start: chosenAlert.firstSeen,
            end: new Date(Date.parse(chosenAlert.lastSeen) + 1000).toISOString(),
          }
        : packetTimeWindow),
      ...(displayFilter ? { filter: displayFilter } : {}),
      assetId: chosenAlert ? undefined : selectionAsset(spec, selection),
      interfaceId: chosenAlert?.interfaceId,
      networkId: chosenAlert?.networkId ?? selectionNetwork(spec, selection),
      limit: 100,
    }),
    [packetTimeWindow, displayFilter, chosenAlert, selection, spec]
  )
  const data = useTrafficObservation(environment, {
    range: Number(range),
    paused,
    filters,
    packets: packetsVisible || (graphVisible && Boolean(timeRange || filter)),
    history: graphVisible,
    graph: graphVisible,
  })
  const selected = selectedId
  const packet = useQuery({
    queryKey: ['environment-packet', environment.id, selected],
    queryFn: ({ signal }) => api.environmentPacket(environment.id, selected!, signal),
    enabled: detailOpen && !!selected,
    staleTime: Infinity,
  })
  const exportPackets = useMutation({
    mutationFn: () => api.exportEnvironmentPackets(environment.id, filters),
    onSuccess: (blob) => saveBlob(blob, `${environment.name}-traffic.pcapng`),
  })
  const saveRoles = useMutation({
    mutationFn: () =>
      onView!({
        ...environment.view,
        roles: Object.fromEntries(
          Object.entries(roles)
            .map(([id, role]) => [id, role.trim()])
            .filter(([, role]) => role)
        ),
      }),
    onSuccess: () => setRolesOpen(false),
  })
  const files = useQuery({
    queryKey: ['captures', environment.id],
    queryFn: ({ signal }) => api.captures(environment.id, signal),
    enabled: filesOpen,
  })
  const errors = [
    ...(fullscreenError ? [fullscreenError] : []),
    ...data.errors,
    ...[security.error, exportPackets.error, saveRoles.error, filesOpen ? files.error : null].flatMap((error) =>
      error ? [error.message] : []
    ),
    ...(data.retention?.error ? [data.retention.error] : []),
    ...(security.data?.pages[0]?.sensors
      .filter((sensor) => sensor.error)
      .map((sensor) => `${sensor.nodeName ?? '运行节点'}：${sensor.error}`) ?? []),
  ]
  const graphLinks = buildTrafficLinks(
    spec,
    (data.traffic.data?.flows ?? []).filter(
      (flow) => !protocol || (protocol === 'ICMP' ? flow.protocol.startsWith('ICMP') : flow.protocol === protocol)
    ),
    windowAlerts,
    timeRange || filter ? data.packets : undefined,
    Boolean(level)
  )
  const selectionName = selectionLabel(spec, selection, graphLinks)
  const select = (value?: TrafficSelection) => {
    setSelection(value)
    setChosenAlert(undefined)
    setSelectedId(undefined)
  }
  const peak =
    data.history.data?.series.reduce(
      (max, series) =>
        measures.includes(series.metric)
          ? series.points.reduce((peak, point) => Math.max(peak, point.value), max)
          : max,
      0
    ) ?? 0
  const unit = byteUnit(peak),
    retention = data.retention
  const counts = security.data?.pages[0]?.counts
  const sensors = security.data?.pages[0]?.sensors
  return (
    <section ref={host} className={styles.workspace} aria-label="流量分析" data-traffic-workspace>
      <header className={styles.toolbar}>
        {recordView !== 'overview' && (
          <Button
            size="compact-xs"
            variant="subtle"
            leftSection={<ArrowLeft size={15} />}
            onClick={() => setRecordView('overview')}
          >
            返回流量概览
          </Button>
        )}
        <Network size={16} />
        <strong>{recordView === 'overview' ? '流量分析' : recordView === 'packets' ? '报文分析' : '告警记录'}</strong>
        <Select
          aria-label="趋势范围"
          value={range}
          data={ranges}
          allowDeselect={false}
          className={styles.rangeSelect}
          size="xs"
          onChange={(value) => {
            if (value) setRange(value)
            setTimeRange(undefined)
            setSelectedId(undefined)
            setChosenAlert(undefined)
          }}
        />
        <Button
          size="compact-xs"
          variant={recordView === 'packets' ? 'filled' : 'default'}
          onClick={() => setRecordView('packets')}
        >
          查看报文
        </Button>
        <Button
          size="compact-xs"
          variant={recordView === 'alerts' ? 'filled' : 'default'}
          onClick={() => setRecordView('alerts')}
        >
          告警 {counts ? Object.values(counts).reduce((sum, count) => sum + count, 0) : '—'}
        </Button>
        <div className={styles.spacer} />
        <Button
          className={styles.liveToggle}
          aria-label={paused ? '继续实时' : '暂停'}
          size="compact-xs"
          variant={paused ? 'light' : 'subtle'}
          leftSection={paused ? <Play size={13} /> : <Pause size={13} />}
          onClick={() => {
            if (paused) {
              data.resetPaging()
              client.setQueryData(
                securityKey,
                security.data
                  ? {
                      ...security.data,
                      pages: security.data.pages.slice(0, 1),
                      pageParams: [undefined],
                    }
                  : undefined
              )
              setSelectedId(undefined)
            }
            setPaused(!paused)
          }}
        >
          <span className={styles.liveLabel}>{paused ? '继续实时' : '暂停'}</span>
        </Button>
        <ActionIcon
          variant="subtle"
          aria-label={fullscreen ? '退出全屏' : '全屏分析'}
          title={fullscreen ? '退出全屏' : '全屏分析'}
          onClick={() => {
            setFullscreenError(undefined)
            void (fullscreen ? document.exitFullscreen() : host.current!.requestFullscreen()).catch((error: Error) =>
              setFullscreenError(error.message)
            )
          }}
        >
          {fullscreen ? <Minimize size={16} /> : <Maximize size={16} />}
        </ActionIcon>
        <Menu position="bottom-end" withinPortal={false}>
          <Menu.Target>
            <ActionIcon variant="subtle" aria-label="分析选项">
              <Ellipsis size={17} />
            </ActionIcon>
          </Menu.Target>
          <Menu.Dropdown>
            <Menu.Item onClick={() => setFilesOpen(true)}>已保存抓包</Menu.Item>
            {onView && (
              <Menu.Item
                onClick={() => {
                  setRoles({ ...environment.view.roles })
                  setRolesOpen(true)
                }}
              >
                资产角色
              </Menu.Item>
            )}
          </Menu.Dropdown>
        </Menu>
      </header>
      {recordView !== 'overview' && (
        <div className={styles.toolbar}>
          <TextInput
            className={styles.filter}
            aria-label="报文过滤器"
            placeholder="显示过滤器 · tcp.port == 443"
            leftSection={<Filter size={14} />}
            value={input}
            onChange={(event) => {
              setInput(event.currentTarget.value)
              setSelectedId(undefined)
              setChosenAlert(undefined)
            }}
            size="xs"
            rightSection={
              input ? (
                <ActionIcon
                  size="xs"
                  variant="subtle"
                  aria-label="清空过滤器"
                  onClick={() => {
                    setInput('')
                    setSelectedId(undefined)
                  }}
                >
                  <X size={12} />
                </ActionIcon>
              ) : undefined
            }
          />
          <Select
            comboboxProps={{ withinPortal: false }}
            aria-label="协议筛选"
            placeholder="全部协议"
            clearable
            data={['TCP', 'UDP', 'ICMP', 'ARP']}
            value={protocol}
            onChange={(value) => {
              setProtocol(value)
              setChosenAlert(undefined)
              setSelectedId(undefined)
            }}
            size="xs"
            className={styles.protocolSelect}
          />
          <div className={styles.risks} aria-label="风险筛选">
            {riskLevels.map((risk) => (
              <button
                key={risk.value}
                aria-label={risk.label}
                aria-pressed={level === risk.value}
                onClick={() => {
                  setLevel(level === risk.value ? undefined : risk.value)
                  setRecordView('alerts')
                  setChosenAlert(undefined)
                  setSelection(undefined)
                  setSelectedId(undefined)
                }}
                style={{ '--risk': risk.color } as CSSProperties}
              >
                <i />
                {risk.label.slice(0, 1)}
                <b>{counts ? counts[risk.value] : '—'}</b>
              </button>
            ))}
          </div>
          <Tooltip label="导出筛选后的报文">
            <ActionIcon
              variant="subtle"
              aria-label="导出报文"
              loading={exportPackets.isPending}
              onClick={() => exportPackets.mutate()}
            >
              <Download size={15} />
            </ActionIcon>
          </Tooltip>
        </div>
      )}
      {(selectionName || chosenAlert || timeRange || filter || protocol || level) && (
        <div className={styles.filterbar}>
          {filter && (
            <button className={styles.selection} onClick={() => setInput('')} title={filter}>
              {filter}
              <X size={12} />
            </button>
          )}
          {protocol && (
            <button className={styles.selection} onClick={() => setProtocol(null)}>
              {protocol}
              <X size={12} />
            </button>
          )}
          {level && (
            <button className={styles.selection} onClick={() => setLevel(undefined)}>
              {riskLevels.find((risk) => risk.value === level)?.label}
              <X size={12} />
            </button>
          )}
          {selectionName && (
            <button className={styles.selection} onClick={() => select()} title={selectionName}>
              {selectionName}
              <X size={12} />
            </button>
          )}
          {chosenAlert && (
            <button
              className={styles.selection}
              onClick={() => {
                setChosenAlert(undefined)
                setSelectedId(undefined)
              }}
              title={chosenAlert.signature}
            >
              {chosenAlert.signature}
              <X size={12} />
            </button>
          )}
          {timeRange && (
            <button
              className={styles.selection}
              onClick={() => {
                setTimeRange(undefined)
                setSelectedId(undefined)
              }}
            >
              {clockTime(timeRange.start)} – {clockTime(timeRange.end)}
              <X size={12} />
            </button>
          )}
        </div>
      )}
      {errors.length > 0 && (
        <div className={styles.errors} role="alert">
          {[...new Set(errors)].map((error) => (
            <div key={error}>{error}</div>
          ))}
        </div>
      )}
      <section className={styles.timeline} aria-label="环境流量时间轴" hidden={!graphVisible}>
        <header>
          <strong>流量趋势</strong>
          <span>接收 ┄　发送 ━</span>
          <span>{unit.unit}/s</span>
        </header>
        {data.history.data && (
          <TrafficTimeline
            history={data.history.data}
            unit={`${unit.unit}/s`}
            scale={unit.scale}
            names={names}
            colors={palette}
            timeWindow={timeRange}
            annotations={alerts.map((alert) => ({
              time: alert.lastSeen,
              label: alert.signature,
              color: riskLevel(alert.severity).color,
            }))}
            onTimeRange={(start, end) => {
              setTimeRange({ start, end })
              setSelectedId(undefined)
            }}
          />
        )}
      </section>
      <section className={styles.graphPane} hidden={!graphVisible}>
        <header className={styles.paneHeading}>
          <strong>{timeRange || filter ? '所示报文关系' : '通信关系'}</strong>
          <span>
            {timeRange || filter
              ? `${data.packets.length} 帧`
              : `${new Set(graphLinks.map((link) => [link.source, link.target].sort().join('/'))).size} 组关系`}
          </span>
          {selection && (
            <Button size="compact-xs" variant="default" onClick={() => setRecordView('packets')}>
              查看相关报文
            </Button>
          )}
          <span ref={setGraphTools} className={styles.graphTools} />
        </header>
        <div className={styles.paneBody}>
          {!data.traffic.isPending && (
            <TrafficGraph
              spec={spec}
              links={graphLinks}
              groups={groups}
              colors={colors}
              selection={selection}
              onSelect={select}
              toolsTarget={graphTools}
            />
          )}
        </div>
      </section>
      <section className={styles.packetPane} hidden={graphVisible}>
        {chosenAlert && recordView === 'packets' && (
          <details className={styles.alertDetails}>
            <summary>告警详情 · {chosenAlert.signature}</summary>
            <p>
              {chosenAlert.category} · {chosenAlert.count} 次
            </p>
            <p>
              {chosenAlert.source}:{chosenAlert.sourcePort} → {chosenAlert.destination}:{chosenAlert.destinationPort} ·{' '}
              {chosenAlert.protocol}
            </p>
            <p>
              {dateTime(chosenAlert.firstSeen)} – {dateTime(chosenAlert.lastSeen)}
            </p>
            <p>
              规则 {chosenAlert.generatorId}:{chosenAlert.signatureId}:{chosenAlert.revision}
            </p>
          </details>
        )}
        <header className={styles.paneHeading}>
          <strong>{recordView === 'alerts' ? '告警记录' : '报文列表'}</strong>
          <span>
            {recordView === 'alerts'
              ? `${visibleAlerts.length} 条`
              : `${data.packets.length.toLocaleString('zh-CN')} 帧`}
          </span>
          {data.fetching && <i className={styles.fetching} />}
        </header>
        <div className={styles.paneBody}>
          {recordView === 'alerts' ? (
            <RiskRecords
              alerts={visibleAlerts}
              more={security.hasNextPage}
              fetching={security.isFetching}
              onMore={() => {
                setPaused(true)
                void security.fetchNextPage()
              }}
              onSelect={(alert) => {
                setChosenAlert(alert)
                setSelectedId(undefined)
                setPaused(true)
                setRecordView('packets')
              }}
            />
          ) : (
            <PacketList
              packets={data.packets}
              selectedId={selected}
              onSelect={(id) => {
                setSelectedId(id)
                setPaused(true)
              }}
              onOpen={() => setDetailOpen(true)}
              alerts={alerts}
              loading={data.loading}
              fetching={data.fetching}
              onOlder={
                data.loadOlder
                  ? () => {
                      setPaused(true)
                      data.loadOlder!()
                    }
                  : undefined
              }
              live={!paused}
            />
          )}
        </div>
      </section>
      <PacketDetailDialog
        key={selected}
        opened={detailOpen && Boolean(selected)}
        onClose={() => setDetailOpen(false)}
        detail={packet.data}
        error={packet.error}
        loading={packet.isFetching}
        alerts={alerts}
        alert={chosenAlert}
        interfaceName={packet.data?.packet.interfaceId ? interfaceNames.get(packet.data.packet.interfaceId) : undefined}
      />
      <footer className={styles.statusbar}>
        <span>
          <i data-recording={retention?.status === 'recording'} />
          {retention?.status === 'recording'
            ? '持续记录'
            : retention?.status === 'idle'
              ? '无运行接口'
              : retention?.status === 'unavailable'
                ? '采集不可用'
                : '连接采集器…'}
        </span>
        <span>{retention ? `${volume(retention.usedBytes)} / ${volume(retention.limitBytes)}` : ''}</span>
        {sensors && sensors.length > 0 && (
          <span
            className={styles.sensorStatus}
            title={sensors
              .map(
                (sensor) => `${sensor.nodeName ?? '节点'} · ${sensor.status}${sensor.error ? ` · ${sensor.error}` : ''}`
              )
              .join('\n')}
          >
            <ShieldCheck size={13} />
            Suricata {sensors.filter((sensor) => sensor.status === 'ready').length}/{sensors.length}
          </span>
        )}
        <div className={styles.spacer} />
        <span className={styles.coverage}>
          {retention?.oldestAt ? `${clockTime(retention.oldestAt)} – ${clockTime(retention.newestAt!)}` : ''}
        </span>
        {paused && <span>视图已暂停</span>}
      </footer>
      <Modal
        portalProps={{
          target: document.fullscreenElement instanceof HTMLElement ? document.fullscreenElement : undefined,
        }}
        opened={filesOpen}
        onClose={() => setFilesOpen(false)}
        title="已保存抓包"
        size="lg"
      >
        <div className={styles.files}>
          {files.data?.segments.map((segment) => (
            <div key={`${segment.id}:${segment.nodeId}`}>
              <span>{dateTime(segment.startedAt)}</span>
              <span>{segment.nodeName}</span>
              <span>{volume(segment.bytes)}</span>
              <a href={api.captureDownload(environment.id, segment.nodeId, segment.id)} download aria-label="下载抓包">
                <Download size={15} />
              </a>
            </div>
          ))}
          {!files.isPending && !files.data?.segments.length && <p>没有已保存抓包</p>}
        </div>
      </Modal>
      <Modal
        portalProps={{
          target: document.fullscreenElement instanceof HTMLElement ? document.fullscreenElement : undefined,
        }}
        opened={rolesOpen}
        onClose={() => setRolesOpen(false)}
        title="资产角色"
        size="sm"
      >
        <div className={styles.roles}>
          {spec.assets.map((asset) => (
            <TextInput
              key={asset.id}
              label={asset.name}
              value={roles[asset.id] ?? ''}
              placeholder={groups.get(asset.id)}
              onChange={(event) => {
                const value = event.currentTarget.value
                setRoles((current) => ({ ...current, [asset.id]: value }))
              }}
              size="xs"
            />
          ))}
          <Button
            size="xs"
            leftSection={<Check size={13} />}
            loading={saveRoles.isPending}
            onClick={() => saveRoles.mutate()}
          >
            保存
          </Button>
        </div>
      </Modal>
    </section>
  )
}
