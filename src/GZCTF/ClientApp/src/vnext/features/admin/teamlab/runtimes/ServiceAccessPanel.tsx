import { Plus, Unplug } from 'lucide-react'
import { useEffect, useMemo, useState } from 'react'
import useSWR from 'swr'
import { ActionButton, InlineFeedback } from '../../../../shared/Interaction'
import { DataState } from '../../../../shared/Primitives'
import { errorMessage } from '../../../../shared/errors'
import { formatAdminDate } from '../../shared/adminFormat'
import type { TeamLabRuntime } from '../api'
import { teamLabServiceAccessApi, teamLabServiceAccessKeys } from '../api/teamlabServiceAccessApi'
import styles from './RuntimePanels.module.css'

export function ServiceAccessPanel({ runtime }: { runtime: TeamLabRuntime }) {
  const assets = useMemo(() => runtime.assets.filter(asset => asset.primaryIp && asset.networkKeys.length), [runtime.assets])
  const [assetId, setAssetId] = useState(assets[0]?.id ?? 0)
  const asset = assets.find(item => item.id === assetId) ?? assets[0]
  const [networkKey, setNetworkKey] = useState(asset?.networkKeys[0] ?? '')
  const [protocol, setProtocol] = useState<'tcp' | 'udp'>('tcp')
  const [internalPort, setInternalPort] = useState(80)
  const [automaticPort, setAutomaticPort] = useState(true)
  const [publicPort, setPublicPort] = useState(30000)
  const [acting, setActing] = useState(false)
  const [actionError, setActionError] = useState<unknown>(null)
  const request = useSWR(teamLabServiceAccessKeys.list(runtime.id), () => teamLabServiceAccessApi.list(runtime.id))

  useEffect(() => {
    if (!asset) return
    if (!asset.networkKeys.includes(networkKey)) setNetworkKey(asset.networkKeys[0] ?? '')
  }, [asset, networkKey])

  const create = async () => {
    if (!asset || acting) return
    setActing(true)
    setActionError(null)
    try {
      await teamLabServiceAccessApi.create(runtime.id, asset.id, {
        protocol,
        internalPort,
        publicPort: automaticPort ? null : publicPort,
        networkKey,
      })
      await request.mutate()
    } catch (error) {
      setActionError(error)
    } finally { setActing(false) }
  }

  const remove = async (accessId: string) => {
    if (acting) return
    setActing(true)
    setActionError(null)
    try {
      const removed = await teamLabServiceAccessApi.remove(runtime.id, accessId)
      await request.mutate(current => current?.map(item => item.id === removed.id ? removed : item), { revalidate: false })
    } catch (error) {
      setActionError(error)
    } finally { setActing(false) }
  }

  return <section aria-labelledby="service-access-title" className={styles.panel}>
    <header className={styles.panelHeader}><div><span>公网入口</span><h3 id="service-access-title">服务开放</h3></div></header>
    {!asset ? <DataState description="运行资产需要先接入网段并取得地址。" title="暂无可开放服务的资产" /> : <>
      <div className={styles.serviceAccessForm}>
        <label>资产<select value={asset.id} onChange={event => setAssetId(Number(event.target.value))}>
          {assets.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}
        </select></label>
        <label>目标网段<select value={networkKey} onChange={event => setNetworkKey(event.target.value)}>
          {asset.networkKeys.map(key => <option key={key} value={key}>{runtime.networks.find(item => item.key === key)?.name ?? key}</option>)}
        </select></label>
        <label>协议<select value={protocol} onChange={event => setProtocol(event.target.value as 'tcp' | 'udp')}>
          <option value="tcp">TCP</option><option value="udp">UDP</option>
        </select></label>
        <label>内部端口<input min={1} max={65535} type="number" value={internalPort} onChange={event => setInternalPort(Number(event.target.value))} /></label>
        <label className={styles.serviceAccessToggle}><input checked={automaticPort} type="checkbox" onChange={event => setAutomaticPort(event.target.checked)} />自动分配公网端口</label>
        {!automaticPort ? <label>公网端口<input min={1} max={65535} type="number" value={publicPort} onChange={event => setPublicPort(Number(event.target.value))} /></label> : null}
        <ActionButton disabled={acting || runtime.status !== 'running' || !networkKey || internalPort < 1 || internalPort > 65535 || (!automaticPort && (publicPort < 1 || publicPort > 65535))}
          icon={<Plus size={16} />} onClick={() => void create()} tone="primary" type="button">开放访问</ActionButton>
      </div>
      {actionError ? <InlineFeedback tone="danger">{errorMessage(actionError, '服务开放操作失败。')}</InlineFeedback> : null}
      {!request.data && !request.error ? <DataState loading title="正在读取服务开放记录" />
        : request.error ? <InlineFeedback tone="danger">{errorMessage(request.error, '服务开放记录读取失败。')}</InlineFeedback>
          : request.data?.length ? <div className={styles.fileTable}><table><thead><tr><th>资产</th><th>访问地址</th><th>转发目标</th><th>状态</th><th>操作</th></tr></thead>
            <tbody>{request.data.map(item => <tr key={item.id}><td>{item.assetName}</td><td><code>{item.endpoint}</code></td>
              <td>{item.protocol.toUpperCase()} / {item.internalPort}</td><td>{statusText(item.status)}<small>{item.lastError ? `：${item.lastError}` : ` · ${formatAdminDate(item.createdAt)}`}</small></td>
              <td>{item.status === 'active' ? <ActionButton disabled={acting} icon={<Unplug size={15} />} onClick={() => void remove(item.id)} tone="danger" type="button">撤销</ActionButton>
                : '-'}</td></tr>)}</tbody></table></div>
            : <DataState description="开放后，平台会在这里给出服务器公网地址。" title="暂无服务开放记录" />}
    </>}
  </section>
}

function statusText(status: string) {
  if (status === 'active') return '已开放'
  if (status === 'revoked') return '已撤销'
  if (status === 'failed') return '开放失败'
  return status
}
