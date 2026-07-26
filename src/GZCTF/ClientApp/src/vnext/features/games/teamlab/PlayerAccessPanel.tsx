import { Download, KeyRound } from 'lucide-react'
import { useState } from 'react'
import { ActionButton, InlineFeedback } from '../../../shared/Interaction'
import { errorMessage } from '../../../shared/errors'
import { teamLabPlayerApi, type TeamLabPlayerAccessGrant } from './api'
import styles from './TeamLabWorkspacePage.module.css'

export function PlayerAccessPanel({ gameId, ready }: { gameId: number; ready: boolean }) {
  const [grant, setGrant] = useState<TeamLabPlayerAccessGrant | null>(null)
  const [creating, setCreating] = useState(false)
  const [error, setError] = useState<unknown>(null)

  const create = async () => {
    if (!ready || creating) return
    setCreating(true)
    setError(null)
    try {
      setGrant(await teamLabPlayerApi.createAccessGrant(gameId))
    } catch (reason) {
      setError(reason)
    } finally {
      setCreating(false)
    }
  }

  return (
    <section className={styles.accessPanel}>
      <header><div><span>WIREGUARD ACCESS</span><h2>环境接入</h2></div></header>
      {grant ? (
        <dl className={styles.accessFacts}>
          <div><dt>客户端地址</dt><dd>{grant.clientAddress}</dd></div>
          <div><dt>服务端</dt><dd>{grant.endpoint}</dd></div>
          <div><dt>允许网段</dt><dd>{grant.allowedIps}</dd></div>
          <div><dt>DNS</dt><dd>{grant.dns}</dd></div>
        </dl>
      ) : null}
      <div className={styles.accessActions}>
        <ActionButton disabled={!ready || creating} icon={<KeyRound size={16} />} onClick={() => void create()} type="button">
          {creating ? '正在签发' : grant ? '重新签发' : '获取 VPN 配置'}
        </ActionButton>
        {grant?.configurationDownloadUrl ? (
          <a className={styles.downloadLink} download href={grant.configurationDownloadUrl}>
            <Download size={16} />下载配置
          </a>
        ) : null}
      </div>
      {error ? <InlineFeedback tone="danger">{errorMessage(error, 'VPN 配置签发失败。')}</InlineFeedback> : null}
    </section>
  )
}
