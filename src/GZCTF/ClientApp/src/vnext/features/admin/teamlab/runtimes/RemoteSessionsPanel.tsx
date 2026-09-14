import { FileClock, RefreshCw, Search, Square } from 'lucide-react'
import { useState } from 'react'
import useSWR from 'swr'
import { ActionButton, InlineFeedback, VNextConfirmDialog } from '../../../../shared/Interaction'
import { DataState } from '../../../../shared/Primitives'
import { errorMessage } from '../../../../shared/errors'
import { DetailDrawer } from '../../shared/AdminWorkbench'
import { formatAdminDate } from '../../shared/adminFormat'
import { teamLabRemoteAccessApi, type TeamLabRemoteSession } from '../api'
import { RemoteAuditPanel } from './RemoteAuditPanel'
import styles from './RuntimePanels.module.css'

const statusLabels = {
  creating: '创建中',
  ready: '待连接',
  connected: '已连接',
  ending: '等待清理',
  ended: '已结束',
  failed: '失败',
}
const protocolLabels = { containerTerminal: '容器终端', ssh: 'SSH', rdp: 'RDP', vnc: 'VNC' }

export function RemoteSessionsPanel({ runtimeId }: { runtimeId?: string }) {
  const [status, setStatus] = useState('')
  const [protocol, setProtocol] = useState('')
  const [abnormalOnly, setAbnormalOnly] = useState(false)
  const [query, setQuery] = useState('')
  const [search, setSearch] = useState('')
  const [after, setAfter] = useState<number | undefined>()
  const [selected, setSelected] = useState<TeamLabRemoteSession | null>(null)
  const [auditSession, setAuditSession] = useState<TeamLabRemoteSession | null>(null)
  const [error, setError] = useState<unknown>(null)
  const sessions = useSWR(
    ['teamlab:remote-sessions', runtimeId, status, protocol, abnormalOnly, search, after],
    () =>
      teamLabRemoteAccessApi.list({
        runtimeId,
        status: status ? Number(status) : undefined,
        protocol: protocol ? (protocol as keyof typeof protocolLabels) : undefined,
        abnormalOnly,
        query: search || undefined,
        after,
      }),
    {
      refreshInterval: (latest) =>
        latest?.items.some((item) => !['ended', 'failed'].includes(item.session.status)) ? 5000 : 0,
    }
  )
  const end = async () => {
    if (!selected) return false
    setError(null)
    try {
      await teamLabRemoteAccessApi.end(selected.id)
      await sessions.mutate()
      return true
    } catch (nextError) {
      setError(nextError)
      await sessions.mutate()
      return false
    }
  }
  return (
    <section className={styles.panel}>
      <header className={styles.panelHeader}>
        <h3>远程会话</h3>
        <ActionButton icon={<RefreshCw size={16} />} onClick={() => void sessions.mutate()} type="button">
          刷新
        </ActionButton>
      </header>
      <form
        className={`${styles.captureForm} ${styles.remoteSessionFilters}`}
        onSubmit={(event) => {
          event.preventDefault()
          setAfter(undefined)
          setSearch(query.trim())
        }}
      >
        <label>
          <span>关键词</span>
          <input
            placeholder="用户名、资产名、节点名或会话号"
            value={query}
            onChange={(event) => setQuery(event.currentTarget.value)}
          />
        </label>
        <label>
          <span>会话状态</span>
          <select
            value={status}
            onChange={(event) => {
              setStatus(event.currentTarget.value)
              setAfter(undefined)
            }}
          >
            <option value="">全部状态</option>
            {Object.entries(statusLabels).map(([key, label], index) => (
              <option key={key} value={index + 1}>
                {label}
              </option>
            ))}
          </select>
        </label>
        <label>
          <span>连接方式</span>
          <select
            value={protocol}
            onChange={(event) => {
              setProtocol(event.currentTarget.value)
              setAfter(undefined)
            }}
          >
            <option value="">全部方式</option>
            {Object.entries(protocolLabels).map(([key, label]) => (
              <option key={key} value={key}>
                {label}
              </option>
            ))}
          </select>
        </label>
        <label>
          <span>仅看异常</span>
          <input
            checked={abnormalOnly}
            onChange={(event) => {
              setAbnormalOnly(event.currentTarget.checked)
              setAfter(undefined)
            }}
            type="checkbox"
          />
        </label>
        <ActionButton icon={<Search size={16} />} type="submit">
          查询
        </ActionButton>
      </form>
      {sessions.error ? (
        <InlineFeedback tone="danger">{errorMessage(sessions.error, '会话列表读取失败。')}</InlineFeedback>
      ) : sessions.isLoading ? (
        <DataState loading title="正在读取会话" />
      ) : !sessions.data?.items.length ? (
        <DataState title="没有符合条件的会话" />
      ) : (
        <div className={styles.sessionTableScroll}>
          <table className={styles.sessionTable}>
            <thead>
              <tr>
                <th>资产</th>
                <th>状态 / 连接方式</th>
                <th>操作者 / 节点</th>
                <th>原因 / 到期时间</th>
                <th>操作</th>
              </tr>
            </thead>
            <tbody>
              {sessions.data.items.map(
                ({ session, workerNodeId, workerNodeName, requestedByUserId, requestedByName }) => (
                  <tr key={session.id} title={`会话 ${session.id} · 用户 ${requestedByUserId} · 节点 ${workerNodeId}`}>
                    <td>{session.assetName}</td>
                    <td>
                      {session.endReason === 'service_restarted' ? '连接中断' : statusLabels[session.status]}
                      <small>{protocolLabels[session.protocol]}</small>
                      {session.endReason && session.endReason !== 'service_restarted' ? (
                        <small>{session.endReason}</small>
                      ) : null}
                    </td>
                    <td>
                      {requestedByName}
                      <small>{workerNodeName}</small>
                    </td>
                    <td>
                      {session.reason}
                      <small>{formatAdminDate(session.expiresAt)}</small>
                    </td>
                    <td>
                      {!['ended', 'failed'].includes(session.status) ? (
                        <ActionButton icon={<Square size={14} />} onClick={() => setSelected(session)} type="button">
                          {session.status === 'ending' ? '重试清理' : '结束'}
                        </ActionButton>
                      ) : null}
                      <ActionButton
                        aria-label={`查看 ${session.assetName} 操作审计`}
                        aria-pressed={auditSession?.id === session.id}
                        icon={<FileClock size={14} />}
                        onClick={() => setAuditSession(session)}
                        tone={auditSession?.id === session.id ? 'primary' : 'secondary'}
                        type="button"
                      >
                        操作审计
                      </ActionButton>
                    </td>
                  </tr>
                )
              )}
            </tbody>
          </table>
        </div>
      )}
      <div className={styles.captureActions}>
        {after ? (
          <ActionButton onClick={() => setAfter(undefined)} type="button">
            返回首页
          </ActionButton>
        ) : null}
        {sessions.data?.nextCursor ? (
          <ActionButton onClick={() => setAfter(sessions.data!.nextCursor!)} type="button">
            下一页
          </ActionButton>
        ) : null}
      </div>
      {error ? <InlineFeedback tone="danger">{errorMessage(error, '结束会话失败。')}</InlineFeedback> : null}
      <DetailDrawer
        description={auditSession ? `会话 ${auditSession.id} · ${protocolLabels[auditSession.protocol]}` : undefined}
        onClose={() => setAuditSession(null)}
        open={auditSession !== null}
        title={auditSession ? `操作审计 · ${auditSession.assetName}` : '操作审计'}
      >
        {auditSession ? <RemoteAuditPanel key={auditSession.id} sessionId={auditSession.id} /> : null}
      </DetailDrawer>
      <VNextConfirmDialog
        open={selected !== null}
        title="结束远程会话"
        message={selected?.assetName ?? ''}
        description="连接将断开，临时访问资源将被回收。"
        confirmLabel="结束会话"
        onClose={() => setSelected(null)}
        onConfirm={end}
      />
    </section>
  )
}
