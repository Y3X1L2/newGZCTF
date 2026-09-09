import { FileClock, RefreshCw, Search, Square } from 'lucide-react'
import { useState } from 'react'
import useSWR from 'swr'
import { ActionButton, InlineFeedback, VNextConfirmDialog } from '../../../../shared/Interaction'
import { DataState } from '../../../../shared/Primitives'
import { errorMessage } from '../../../../shared/errors'
import { formatAdminDate } from '../../shared/adminFormat'
import { teamLabRemoteAccessApi, type TeamLabRemoteSession } from '../api'
import styles from './RuntimePanels.module.css'
import { RemoteAuditPanel } from './RemoteAuditPanel'

const statusLabels = { creating: '创建中', ready: '待连接', connected: '已连接', ending: '等待清理', ended: '已结束', failed: '失败' }

export function RemoteSessionsPanel({ runtimeId }: { runtimeId?: string }) {
  const [status, setStatus] = useState('')
  const [node, setNode] = useState('')
  const [operator, setOperator] = useState('')
  const [filters, setFilters] = useState({ node: '', operator: '' })
  const [after, setAfter] = useState<number | undefined>()
  const [selected, setSelected] = useState<TeamLabRemoteSession | null>(null)
  const [auditSessionId, setAuditSessionId] = useState<string | null>(null)
  const [error, setError] = useState<unknown>(null)
  const sessions = useSWR(['teamlab:remote-sessions', runtimeId, status, filters.node, filters.operator, after],
    () => teamLabRemoteAccessApi.list({ runtimeId, status: status ? Number(status) : undefined, workerNodeId: filters.node || undefined, requestedByUserId: filters.operator || undefined, after }),
    { refreshInterval: 5000 })
  const end = async () => {
    if (!selected) return false
    setError(null)
    try { await teamLabRemoteAccessApi.end(selected.id); await sessions.mutate(); return true }
    catch (nextError) { setError(nextError); await sessions.mutate(); return false }
  }
  return <section className={styles.panel}>
    <header className={styles.panelHeader}><h3>远程会话</h3><ActionButton icon={<RefreshCw size={16} />} onClick={() => void sessions.mutate()} type="button">刷新</ActionButton></header>
    <form className={`${styles.captureForm} ${styles.remoteSessionFilters}`} onSubmit={(event) => { event.preventDefault(); setAfter(undefined); setFilters({ node: node.trim(), operator: operator.trim() }) }}>
      <label><span>会话状态</span><select value={status} onChange={(event) => { setStatus(event.currentTarget.value); setAfter(undefined) }}><option value="">全部状态</option>{Object.entries(statusLabels).map(([key, label], index) => <option key={key} value={index + 1}>{label}</option>)}</select></label>
      <label><span>节点 ID</span><input pattern="[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}" title="请输入完整的 UUID" value={node} onChange={(event) => setNode(event.currentTarget.value)} /></label>
      <label><span>操作者 ID</span><input pattern="[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}" title="请输入完整的 UUID" value={operator} onChange={(event) => setOperator(event.currentTarget.value)} /></label>
      <ActionButton icon={<Search size={16} />} type="submit">查询</ActionButton>
    </form>
    {sessions.error ? <InlineFeedback tone="danger">{errorMessage(sessions.error, '会话列表读取失败。')}</InlineFeedback> : sessions.isLoading ? <DataState loading title="正在读取会话" /> : !sessions.data?.items.length ? <DataState title="没有符合条件的会话" /> : <div className={styles.sessionTableScroll}><table className={styles.sessionTable}><thead><tr><th>资产 / 会话</th><th>状态 / 协议</th><th>操作者 / 节点</th><th>原因 / 到期时间</th><th>操作</th></tr></thead><tbody>{sessions.data.items.map(({ session, workerNodeId, requestedByUserId }) => <tr key={session.id}>
      <td>{session.assetName}<small>{session.id}</small></td>
      <td>{statusLabels[session.status]}<small>{session.protocol}</small>{session.endReason ? <small>{session.endReason}</small> : null}</td>
      <td><small>{requestedByUserId}</small><small>{workerNodeId}</small></td>
      <td>{session.reason}<small>{formatAdminDate(session.expiresAt)}</small></td>
      <td>{!['ended', 'failed'].includes(session.status) ? <ActionButton icon={<Square size={14} />} onClick={() => setSelected(session)} type="button">{session.status === 'ending' ? '重试清理' : '结束'}</ActionButton> : null}
        <ActionButton icon={<FileClock size={14} />} onClick={() => setAuditSessionId(session.id)} type="button">操作审计</ActionButton></td>
    </tr>)}</tbody></table></div>}
    <div className={styles.captureActions}>{after ? <ActionButton onClick={() => setAfter(undefined)} type="button">返回首页</ActionButton> : null}{sessions.data?.nextCursor ? <ActionButton onClick={() => setAfter(sessions.data!.nextCursor!)} type="button">下一页</ActionButton> : null}</div>
    {error ? <InlineFeedback tone="danger">{errorMessage(error, '结束会话失败。')}</InlineFeedback> : null}
    {auditSessionId ? <><ActionButton onClick={() => setAuditSessionId(null)} type="button">收起审计</ActionButton>
      <RemoteAuditPanel key={auditSessionId} sessionId={auditSessionId} /></> : null}
    <VNextConfirmDialog open={selected !== null} title="结束远程会话" message={selected?.assetName ?? ''} description="连接将断开，临时访问资源将被回收。" confirmLabel="结束会话" onClose={() => setSelected(null)} onConfirm={end} />
  </section>
}
