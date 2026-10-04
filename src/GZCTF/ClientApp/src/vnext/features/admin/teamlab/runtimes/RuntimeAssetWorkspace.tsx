import { FolderOpen, Plus, Search } from 'lucide-react'
import { useState } from 'react'
import { useSearchParams } from 'react-router'
import { ActionButton, VNextDrawer } from '../../../../shared/Interaction'
import { DataState } from '../../../../shared/Primitives'
import type { TeamLabRuntime } from '../api'
import { TeamLabAssetStatusBadge } from '../shared/TeamLabStatusBadge'
import { AssetCompositionPanel } from './AssetCompositionPanel'
import { AssetControlPanel } from './AssetControlPanel'
import { AssetDiagnosticsPanel } from './AssetDiagnosticsPanel'
import { AssetFilesPanel } from './AssetFilesPanel'
import { RuntimeRemoteAccessPanel } from './RuntimeRemoteAccessPanel'
import { ServiceAccessPanel } from './ServiceAccessPanel'
import { VmDiagnosticsPanel } from './VmDiagnosticsPanel'
import { DeviceHealthPanel } from './DeviceHealthPanel'
import styles from './RuntimeAssetWorkspace.module.css'

type AssetView = 'access' | 'files' | 'diagnostics'
type ComposeAction = 'add' | 'replace' | 'remove'

export function RuntimeAssetWorkspace({ runtime, onSubmitted }: { runtime: TeamLabRuntime; onSubmitted: () => Promise<unknown> }) {
  const [params, setParams] = useSearchParams()
  const [query, setQuery] = useState('')
  const [status, setStatus] = useState('')
  const [view, setView] = useState<AssetView>('access')
  const [compose, setCompose] = useState<ComposeAction | null>(null)
  const asset = runtime.assets.find(item => item.id === Number(params.get('asset'))) ?? runtime.assets[0]
  const filtered = runtime.assets.filter(item => (!status || item.status === status) && `${item.name} ${item.primaryIp ?? ''}`.toLowerCase().includes(query.toLowerCase()))
  const select = (id: number) => setParams(current => {
    const next = new URLSearchParams(current)
    next.set('asset', String(id))
    next.delete('assetTask')
    return next
  }, { replace: true })

  const composingDisabled = !['running', 'failed'].includes(runtime.status) || !!runtime.managedRolloutId ||
    ['pending', 'scheduling', 'scheduled', 'running'].includes(runtime.queueStatus ?? '')

  return <div className={styles.workspace}>
    <aside className={styles.assetRail} aria-label="运行资产">
      <div className={styles.railHeader}><strong>资产 <span>{runtime.assets.length}</span></strong>
        <ActionButton aria-label="新增资产" title="新增资产" disabled={composingDisabled} icon={<Plus size={16} />} onClick={() => setCompose('add')} type="button" />
      </div>
      <label className={styles.search}><Search size={16} aria-hidden="true" /><input aria-label="搜索运行资产" placeholder="搜索名称或地址" value={query} onChange={event => setQuery(event.currentTarget.value)} /></label>
      <select className={styles.statusFilter} aria-label="资产状态" value={status} onChange={event => setStatus(event.currentTarget.value)}><option value="">全部状态</option><option value="running">运行中</option><option value="paused">已暂停</option><option value="stopped">已停止</option><option value="failed">异常</option></select>
      <div className={styles.assetList}>{filtered.map(item => <button aria-current={item.id === asset?.id ? 'true' : undefined} key={item.id} onClick={() => select(item.id)} type="button">
        <span className={styles.assetIdentity}><strong>{item.name}</strong><small>{item.primaryIp ?? (item.kind === 'vm' ? '虚拟机' : '容器')}</small></span>
        <TeamLabAssetStatusBadge status={item.status} />
      </button>)}</div>
      {!filtered.length ? <p className={styles.empty}>没有匹配的资产</p> : null}
    </aside>
    {asset ? <div className={styles.assetDetail}>
      <header className={styles.assetHeader}>
        <div><span>{asset.kind === 'vm' ? '虚拟机' : '容器'} · {asset.networkKeys.map(key => runtime.networks.find(network => network.key === key)?.name ?? key).join('、')}</span>
          <h3>{asset.name}</h3><p>{asset.primaryIp ?? '尚未分配地址'} <TeamLabAssetStatusBadge status={asset.status} /></p>
          {asset.error ? <p className={styles.assetError} role="alert">{asset.error}</p> : null}</div>
        <div className={styles.headerTools}>
          <AssetControlPanel key={asset.id} runtime={runtime} onReplace={() => setCompose('replace')} onRemove={() => setCompose('remove')} />
        </div>
      </header>
      <nav aria-label="资产管理" className={styles.views}>
        <button aria-pressed={view === 'access'} onClick={() => setView('access')} type="button">连接与服务</button>
        <button aria-pressed={view === 'files'} onClick={() => setView('files')} type="button"><FolderOpen size={16} />文件</button>
        <button aria-pressed={view === 'diagnostics'} onClick={() => setView('diagnostics')} type="button">诊断</button>
      </nav>
      {view === 'access' ? <div key={`${runtime.id}:${runtime.generation}:${asset.id}`} className={styles.accessLayout}>
        <RuntimeRemoteAccessPanel runtime={runtime} assetId={asset.id} />
        <ServiceAccessPanel runtime={runtime} assetId={asset.id} />
      </div> : null}
      {view === 'files' ? <AssetFilesPanel runtime={runtime} assetId={asset.id} /> : null}
      {view === 'diagnostics' ? <>{asset.kind === 'vm'
        ? <VmDiagnosticsPanel runtime={runtime} assetId={asset.id} />
        : <AssetDiagnosticsPanel runtime={runtime} assetId={asset.id} />}
        <DeviceHealthPanel runtimeId={runtime.id} generation={runtime.generation} assetId={asset.id} /></> : null}
    </div> : <DataState title="当前环境没有资产" />}
    <VNextDrawer eyebrow="" title={compose === 'add' ? '新增资产' : compose === 'replace' ? '替换资产' : '移除资产'}
      open={compose !== null} onClose={() => setCompose(null)}>
      {compose ? <AssetCompositionPanel key={`${compose}:${asset?.key ?? ''}`} runtime={runtime} initialAction={compose} initialAssetKey={compose === 'add' ? undefined : asset?.key}
        onSubmitted={async () => { await onSubmitted(); setCompose(null) }} /> : null}
    </VNextDrawer>
  </div>
}
