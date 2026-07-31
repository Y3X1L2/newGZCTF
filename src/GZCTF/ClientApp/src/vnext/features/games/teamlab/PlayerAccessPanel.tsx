import { Download, KeyRound } from 'lucide-react'
import { useEffect, useState } from 'react'
import { ActionButton, InlineFeedback, VNextConfirmDialog } from '../../../shared/Interaction'
import { errorMessage } from '../../../shared/errors'
import styles from './TeamLabWorkspacePage.module.css'
import { teamLabPlayerApi, type TeamLabPlayerAccessGrant } from './api'
import { clearPlayerAccessGrant, loadPlayerAccessGrant, savePlayerAccessGrant } from './playerAccessGrantSession'

export function PlayerAccessPanel({ gameId, runtimeId, ready }: { gameId: number; runtimeId: string; ready: boolean }) {
  const [grant, setGrant] = useState<TeamLabPlayerAccessGrant | null>(() => loadPlayerAccessGrant(gameId, runtimeId))
  const [confirmOpen, setConfirmOpen] = useState(false)
  const [creating, setCreating] = useState(false)
  const [error, setError] = useState<unknown>(null)

  useEffect(() => {
    if (ready) {
      setGrant(loadPlayerAccessGrant(gameId, runtimeId))
      return
    }
    clearPlayerAccessGrant(gameId, runtimeId)
    setGrant(null)
  }, [gameId, ready, runtimeId])

  const create = async () => {
    if (!ready || creating) return false
    setCreating(true)
    setError(null)
    try {
      const next = await teamLabPlayerApi.createAccessGrant(gameId)
      savePlayerAccessGrant(gameId, runtimeId, next)
      setGrant(next)
      return true
    } catch (reason) {
      setError(reason)
      return false
    } finally {
      setCreating(false)
    }
  }

  return (
    <section className={styles.accessPanel}>
      <header>
        <div>
          <span>WIREGUARD ACCESS</span>
          <h2>环境接入</h2>
        </div>
      </header>
      {grant ? (
        <dl className={styles.accessFacts}>
          <div>
            <dt>客户端地址</dt>
            <dd>{grant.clientAddress}</dd>
          </div>
          <div>
            <dt>服务端</dt>
            <dd>{grant.endpoint}</dd>
          </div>
          <div>
            <dt>允许网段</dt>
            <dd>{grant.allowedIps}</dd>
          </div>
          <div>
            <dt>DNS</dt>
            <dd>{grant.dns}</dd>
          </div>
        </dl>
      ) : null}
      <div className={styles.accessActions}>
        <ActionButton
          disabled={!ready || creating}
          icon={<KeyRound size={16} />}
          onClick={() => setConfirmOpen(true)}
          type="button"
        >
          {grant ? '替换 VPN 配置' : '获取 VPN 配置'}
        </ActionButton>
        {grant?.configurationDownloadUrl ? (
          <a className={styles.downloadLink} download href={grant.configurationDownloadUrl}>
            <Download size={16} />
            下载配置
          </a>
        ) : null}
      </div>
      {error ? <InlineFeedback tone="danger">{errorMessage(error, 'VPN 配置签发失败。')}</InlineFeedback> : null}
      <VNextConfirmDialog
        confirmLabel={grant ? '确认替换' : '确认签发'}
        description="每个队伍环境同时只保留一份有效的 WireGuard 配置。"
        message={
          <>
            签发新配置会立即使队伍此前下载的 VPN 配置失效，请先确认没有队友仍在使用。
            {error ? <InlineFeedback tone="danger">{errorMessage(error, 'VPN 配置签发失败。')}</InlineFeedback> : null}
          </>
        }
        onClose={() => setConfirmOpen(false)}
        onConfirm={create}
        open={confirmOpen}
        title={grant ? '替换队伍 VPN 配置' : '签发队伍 VPN 配置'}
        tone="primary"
      />
    </section>
  )
}
