import { Download, KeyRound, ShieldOff } from 'lucide-react'
import { useState } from 'react'
import useSWR from 'swr'
import { ActionButton, InlineFeedback } from '../../../../shared/Interaction'
import { DataState } from '../../../../shared/Primitives'
import { errorMessage } from '../../../../shared/errors'
import { formatAdminDate } from '../../shared/adminFormat'
import { teamLabRuntimeApi, teamLabRuntimeKeys } from '../api'
import styles from './RuntimePanels.module.css'

export function RuntimeAccessPanel({ runtimeId, canCreate }: { runtimeId: string; canCreate: boolean }) {
  const [acting, setActing] = useState(false)
  const [actionError, setActionError] = useState<unknown>(null)
  const request = useSWR(
    teamLabRuntimeKeys.accessGrants(runtimeId),
    () => teamLabRuntimeApi.listAccessGrants(runtimeId),
    { revalidateOnFocus: true }
  )

  const create = async () => {
    if (acting) return
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
    if (acting) return
    setActing(true)
    setActionError(null)
    try {
      await teamLabRuntimeApi.revokeAccessGrant(runtimeId, grantId)
      await request.mutate((current) => current?.filter((grant) => grant.id !== grantId), { revalidate: false })
    } catch (error) {
      setActionError(error)
    } finally {
      setActing(false)
    }
  }

  return (
    <section aria-labelledby="runtime-access-title" className={styles.panel}>
      <header className={styles.panelHeader}>
        <h3 id="runtime-access-title">WireGuard 接入</h3>
        <ActionButton disabled={!canCreate || acting} icon={<KeyRound size={16} />} onClick={() => void create()} type="button">
          获取配置
        </ActionButton>
      </header>
      {!request.data && !request.error ? (
        <DataState description="" loading title="配置加载中" />
      ) : request.error ? (
        <InlineFeedback tone="danger">{errorMessage(request.error, '授权读取失败。')}</InlineFeedback>
      ) : request.data?.length ? (
        <div className={styles.accessList}>
          {request.data.map((grant) => (
            <article key={grant.id}>
              <div><strong>{grant.clientAddress}</strong><code>{grant.endpoint}</code><small>到期 {formatAdminDate(grant.expiresAt)}</small></div>
              {grant.configurationDownloadUrl ? (
                <a className={styles.downloadLink} href={grant.configurationDownloadUrl}><Download size={15} />下载配置</a>
              ) : null}
              <ActionButton disabled={acting} icon={<ShieldOff size={15} />} onClick={() => void revoke(grant.id)} tone="danger" type="button">
                撤销
              </ActionButton>
            </article>
          ))}
        </div>
      ) : (
        <p className={styles.muted}>暂无接入配置</p>
      )}
      {actionError ? <InlineFeedback tone="danger">{errorMessage(actionError, '授权操作失败。')}</InlineFeedback> : null}
    </section>
  )
}
