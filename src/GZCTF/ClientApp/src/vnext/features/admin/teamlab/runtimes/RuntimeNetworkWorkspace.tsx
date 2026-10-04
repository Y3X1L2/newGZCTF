import { Container, Monitor, Network, Radio, Search, SlidersHorizontal } from 'lucide-react'
import { useCallback, useDeferredValue, useState } from 'react'
import { useSearchParams } from 'react-router'
import { ActionButton, VNextDrawer } from '../../../../shared/Interaction'
import { DataState } from '../../../../shared/Primitives'
import type { TeamLabRuntime, TeamLabTrafficFlow } from '../api'
import { TeamLabAssetStatusBadge } from '../shared/TeamLabStatusBadge'
import { CapturePanel } from './CapturePanel'
import { RuntimeLinkPolicyPanel } from './RuntimeLinkPolicyPanel'
import { TrafficConnectionChart } from './TrafficConnectionChart'
import { TrafficFlowPanel } from './TrafficFlowPanel'
import { TrafficPathPanel } from './TrafficPathPanel'
import { TrafficProtocolFilter } from './TrafficProtocolFilter'
import { endpoint, formatBytes } from './runtimePresentation'
import { useTrafficObservability } from './useTrafficObservability'
import styles from './RuntimeWorkspaces.module.css'

export function RuntimeNetworkWorkspace({ runtime, onSelectAsset }: { runtime: TeamLabRuntime; onSelectAsset: (id: number) => void }) {
  const [params] = useSearchParams()
  const [view, setView] = useState(params.get('tab') === 'traffic' ? 'flows' : 'connections')
  const [networkKey, setNetworkKey] = useState(runtime.networks[0]?.key ?? '')
  const [query, setQuery] = useState('')
  const [protocol, setProtocol] = useState('')
  const [trafficNetwork, setTrafficNetwork] = useState('')
  const [drawer, setDrawer] = useState<'policies' | 'capture' | null>(params.get('tab') === 'capture' ? 'capture' : null)
  const [selection, setSelection] = useState<readonly TeamLabTrafficFlow[] | null>(null)
  const deferredQuery = useDeferredValue(query)
  const traffic = useTrafficObservability(view === 'connections' ? '' : runtime.id, runtime.status,
    { query: deferredQuery, protocol, networkKey: trafficNetwork }, { query: deferredQuery, protocol, confidence: '' })
  const network = runtime.networks.find(item => item.key === networkKey)
  const flows = traffic.flows.page?.items
  const selectConnection = useCallback((selected: { address: string } | { source: string; destination: string }) => {
    if ('address' in selected) setQuery(selected.address)
    else setSelection(flows?.filter(flow => flow.sourceIp === selected.source && flow.destinationIp === selected.destination) ?? [])
  }, [flows])
  return <div>
    <div className={styles.trafficHeader}>
      <nav className={styles.viewTabs} aria-label="网络视图">{[['connections', '网络连接'], ['flows', '通信流量'], ['paths', '流量路径']].map(([key, label]) =>
        <button key={key} aria-pressed={view === key} onClick={() => setView(key)} type="button">{label}</button>)}</nav>
      <ActionButton icon={<Radio size={16} />} onClick={() => setDrawer('capture')} type="button">抓包</ActionButton>
    </div>
    {view === 'connections' ? <div className={styles.networkLayout}>
      <aside className={styles.networkRail} aria-label="运行网段">{runtime.networks.map(item => <button key={item.key} aria-current={networkKey === item.key ? 'true' : undefined}
        onClick={() => setNetworkKey(item.key)} type="button"><Network size={18} /><span><strong>{item.name}</strong><small>{item.cidr}</small></span></button>)}</aside>
      <section>{network ? <><header className={styles.networkHeader}><div><h3>{network.name}</h3><p>{network.cidr} · 网关 {network.gatewayIp}</p></div>
        <ActionButton icon={<SlidersHorizontal size={16} />} onClick={() => setDrawer('policies')} type="button">链路策略</ActionButton></header>
        <div className={styles.networkAssets}>{runtime.assets.filter(asset => asset.networkKeys.includes(network.key)).map(asset => <button key={asset.id} onClick={() => onSelectAsset(asset.id)} type="button">
          {asset.kind === 'vm' ? <Monitor size={22} /> : <Container size={22} />}<div><strong>{asset.name}</strong><span>{asset.primaryIp ?? '等待地址'}</span><TeamLabAssetStatusBadge status={asset.status} /></div>
        </button>)}</div></> : <DataState title="当前没有运行网段" />}</section>
    </div> : <>
      <div className={styles.toolbar}><label className={styles.search}><Search size={16} /><input aria-label="搜索通信地址" placeholder="源地址或目标地址" value={query} onChange={event => setQuery(event.currentTarget.value)} /></label>
        <TrafficProtocolFilter label="通信协议" value={protocol} onChange={setProtocol} />
        {view === 'flows' ? <select aria-label="流量网段" value={trafficNetwork} onChange={event => setTrafficNetwork(event.currentTarget.value)}><option value="">全部网段</option>{runtime.networks.map(item => <option key={item.key} value={item.key}>{item.name}</option>)}</select> : null}
      </div>
      {view === 'flows' ? <>
        {flows?.length ? <><div className={styles.trafficHeader}><h3>通信分布</h3><span>当前 {flows.length} 条记录 · {formatBytes(flows.reduce((sum, flow) => sum + flow.bytes, 0))}</span></div>
          <TrafficConnectionChart flows={flows} assets={runtime.assets} onSelect={selectConnection} /></> : null}
        <TrafficFlowPanel flows={traffic.flows} networks={runtime.networks} onSelect={flow => setSelection([flow])} />
      </> : <TrafficPathPanel runtimeId={runtime.id} paths={traffic.paths} />}
    </>}
    <VNextDrawer eyebrow="" open={drawer !== null} onClose={() => setDrawer(null)} title={drawer === 'capture' ? '网络抓包' : `${network?.name ?? ''} · 链路策略`}>
      {drawer === 'capture' ? <CapturePanel runtimeId={runtime.id} networks={runtime.networks} /> : drawer === 'policies' && network ? <RuntimeLinkPolicyPanel runtimeId={runtime.id}
        networks={[network]} assets={runtime.assets.map(asset => ({ key: asset.key, name: asset.name, networkKeys: asset.networkKeys }))} /> : null}
    </VNextDrawer>
    <VNextDrawer eyebrow="" open={selection !== null} onClose={() => setSelection(null)} title="通信详情">
      {selection?.map(flow => <dl className={styles.facts} key={flow.cursor}>
        <div><dt>源端点</dt><dd>{endpoint(flow.sourceIp, flow.sourcePort)}</dd></div><div><dt>目标端点</dt><dd>{endpoint(flow.destinationIp, flow.destinationPort)}</dd></div>
        <div><dt>协议</dt><dd>{flow.protocol}</dd></div><div><dt>网段</dt><dd>{runtime.networks.find(item => item.key === flow.networkKey)?.name ?? flow.networkKey}</dd></div>
        <div><dt>通信量</dt><dd>{formatBytes(flow.bytes)} · {flow.packets} 个报文</dd></div>
      </dl>)}
    </VNextDrawer>
  </div>
}
