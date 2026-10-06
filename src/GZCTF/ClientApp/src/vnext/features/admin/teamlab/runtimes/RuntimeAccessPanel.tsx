import { Download, KeyRound, RefreshCw, ShieldOff } from 'lucide-react'
import { useState } from 'react'
import useSWR from 'swr'
import { ActionButton, InlineFeedback, VNextConfirmDialog } from '../../../../shared/Interaction'
import { DataState } from '../../../../shared/Primitives'
import { errorMessage } from '../../../../shared/errors'
import { formatAdminDate } from '../../shared/adminFormat'
import { teamLabRuntimeApi, teamLabRuntimeKeys } from '../api'
import styles from './RuntimePanels.module.css'

export function RuntimeAccessPanel({ runtimeId, canCreate }: { runtimeId: string; canCreate: boolean }) {
  const [acting, setActing] = useState(false)
  const [actionError, setActionError] = useState<unknown>(null)
  const [pendingRevoke, setPendingRevoke] = useState<string | null>(null)
  const request = useSWR(
    teamLabRuntimeKeys.accessGrants(runtimeId),
    () => teamLabRuntimeApi.listAccessGrants(runtimeId),
    { revalidateOnFocus: true }
  )

  const create = async () => {
    if (acting) return false
    setActing(true)
    setActionError(null)
    try {
      await teamLabRuntimeApi.createAccessGrant(runtimeId)
      await request.mutate()
    } catch (error) {
      setActionError(error)
    } finally {
      setActing(false)
    }
  }

  const revoke = async (grantId: string) => {
    if (acting) return false
    setActing(true)
    setActionError(null)
    try {
      await teamLabRuntimeApi.revokeAccessGrant(runtimeId, grantId)
      await request.mutate((current) => current?.filter((grant) => grant.id !== grantId), { revalidate: false })
      return true
    } catch (error) {
      setActionError(error)
      return false
    } finally {
      setActing(false)
    }
  }

  return (
    <section aria-labelledby="runtime-access-title" className={styles.panel}>
      <header className={styles.panelHeader}>
        <h3 id="runtime-access-title">WireGuard 授权</h3>
        <ActionButton disabled={!canCreate || acting} icon={<KeyRound size={16} />} onClick={() => void create()} type="button">
          新增授权
        </ActionButton>
      </header>
      {!request.data && !request.error ? (
        <DataState description="" loading title="配置加载中" />
      ) : request.error ? (
        <InlineFeedback tone="danger">{errorMessage(request.error, '授权读取失败。')}
          <ActionButton aria-label="重新读取 VPN 授权" icon={<RefreshCw size={15} />} onClick={() => void request.mutate()} type="button" />
        </InlineFeedback>
      ) : request.data?.length ? (
        <div className={styles.accessList}>
          {request.data.map((grant) => (
            <article key={grant.id}>
              <div><strong>{grant.clientAddress}</strong><code>{grant.endpoint}</code><small>允许网段 {grant.allowedIps || '未提供'}</small><small>到期 {grant.expiresAt ? formatAdminDate(grant.expiresAt) : '未设置'}</small></div>
              {grant.configurationDownloadUrl ? (
                <a className={styles.downloadLink} download href={grant.configurationDownloadUrl}><Download size={15} />下载现有配置</a>
              ) : null}
              <ActionButton disabled={!canCreate || acting} icon={<ShieldOff size={15} />} onClick={() => setPendingRevoke(grant.id)} tone="danger" type="button">
                撤销
              </ActionButton>
            </article>
          ))}
        </div>
      ) : (
        <p className={styles.muted}>暂无接入配置</p>
      )}
      {actionError ? <InlineFeedback tone="danger">{errorMessage(actionError, '授权操作失败。')}</InlineFeedback> : null}
      <VNextConfirmDialog open={pendingRevoke !== null} onClose={() => setPendingRevoke(null)} title="撤销 VPN 授权"
        message="此配置将无法再用于接入当前环境。" confirmLabel="撤销授权" tone="danger"
        onConfirm={() => pendingRevoke ? revoke(pendingRevoke) : Promise.resolve(false)} />
    </section>
  )
}
