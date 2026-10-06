import { FolderUp, HardDrive, Network, Terminal } from 'lucide-react'
import { useState } from 'react'
import { Link, useSearchParams } from 'react-router'
import { DataState } from '../../../../shared/Primitives'
import type { TeamLabRuntime, TeamLabRuntimeAsset } from '../api'
import { TeamLabAssetStatusBadge } from '../shared/TeamLabStatusBadge'
import { AssetControlPanel } from './AssetControlPanel'
import { AssetDiagnosticsPanel } from './AssetDiagnosticsPanel'
import { AssetTransferPanel } from './AssetTransferPanel'
import { RuntimeRemoteAccessPanel } from './RuntimeRemoteAccessPanel'
import { VmDiagnosticsPanel } from './VmDiagnosticsPanel'
import styles from './RuntimeAssetWorkspace.module.css'

function kindLabel(asset: TeamLabRuntimeAsset) {
  return asset.kind === 'docker' ? 'Docker' : asset.operatingSystem === 'windows' ? 'Windows VM'
    : asset.operatingSystem === 'linux' ? 'Linux VM' : '系统类型未确认的 VM'
}

export function RuntimeAssetWorkspace({ runtime }: { runtime: TeamLabRuntime }) {
  const [params, setParams] = useSearchParams()
  const [tool, setTool] = useState<'transfer' | 'logs' | null>(null)
  const assets = runtime.assets
  const asset = assets.find(item => item.id === Number(params.get('asset'))) ?? assets[0]
  const select = (id: number) => {
    setTool(null)
    setParams(current => { const next = new URLSearchParams(current); next.set('asset', String(id)); next.delete('assetTask'); return next }, { replace: true })
  }
  if (!asset) return <DataState title="当前环境没有机器" />
  const visibleInterfaces = asset.interfaces ?? []
  const transfer = asset.capabilities?.find(item => item.kind === (asset.kind === 'docker' ? 'files' : 'sftp'))
  const canTransfer = transfer?.status === 'configured-unverified'
  const hasMoreTools = asset.kind === 'docker' || canTransfer || !!transfer && ['unconfigured', 'currently-unavailable'].includes(transfer.status)
  return <div className={styles.workspace}>
    <aside className={styles.assetRail} aria-label="按网段查看机器">
      <h3>机器与网段</h3>
      {runtime.networks.map(network => <section key={network.key} className={styles.networkGroup}>
        <h4><Network size={16} />{network.name}</h4><small>{network.cidr}</small>
        {assets.filter(item => item.networkKeys.includes(network.key)).map(item => {
          const address = item.interfaces?.find(nic => nic.networkKey === network.key)?.assigned?.ipAddress
          return <button aria-current={item.id === asset.id ? 'true' : undefined} key={item.id} onClick={() => select(item.id)} type="button">
            <span><strong>{item.name}</strong><small>{address ?? '地址未分配'}</small></span><TeamLabAssetStatusBadge status={item.status} />
          </button>
        })}
      </section>)}
      {assets.filter(item => !item.networkKeys.length || !item.networkKeys.some(key => runtime.networks.some(network => network.key === key))).map(item =>
        <button aria-current={item.id === asset.id ? 'true' : undefined} key={item.id} onClick={() => select(item.id)} type="button"><span><strong>{item.name}</strong><small>网段未关联</small></span></button>)}
    </aside>
    <div className={styles.assetDetail} key={`${runtime.id}:${runtime.generation}:${asset.id}`}>
      <header className={styles.assetHeader}><div><span>{kindLabel(asset)}</span><h3>{asset.name}</h3>
        <TeamLabAssetStatusBadge status={asset.status} />
        {asset.operatingSystemSource === 'template-current' ? <p className={styles.sourceNote}>系统类型根据镜像标记，来宾系统未核验。</p> : null}
        {asset.error ? <p className={styles.assetError} role="alert">{asset.error}</p> : null}</div>
        <AssetControlPanel runtime={runtime} />
      </header>
      <section className={styles.interfaces} aria-label="网卡与地址"><h4>网卡与地址</h4>
        {visibleInterfaces.length ? visibleInterfaces.map(nic => <div className={styles.interface} key={nic.key}>
          <div><strong>{runtime.networks.find(network => network.key === nic.networkKey)?.name ?? nic.networkKey}</strong>
            <span>{nic.key}{nic.primary ? ' · 主网卡' : ''}</span></div>
          <div><small>分配配置</small><code>{nic.assigned ? `${nic.assigned.ipAddress}/${nic.assigned.prefixLength}` : '未分配地址'}</code>
            {nic.observed ? <><small>来宾回读 · {new Date(nic.observed.observedAt).toLocaleString()}</small>
              <code>{nic.observed.ipAddress}/{nic.observed.prefixLength}</code></> : <small>来宾状态未核对</small>}</div>
          <details><summary>DNS 与路由</summary>
            <dl><div><dt>分配 DNS</dt><dd>{nic.assigned?.dnsServers.join('、') || '未设置'}</dd></div>
              <div><dt>分配网关</dt><dd>{nic.assigned?.gatewayIp || '未设置'}</dd></div>
              <div><dt>分配路由</dt><dd>{nic.assigned?.staticRoutes.map(route => `${route.destinationCidr} → ${route.nextHop}`).join('；') || '无'}</dd></div>
              {nic.observed ? <><div><dt>回读 DNS</dt><dd>{nic.observed.dnsServers.join('、') || '未设置'}</dd></div>
                <div><dt>回读网关</dt><dd>{nic.observed.gatewayIp || '未设置'}</dd></div>
                <div><dt>回读路由</dt><dd>{nic.observed.staticRoutes.map(route => `${route.destinationCidr} → ${route.nextHop}`).join('；') || '无'}</dd></div></> : null}</dl>
          </details>
        </div>) : <p>当前代次没有可确认的逐网卡分配记录，地址尚未核对。</p>}
      </section>
      <RuntimeRemoteAccessPanel runtime={runtime} assetId={asset.id} />
      {asset.kind === 'vm' ? <VmDiagnosticsPanel runtime={runtime} assetId={asset.id} /> : null}
      {hasMoreTools ? <section className={styles.moreTools} aria-label="更多机器工具"><h4>更多工具</h4>
        {asset.kind === 'docker' ? <button onClick={() => setTool(tool === 'logs' ? null : 'logs')} type="button"><Terminal size={16} />容器日志</button> : null}
        {canTransfer ? <button onClick={() => setTool(tool === 'transfer' ? null : 'transfer')} type="button"><FolderUp size={16} />传文件</button> : null}
        {transfer?.status === 'unconfigured' && transfer.settingsTemplateId ? <Link to={`/admin/images?template=${transfer.settingsTemplateId}&remoteAccess=1&returnTo=${encodeURIComponent(window.location.pathname + window.location.search)}`}><HardDrive size={16} />配置传文件入口</Link> : null}
        {transfer && ['unconfigured', 'currently-unavailable'].includes(transfer.status) ? <p>{transfer.reason}</p> : null}
        {tool === 'logs' ? <AssetDiagnosticsPanel runtime={runtime} assetId={asset.id} /> : null}
        {tool === 'transfer' ? <><p>实例内文件变化不会自动写回镜像或场景版本。</p><AssetTransferPanel runtime={runtime} assetId={asset.id} /></> : null}
      </section> : null}
    </div>
  </div>
}
