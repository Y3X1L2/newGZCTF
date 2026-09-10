import { Download, FileCheck, RefreshCw } from 'lucide-react'
import { ActionButton, InlineFeedback } from '../../../../shared/Interaction'
import { DataState } from '../../../../shared/Primitives'
import { errorMessage } from '../../../../shared/errors'
import { formatAdminDate } from '../../shared/adminFormat'
import { useRemoteAudit } from './useRemoteAudit'
import styles from './RuntimePanels.module.css'

export function RemoteAuditPanel({ sessionId }: { sessionId: string }) {
  const audit = useRemoteAudit(sessionId)
  return <section className={styles.panel} aria-label="会话操作审计证据">
    <header className={styles.panelHeader}><h3>会话操作审计证据</h3>
      <ActionButton icon={<RefreshCw size={16} />} onClick={() => void audit.mutate()} type="button">刷新审计</ActionButton>
    </header>
    {audit.error ? <InlineFeedback tone="danger">{errorMessage(audit.error, '审计文件读取失败。')}</InlineFeedback>
      : !audit.data ? <DataState loading title="正在读取审计文件" /> : <>
        <p>保留期 {audit.data.retentionDays} 天</p>
        {audit.data.state === 'session-active' ? <DataState title="会话尚未结束" /> : null}
        {audit.data.state === 'expired' ? <DataState title="已超过审计保留期" /> : null}
        {audit.data.state === 'pending' ? <ActionButton icon={<FileCheck size={16} />} disabled={audit.generating}
          onClick={() => void audit.generate()} type="button">{audit.generating ? '正在归档' : '归档操作证据'}</ActionButton> : null}
        {audit.data.items.map(file => <div key={file.id} className={styles.auditFile}>
          <dl className={styles.diagnosticsFacts}>
            <div><dt>文件大小</dt><dd>{file.size} B</dd></div>
            <div><dt>归档时间</dt><dd>{formatAdminDate(file.createdAt)}</dd></div>
            <div><dt>到期时间</dt><dd>{file.expiresAt ? formatAdminDate(file.expiresAt) : '-'}</dd></div>
            <div><dt>SHA-256</dt><dd>{file.sha256}</dd></div>
          </dl>
          <ActionButton icon={<Download size={16} />} disabled={audit.downloading}
            onClick={() => void audit.download(file.id)} type="button">下载证据 JSON</ActionButton>
        </div>)}
      </>}
    {audit.actionError ? <InlineFeedback tone="danger">{errorMessage(audit.actionError, '审计归档失败。')}</InlineFeedback> : null}
  </section>
}
