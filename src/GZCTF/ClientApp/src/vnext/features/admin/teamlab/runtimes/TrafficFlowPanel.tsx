import { ArrowRight } from 'lucide-react'
import { memo, useMemo } from 'react'
import { InlineFeedback } from '../../../../shared/Interaction'
import { DataState } from '../../../../shared/Primitives'
import { errorMessage } from '../../../../shared/errors'
import { CursorPaginationBar, DataTable, type AdminDataColumn } from '../../shared/AdminWorkbench'
import { formatAdminDate } from '../../shared/adminFormat'
import type { TeamLabRuntimeNetwork, TeamLabTrafficFlow } from '../api'
import { endpoint, formatBytes } from './runtimePresentation'
import type { useTrafficObservability } from './useTrafficObservability'
import styles from './RuntimePanels.module.css'

type FlowState = ReturnType<typeof useTrafficObservability>['flows']

export const TrafficFlowPanel = memo(function TrafficFlowPanel({
  flows,
  networks,
  onSelect,
}: {
  flows: FlowState
  networks: readonly TeamLabRuntimeNetwork[]
  onSelect: (flow: TeamLabTrafficFlow) => void
}) {
  const columns = useMemo<AdminDataColumn<TeamLabTrafficFlow>[]>(() => [
    { id: 'route', header: '通信端点', width: 'wide', render: (flow) => <span className={styles.flowRoute}><code>{endpoint(flow.sourceIp, flow.sourcePort)}</code><ArrowRight size={14} /><code>{endpoint(flow.destinationIp, flow.destinationPort)}</code></span> },
    { id: 'protocol', header: '协议', width: 'compact', render: (flow) => <code>{flow.protocol}</code> },
    { id: 'network', header: '网段', visibility: 'desktop', render: (flow) => networks.find(network => network.key === flow.networkKey)?.name ?? flow.networkKey },
    { id: 'traffic', header: '流量', render: (flow) => <span className={styles.identityCell}><strong>{formatBytes(flow.bytes)}</strong><small>{flow.packets} 包</small></span> },
    { id: 'seen', header: '最后观测', visibility: 'desktop', render: (flow) => formatAdminDate(flow.lastSeen) },
  ], [networks])

  return <section className={styles.panel} aria-labelledby="traffic-flows-title">
    <header className={styles.panelHeader}><h3 id="traffic-flows-title">通信记录</h3></header>
    {flows.page && <TrafficCompleteness complete={flows.page.completeness.complete} droppedRecords={flows.page.completeness.droppedRecords} />}
    {flows.isLoading ? <DataState loading title="流量加载中" /> : flows.error ? <InlineFeedback tone="danger">{errorMessage(flows.error, '流量记录加载失败。')}</InlineFeedback> : flows.page?.items.length ? <><DataTable caption="通信记录" columns={columns} onRowClick={onSelect} rowKey={(flow) => flow.cursor} rows={[...flows.page.items]} /><CursorPaginationBar hasNext={Boolean(flows.page.nextCursor)} label="流量记录分页" onNext={() => flows.page?.nextCursor && flows.cursor.next(flows.page.nextCursor)} onPrevious={flows.cursor.previous} page={flows.cursor.page} /></> : <DataState title="暂无流量记录" />}
  </section>
})

function TrafficCompleteness({ complete, droppedRecords }: { complete: boolean; droppedRecords: number }) {
  return complete ? null : <InlineFeedback>丢失 {droppedRecords} 条观测记录</InlineFeedback>
}
