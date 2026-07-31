import { Box, Container, Monitor, Network, Server } from 'lucide-react'
import { memo, useMemo } from 'react'
import type { TeamLabRuntime } from '../api'
import { TeamLabRuntimeStatusBadge } from '../shared/TeamLabStatusBadge'
import styles from './RuntimePanels.module.css'

export const RuntimeTopologyView = memo(function RuntimeTopologyView({ runtime }: { runtime: TeamLabRuntime }) {
  const networks = useMemo(() => new Map(runtime.networks.map((network) => [network.key, network])), [runtime.networks])
  const assets = useMemo(() => new Map(runtime.assets.map((asset) => [asset.key, asset])), [runtime.assets])
  const assignedAssets = useMemo(() => new Set(runtime.shards.flatMap((shard) => shard.assetKeys)), [runtime.shards])
  const unassigned = runtime.assets.filter((asset) => !assignedAssets.has(asset.key))

  return (
    <section className={styles.panel} aria-labelledby="runtime-topology-title">
      <header className={styles.panelHeader}>
        <div><span>RUNTIME TOPOLOGY</span><h3 id="runtime-topology-title">实际部署拓扑</h3></div>
        <span className={styles.muted}>{runtime.networks.length} 网段 / {runtime.assets.length} 资产</span>
      </header>
      <div className={styles.topologyGrid}>
        {runtime.shards.map((shard) => (
          <article className={styles.shardLane} key={shard.id}>
            <header>
              <Server size={17} />
              <span><strong>{shard.workerNodeName}</strong><small>{shard.id}</small></span>
              <TeamLabRuntimeStatusBadge status={shard.status} />
            </header>
            <div className={styles.networkStrip}>
              {shard.networkKeys.map((key) => {
                const network = networks.get(key)
                return <span key={key}><Network size={14} /><strong>{network?.name ?? key}</strong><code>{network?.cidr ?? '—'}</code></span>
              })}
            </div>
            <div className={styles.assetList}>
              {shard.assetKeys.map((key) => {
                const asset = assets.get(key)
                if (!asset) return <span key={key}><Box size={15} />{key}</span>
                return (
                  <span key={key}>
                    {asset.kind === 'docker' ? <Container size={15} /> : <Monitor size={15} />}
                    <span><strong>{asset.name}</strong><small>{asset.primaryIp ?? '等待地址'}</small></span>
                    <TeamLabRuntimeStatusBadge status={asset.status} />
                  </span>
                )
              })}
            </div>
          </article>
        ))}
      </div>
      {unassigned.length ? (
        <div className={styles.unassigned}><strong>尚未分配</strong>{unassigned.map((asset) => <code key={asset.key}>{asset.name}</code>)}</div>
      ) : null}
    </section>
  )
})
