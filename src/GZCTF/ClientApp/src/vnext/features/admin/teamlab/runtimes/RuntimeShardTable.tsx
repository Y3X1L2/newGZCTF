import { memo, useMemo } from 'react'
import { DataTable, type AdminDataColumn } from '../../shared/AdminWorkbench'
import type { TeamLabRuntime, TeamLabRuntimeShard } from '../api'
import { TeamLabRuntimeStatusBadge } from '../shared/TeamLabStatusBadge'
import styles from './RuntimePanels.module.css'

export const RuntimeShardTable = memo(function RuntimeShardTable({ runtime }: { runtime: TeamLabRuntime }) {
  const networkNames = useMemo(() => new Map(runtime.networks.map((network) => [network.key, network.name])), [runtime.networks])
  const assetNames = useMemo(() => new Map(runtime.assets.map((asset) => [asset.key, asset.name])), [runtime.assets])
  const columns = useMemo<AdminDataColumn<TeamLabRuntimeShard>[]>(() => [
    {
      id: 'node',
      header: '运行节点',
      width: 'wide',
      render: (shard) => <span className={styles.identityCell}><strong>{shard.workerNodeName}</strong><code>{shard.workerNodeId}</code></span>,
    },
    {
      id: 'networks',
      header: '网段',
      render: (shard) => shard.networkKeys.length ? shard.networkKeys.map((key) => networkNames.get(key) ?? key).join('、') : '—',
    },
    {
      id: 'assets',
      header: '资产',
      render: (shard) => shard.assetKeys.length ? shard.assetKeys.map((key) => assetNames.get(key) ?? key).join('、') : '—',
    },
    {
      id: 'status',
      header: '状态',
      width: 'compact',
      render: (shard) => <TeamLabRuntimeStatusBadge status={shard.status} />,
    },
  ], [assetNames, networkNames])

  return (
    <section className={styles.panel} aria-labelledby="runtime-shards-title">
      <header className={styles.panelHeader}>
        <div><span>SHARD PLACEMENT</span><h3 id="runtime-shards-title">运行分片</h3></div>
        <strong>{runtime.shards.length} 个节点分片</strong>
      </header>
      <DataTable
        caption="TeamLab 运行分片"
        columns={columns}
        emptyDescription="运行时尚未产生节点分片。"
        emptyTitle="暂无分片"
        rowKey={(shard) => shard.id}
        rows={[...runtime.shards]}
      />
      {runtime.shards.some((shard) => shard.error) ? (
        <ul className={styles.errorList}>
          {runtime.shards.filter((shard) => shard.error).map((shard) => <li key={shard.id}>{shard.workerNodeName}: {shard.error}</li>)}
        </ul>
      ) : null}
    </section>
  )
})
