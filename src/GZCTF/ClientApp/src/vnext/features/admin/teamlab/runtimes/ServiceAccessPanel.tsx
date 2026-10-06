import { Plus, RefreshCw, Unplug } from 'lucide-react'
import { useEffect, useMemo, useState } from 'react'
import useSWR from 'swr'
import { ActionButton, InlineFeedback, VNextConfirmDialog } from '../../../../shared/Interaction'
import { DataState } from '../../../../shared/Primitives'
import { errorMessage } from '../../../../shared/errors'
import { formatAdminDate } from '../../shared/adminFormat'
import type { TeamLabRuntime } from '../api'
import { teamLabServiceAccessApi, teamLabServiceAccessKeys } from '../api/teamlabServiceAccessApi'
import styles from './RuntimePanels.module.css'

export function ServiceAccessPanel({ runtime, assetId }: { runtime: TeamLabRuntime; assetId?: number }) {
  const assets = runtime.assets
  const [selectedId, setSelectedId] = useState(assetId ?? assets.find(item => item.interfaces?.some(nic => nic.assigned))?.id ?? assets[0]?.id ?? 0)
  const asset = useMemo(() => assets.find(item => item.id === selectedId), [assets, selectedId])
  const [formOpen, setFormOpen] = useState(false)
  const [networkKey, setNetworkKey] = useState(asset?.interfaces?.find(nic => nic.assigned)?.networkKey ?? '')
  const [protocol, setProtocol] = useState<'tcp' | 'udp'>('tcp')
  const [internalPort, setInternalPort] = useState(80)
  const [automaticPort, setAutomaticPort] = useState(true)
  const [publicPort, setPublicPort] = useState(30000)
  const [acting, setActing] = useState(false)
  const [actionError, setActionError] = useState<unknown>(null)
  const [pendingRemove, setPendingRemove] = useState<string | null>(null)
  const request = useSWR(teamLabServiceAccessKeys.list(runtime.id), () => teamLabServiceAccessApi.list(runtime.id))
  const selectedInterface = asset?.interfaces?.find(nic => nic.networkKey === networkKey && nic.assigned)

  useEffect(() => {
    if (!asset) return
    if (!asset.interfaces?.some(nic => nic.networkKey === networkKey && nic.assigned))
      setNetworkKey(asset.interfaces?.find(nic => nic.assigned)?.networkKey ?? '')
  }, [asset, networkKey])

  const create = async () => {
    if (!asset || !selectedInterface || acting) return
    setActing(true)
    setActionError(null)
    try {
      const created = await teamLabServiceAccessApi.create(runtime.id, asset.id, {
        protocol,
        internalPort,
        publicPort: automaticPort ? null : publicPort,
        networkKey,
      })
      if (created.status === 'failed') throw new Error(created.lastError ?? '开放服务失败。')
      await request.mutate()
      setFormOpen(false)
    } catch (error) {
      setActionError(error)
    } finally { setActing(false) }
  }

  const remove = async (accessId: string) => {
    if (acting) return false
    setActing(true)
    setActionError(null)
    try {
      const removed = await teamLabServiceAccessApi.remove(runtime.id, accessId)
      await request.mutate(current => current?.map(item => item.id === removed.id ? removed : item), { revalidate: false })
      return true
    } catch (error) {
      setActionError(error)
      return false
    } finally { setActing(false) }
  }

  return <section aria-labelledby="service-access-title" className={styles.panel}>
    <header className={styles.panelHeader}><h3 id="service-access-title">业务访问入口</h3>
      <ActionButton icon={<Plus size={16} />} disabled={!assets.some(item => item.interfaces?.some(nic => nic.assigned)) || runtime.status !== 'running' || !!runtime.managedRolloutId} onClick={() => setFormOpen(value => !value)} type="button">添加访问入口</ActionButton>
    </header>
    <p className={styles.muted}>端口映射建立后仍需由使用者验证目标服务，映射状态不代表业务可用。</p>
    {!asset ? <p className={styles.muted}>当前环境没有机器。</p> : <>
      {formOpen ? <div className={styles.serviceAccessForm}>
        <label>目标机器<select value={selectedId} onChange={event => setSelectedId(Number(event.target.value))}>
          {assets.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}
        </select></label>
        <label>目标网段<select value={networkKey} onChange={event => setNetworkKey(event.target.value)}>
          {asset.interfaces?.filter(nic => nic.assigned).map(nic => <option key={nic.key} value={nic.networkKey}>
            {runtime.networks.find(item => item.key === nic.networkKey)?.name ?? nic.networkKey} · {nic.assigned?.ipAddress}
          </option>)}
        </select></label>
        <label>协议<select value={protocol} onChange={event => setProtocol(event.target.value as 'tcp' | 'udp')}>
          <option value="tcp">TCP</option><option value="udp">UDP</option>
        </select></label>
        <label>内部端口<input min={1} max={65535} type="number" value={internalPort} onChange={event => setInternalPort(Number(event.target.value))} /></label>
        <label className={styles.serviceAccessToggle}><input checked={automaticPort} type="checkbox" onChange={event => setAutomaticPort(event.target.checked)} />自动分配公网端口</label>
        {!automaticPort ? <label>公网端口<input min={1} max={65535} type="number" value={publicPort} onChange={event => setPublicPort(Number(event.target.value))} /></label> : null}
        <ActionButton disabled={acting || runtime.status !== 'running' || !!runtime.managedRolloutId || !selectedInterface || internalPort < 1 || internalPort > 65535 || (!automaticPort && (publicPort < 1 || publicPort > 65535))}
          icon={<Plus size={16} />} onClick={() => void create()} tone="primary" type="button">开放访问</ActionButton>
      </div> : null}
      {actionError ? <InlineFeedback tone="danger">{errorMessage(actionError, '服务开放操作失败。')}</InlineFeedback> : null}
      {!request.data && !request.error ? <DataState loading title="正在读取访问入口" />
        : request.error ? <InlineFeedback tone="danger">{errorMessage(request.error, '服务开放记录读取失败。')}
            <ActionButton aria-label="重新读取访问入口" icon={<RefreshCw size={15} />} onClick={() => void request.mutate()} type="button" />
          </InlineFeedback>
          : request.data?.some(item => item.revokedAt === null) ? <div className={styles.fileTable}><table><thead><tr><th>外部地址</th><th>目标机器 / 网卡 / 端口</th><th>映射状态</th><th>操作</th></tr></thead>
            <tbody>{request.data.filter(item => item.revokedAt === null).map(item => {
              const target = assets.find(asset => asset.id === item.assetId)
              const targetInterface = target?.interfaces?.find(nic => nic.networkKey === item.networkKey)
              return <tr key={item.id}><td><code className={styles.serviceEndpoint}>{item.endpoint}</code></td>
              <td>{item.assetName} · {targetInterface?.assigned?.ipAddress ?? '地址未核对'}:{item.internalPort} / {item.protocol.toUpperCase()}</td>
              <td>{statusText(item.status)}<small>{item.lastError ? `：${item.lastError}` : ` · ${formatAdminDate(item.createdAt)}`}</small></td>
              <td>{item.status === 'active' ? <ActionButton disabled={acting || !!runtime.managedRolloutId} icon={<Unplug size={15} />} onClick={() => setPendingRemove(item.id)} tone="danger" type="button">撤销</ActionButton>
                : '-'}</td></tr>
            })}</tbody></table></div>
            : <p className={styles.muted}>暂无业务访问入口。</p>}
    </>}
    <VNextConfirmDialog open={pendingRemove !== null} onClose={() => setPendingRemove(null)} title="撤销业务访问入口"
      message="外部地址将不再转发到目标机器，现有连接可能中断。" confirmLabel="撤销入口" tone="danger"
      onConfirm={() => pendingRemove ? remove(pendingRemove) : Promise.resolve(false)} />
  </section>
}

function statusText(status: string) {
  if (status === 'active') return '已开放'
  if (status === 'revoked') return '已撤销'
  if (status === 'failed') return '开放失败'
  return status
}
