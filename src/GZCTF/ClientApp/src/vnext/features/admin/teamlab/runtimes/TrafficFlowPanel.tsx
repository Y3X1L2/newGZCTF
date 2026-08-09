import { Activity, ArrowRight } from 'lucide-react'
import { memo, useMemo } from 'react'
import { InlineFeedback } from '../../../../shared/Interaction'
import { DataState } from '../../../../shared/Primitives'
import { errorMessage } from '../../../../shared/errors'
import { CursorPaginationBar, DataTable, RefreshIndicator, type AdminDataColumn } from '../../shared/AdminWorkbench'
import { formatAdminDate } from '../../shared/adminFormat'
import type { TeamLabTrafficFlow } from '../api'
import { endpoint, formatBytes } from './runtimePresentation'
import type { useTrafficObservability } from './useTrafficObservability'
import styles from './RuntimePanels.module.css'

type FlowState = ReturnType<typeof useTrafficObservability>['flows']

export const TrafficFlowPanel = memo(function TrafficFlowPanel({ flows }: { flows: FlowState }) {
  const columns = useMemo<AdminDataColumn<TeamLabTrafficFlow>[]>(() => [
    {
      id: 'route',
      header: '通信端点',
      width: 'wide',
      render: (flow) => (
        <span className={styles.flowRoute}>
          <code>{endpoint(flow.sourceIp, flow.sourcePort)}</code>
          <ArrowRight size={14} />
          <code>{endpoint(flow.destinationIp, flow.destinationPort)}</code>
        </span>
      ),
    },
    { id: 'protocol', header: '协议', width: 'compact', render: (flow) => <code>{flow.protocol}</code> },
    { id: 'network', header: '网段', render: (flow) => <span className={styles.identityCell}><strong>{flow.networkKey}</strong><small>{flow.shardId}</small></span> },
    { id: 'traffic', header: '流量', render: (flow) => <span className={styles.identityCell}><strong>{formatBytes(flow.bytes)}</strong><small>{flow.packets} 包</small></span> },
    { id: 'seen', header: '最后观测', visibility: 'desktop', render: (flow) => formatAdminDate(flow.lastSeen) },
  ], [])

  return (
    <section className={styles.panel} aria-labelledby="traffic-flows-title">
      <header className={styles.panelHeader}>
        <div><span>FLOW METADATA</span><h3 id="traffic-flows-title">流量元数据</h3></div>
        <RefreshIndicator active={flows.isRefreshing} label={flows.isRefreshing ? '同步中' : '已同步'} />
      </header>
      {flows.isLoading ? <DataState description="正在读取服务端流量分页。" loading title="流量加载中" /> : flows.error ? (
        <InlineFeedback tone="danger">{errorMessage(flows.error, '流量元数据加载失败。')}</InlineFeedback>
      ) : flows.page?.items.length ? (
        <>
          <DataTable caption="TeamLab 流量元数据" columns={columns} rowKey={(flow) => flow.cursor} rows={[...flows.page.items]} />
          <CursorPaginationBar
            hasNext={Boolean(flows.page.nextCursor)}
            label="流量记录分页"
            onNext={() => flows.page?.nextCursor && flows.cursor.next(flows.page.nextCursor)}
            onPrevious={flows.cursor.previous}
            page={flows.cursor.page}
          />
        </>
      ) : (
        <DataState description="运行环境尚未上报可聚合的五元组流量。" title="暂无流量记录" />
      )}
      <footer className={styles.panelNote}><Activity size={14} />仅展示平台持久化的流量观测结果。</footer>
    </section>
  )
})
