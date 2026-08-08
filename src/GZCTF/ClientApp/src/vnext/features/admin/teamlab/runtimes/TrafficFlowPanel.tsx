import { Activity, ArrowRight, Search } from 'lucide-react'
import { memo, useMemo } from 'react'
import { InlineFeedback } from '../../../../shared/Interaction'
import { DataState } from '../../../../shared/Primitives'
import { errorMessage } from '../../../../shared/errors'
import { CursorPaginationBar, DataTable, FilterToolbar, RefreshIndicator, ToolbarGroup, type AdminDataColumn } from '../../shared/AdminWorkbench'
import { formatAdminDate } from '../../shared/adminFormat'
import type { TeamLabTrafficFlow } from '../api'
import { endpoint, formatBytes } from './runtimePresentation'
import { TrafficProtocolFilter } from './TrafficProtocolFilter'
import type { TrafficFlowFilters, useTrafficObservability } from './useTrafficObservability'
import styles from './RuntimePanels.module.css'

type FlowState = ReturnType<typeof useTrafficObservability>['flows']

export const TrafficFlowPanel = memo(function TrafficFlowPanel({
  flows,
  filters,
  onFiltersChange,
}: {
  flows: FlowState
  filters: TrafficFlowFilters
  onFiltersChange: (filters: TrafficFlowFilters) => void
}) {
  const columns = useMemo<AdminDataColumn<TeamLabTrafficFlow>[]>(() => [
    { id: 'route', header: '通信端点', width: 'wide', render: (flow) => <span className={styles.flowRoute}><code>{endpoint(flow.sourceIp, flow.sourcePort)}</code><ArrowRight size={14} /><code>{endpoint(flow.destinationIp, flow.destinationPort)}</code></span> },
    { id: 'protocol', header: '协议', width: 'compact', render: (flow) => <code>{flow.protocol}</code> },
    { id: 'network', header: '网段', render: (flow) => <span className={styles.identityCell}><strong>{flow.networkKey}</strong><small>{flow.shardId}</small></span> },
    { id: 'traffic', header: '流量', render: (flow) => <span className={styles.identityCell}><strong>{formatBytes(flow.bytes)}</strong><small>{flow.packets} 包</small></span> },
    { id: 'seen', header: '最后观测', visibility: 'desktop', render: (flow) => formatAdminDate(flow.lastSeen) },
  ], [])

  return <section className={styles.panel} aria-labelledby="traffic-flows-title">
    <header className={styles.panelHeader}><div><span>流量观测</span><h3 id="traffic-flows-title">流量元数据</h3></div><RefreshIndicator active={flows.isRefreshing} label={flows.isRefreshing ? '同步中' : '已同步'} /></header>
    <FilterToolbar>
      <ToolbarGroup grow><label className={styles.filterInput}><Search aria-hidden="true" size={15} /><input aria-label="检索通信端点" onChange={(event) => onFiltersChange({ ...filters, query: event.target.value })} placeholder="检索源或目标地址" value={filters.query} /></label></ToolbarGroup>
      <ToolbarGroup>
        <TrafficProtocolFilter label="流量协议" onChange={(protocol) => onFiltersChange({ ...filters, protocol })} value={filters.protocol} />
        <input aria-label="网段标识" className={styles.filterSelect} onChange={(event) => onFiltersChange({ ...filters, networkKey: event.target.value })} placeholder="网段标识" value={filters.networkKey} />
      </ToolbarGroup>
    </FilterToolbar>
    {flows.page && <TrafficCompleteness complete={flows.page.completeness.complete} droppedRecords={flows.page.completeness.droppedRecords} />}
    {flows.isLoading ? <DataState description="正在读取服务端流量分页。" loading title="流量加载中" /> : flows.error ? <InlineFeedback tone="danger">{errorMessage(flows.error, '流量元数据加载失败。')}</InlineFeedback> : flows.page?.items.length ? <><DataTable caption="TeamLab 流量元数据" columns={columns} rowKey={(flow) => flow.cursor} rows={[...flows.page.items]} /><CursorPaginationBar hasNext={Boolean(flows.page.nextCursor)} label="流量记录分页" onNext={() => flows.page?.nextCursor && flows.cursor.next(flows.page.nextCursor)} onPrevious={flows.cursor.previous} page={flows.cursor.page} /></> : <DataState description="当前筛选条件下没有流量记录。" title="暂无流量记录" />}
    <footer className={styles.panelNote}><Activity size={14} />筛选条件在服务端执行，切换分页不会遗漏匹配记录。</footer>
  </section>
})

function TrafficCompleteness({ complete, droppedRecords }: { complete: boolean; droppedRecords: number }) {
  return complete ? <InlineFeedback tone="success">观测完整</InlineFeedback> : <InlineFeedback>存在 {droppedRecords} 条观测丢失，路径结论可能不完整。</InlineFeedback>
}
