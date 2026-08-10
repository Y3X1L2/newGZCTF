import { ArrowRight, Route, Search } from 'lucide-react'
import { memo, useMemo, useState } from 'react'
import useSWR from 'swr'
import { InlineFeedback } from '../../../../shared/Interaction'
import { DataState } from '../../../../shared/Primitives'
import { errorMessage } from '../../../../shared/errors'
import { CursorPaginationBar, DataTable, DetailDrawer, FilterToolbar, RefreshIndicator, StatusBadge, ToolbarGroup, type AdminDataColumn } from '../../shared/AdminWorkbench'
import { formatAdminDate } from '../../shared/adminFormat'
import { teamLabRuntimeApi, teamLabRuntimeKeys, type TeamLabTrafficPathSummary } from '../api'
import { endpoint, pathConfidenceLabels } from './runtimePresentation'
import { TrafficProtocolFilter } from './TrafficProtocolFilter'
import type { TrafficPathFilters, useTrafficObservability } from './useTrafficObservability'
import styles from './RuntimePanels.module.css'

type PathState = ReturnType<typeof useTrafficObservability>['paths']
const observationPointLabels = { 'network-bridge': '网段桥接', 'router-fragment': '路由节点', 'fabric-uplink': '跨节点链路', 'workload-endpoint': '资产端点' } as const

export const TrafficPathPanel = memo(function TrafficPathPanel({ runtimeId, paths, filters, onFiltersChange }: { runtimeId: string; paths: PathState; filters: TrafficPathFilters; onFiltersChange: (filters: TrafficPathFilters) => void }) {
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const detail = useSWR(selectedId ? teamLabRuntimeKeys.path(runtimeId, selectedId) : null, () => teamLabRuntimeApi.getPath(runtimeId, selectedId!), { revalidateOnFocus: false })
  const columns = useMemo<AdminDataColumn<TeamLabTrafficPathSummary>[]>(() => [
    { id: 'route', header: '通信路径', width: 'wide', render: (path) => <span className={styles.flowRoute}><code>{endpoint(path.sourceIp, path.sourcePort)}</code><ArrowRight size={14} /><code>{endpoint(path.destinationIp, path.destinationPort)}</code></span> },
    { id: 'protocol', header: '协议', width: 'compact', render: (path) => <code>{path.protocol}</code> },
    { id: 'confidence', header: '关联可信度', render: (path) => <StatusBadge tone={path.confidence === 'packet-exact' ? 'success' : path.confidence === 'process-correlated' ? 'info' : 'warning'}>{pathConfidenceLabels[path.confidence]}</StatusBadge> },
    { id: 'hops', header: '观测跳数', width: 'compact', render: (path) => `${path.hopCount} 跳` },
    { id: 'time', header: '开始时间', visibility: 'desktop', render: (path) => formatAdminDate(path.startedAt) },
  ], [])
  return <section className={styles.panel} aria-labelledby="traffic-paths-title">
    <header className={styles.panelHeader}><div><span>路径关联</span><h3 id="traffic-paths-title">端到端路径</h3></div><RefreshIndicator active={paths.isRefreshing} label={paths.isRefreshing ? '关联中' : '已同步'} /></header>
    <FilterToolbar><ToolbarGroup grow><label className={styles.filterInput}><Search aria-hidden="true" size={15} /><input aria-label="检索流量路径" onChange={(event) => onFiltersChange({ ...filters, query: event.target.value })} placeholder="检索源或目标地址" value={filters.query} /></label></ToolbarGroup><ToolbarGroup><TrafficProtocolFilter label="路径协议" onChange={(protocol) => onFiltersChange({ ...filters, protocol })} value={filters.protocol} /><select aria-label="关联可信度" className={styles.filterSelect} onChange={(event) => onFiltersChange({ ...filters, confidence: event.target.value })} value={filters.confidence}><option value="">全部可信度</option><option value="packet-exact">报文精确</option><option value="process-correlated">进程关联</option><option value="temporally-related">时间关联</option></select></ToolbarGroup></FilterToolbar>
    {paths.page && (paths.page.completeness.complete ? <InlineFeedback tone="success">观测完整</InlineFeedback> : <InlineFeedback>存在 {paths.page.completeness.droppedRecords} 条观测丢失，路径结论可能不完整。</InlineFeedback>)}
    {paths.isLoading ? <DataState description="正在读取服务端路径分页。" loading title="路径加载中" /> : paths.error ? <InlineFeedback tone="danger">{errorMessage(paths.error, '端到端路径加载失败。')}</InlineFeedback> : paths.page?.items.length ? <><DataTable caption="TeamLab 端到端流量路径" columns={columns} onRowClick={(path) => setSelectedId(path.id)} rowKey={(path) => path.id} rows={[...paths.page.items]} /><CursorPaginationBar hasNext={Boolean(paths.page.nextCursor)} label="路径记录分页" onNext={() => paths.page?.nextCursor && paths.cursor.next(paths.page.nextCursor)} onPrevious={paths.cursor.previous} page={paths.cursor.page} /></> : <DataState description="当前筛选条件下没有端到端路径。" title="暂无端到端路径" />}
    <DetailDrawer description="按服务端保存的观测顺序展示，不补齐未观测到的中间节点。" onClose={() => setSelectedId(null)} open={Boolean(selectedId)} title="流量路径证据">
      {!detail.data && !detail.error ? <DataState description="正在读取路径跳点。" loading title="路径详情加载中" /> : detail.error ? <InlineFeedback tone="danger">{errorMessage(detail.error, '路径详情加载失败。')}</InlineFeedback> : detail.data ? <div className={styles.pathDetail}><header><Route size={18} /><strong>{endpoint(detail.data.sourceIp, detail.data.sourcePort)} 至 {endpoint(detail.data.destinationIp, detail.data.destinationPort)}</strong><StatusBadge tone="info">{pathConfidenceLabels[detail.data.confidence]}</StatusBadge></header><ol>{detail.data.hops.map((hop) => <li key={`${hop.ordinal}:${hop.observedAt}`}><span>{hop.ordinal + 1}</span><div><strong>{observationPointLabels[hop.observationPointKind]}</strong><small>{hop.assetKey ?? hop.infrastructureKey ?? hop.networkKey ?? hop.shardId ?? '未绑定对象'}</small></div><code>{endpoint(hop.sourceIp, hop.sourcePort)} 至 {endpoint(hop.destinationIp, hop.destinationPort)}</code><time>{formatAdminDate(hop.observedAt)}</time></li>)}</ol></div> : null}
    </DetailDrawer>
  </section>
})
