import { Copy, KeyRound } from 'lucide-react'
import { useState } from 'react'
import { ApiTokenModel, Role } from '@Api'
import { ActionButton, InlineFeedback, VNextConfirmDialog, VNextDialog } from '../../shared/Interaction'
import { DataState, StatusPill } from '../../shared/Primitives'
import { errorMessage } from '../../shared/errors'
import { useCurrentAccount } from '../account/useCurrentAccount'
import { settingsApi, useApiTokens } from './settingsApi'
import { TokenCreateDialog } from './TokenCreateDialog'
import styles from './SettingsPage.module.css'

function formatTime(value?: number | null) {
  return value ? new Intl.DateTimeFormat('zh-CN', { dateStyle: 'medium', timeStyle: 'short' }).format(value) : '未设置'
}

function grantedScopes(token: ApiTokenModel) {
  return (token.resources ?? []).filter((resource) => resource.resourceType === 'teamlab-scope')
}

function TokenRow({ token, onRevoke }: { token: ApiTokenModel; onRevoke: (id: string) => void }) {
  const expired = Boolean(token.expiresAt && token.expiresAt <= Date.now())
  const granted = grantedScopes(token)
  return (
    <tr>
      <td>
        <strong>{token.name || '未命名'}</strong>
        <small>{token.id}</small>
      </td>
      <td>
        <div className={styles.scopeList}>
          {(token.scopes ?? []).map((scope) => (
            <span key={scope}>{scope}</span>
          ))}
        </div>
      </td>
      <td>
        {granted.length ? (
          <div className={styles.scopeList}>
            {granted.map((resource) => (
              <span key={resource.resourceId ?? resource.resourceType}>
                {resource.resourceId ? resource.resourceId.slice(0, 8) : '—'}…
              </span>
            ))}
          </div>
        ) : (
          <span className={styles.mutedText}>无</span>
        )}
      </td>
      <td>{token.requestsPerMinute ?? '-'} / min</td>
      <td>{formatTime(token.lastUsedAt)}</td>
      <td>{formatTime(token.expiresAt)}</td>
      <td>
        <StatusPill tone={expired ? 'neutral' : 'success'}>{expired ? '已过期' : '有效'}</StatusPill>
      </td>
      <td>
        <button
          className={styles.revokeButton}
          disabled={expired}
          onClick={() => token.id && onRevoke(token.id)}
          type="button"
        >
          撤销
        </button>
      </td>
    </tr>
  )
}

function SecretDialog({ onClose, secret }: { onClose: () => void; secret: string | null }) {
  return (
    <VNextDialog
      description="关闭后无法再次查看明文，请立即存放到安全位置。"
      eyebrow="TOKEN ISSUED"
      footer={
        <ActionButton onClick={onClose} tone="primary" type="button">
          我已保存
        </ActionButton>
      }
      onClose={onClose}
      open={secret !== null}
      title="API Token 已创建"
    >
      <div className={styles.secretBox}>
        <code>{secret}</code>
        <ActionButton
          icon={<Copy size={16} />}
          onClick={() => secret && void navigator.clipboard.writeText(secret)}
          type="button"
        >
          复制
        </ActionButton>
      </div>
    </VNextDialog>
  )
}

export function TokenSettings() {
  const { user, isTeacher } = useCurrentAccount()
  const { data: tokens, error, mutate } = useApiTokens()
  const [createOpen, setCreateOpen] = useState(false)
  const [secret, setSecret] = useState<string | null>(null)
  const [revokeId, setRevokeId] = useState<string | null>(null)
  const [feedback, setFeedback] = useState<string | null>(null)
  const activeTokens = (tokens ?? []).filter((token) => !token.revokedAt)

  const canGrantScopes = user?.role === Role.Admin || user?.role === Role.SuperAdmin

  const revoke = async () => {
    if (!revokeId) return false
    setFeedback(null)
    try {
      await settingsApi.revokeToken(revokeId)
      await mutate()
      return true
    } catch (requestError) {
      setFeedback(errorMessage(requestError, 'Token 撤销失败。'))
      return false
    }
  }

  if (!isTeacher) {
    return (
      <div className={styles.settingsContent}>
        <section className={styles.sectionIntroRow}>
          <div>
            <span>API ACCESS</span>
            <h2>API Token</h2>
            <p>API Token 仅对教师及更高权限开放。如需程序化访问，请联系平台管理员。</p>
          </div>
        </section>
      </div>
    )
  }

  return (
    <div className={styles.settingsContent}>
      <section className={styles.sectionIntroRow}>
        <div>
          <span>API ACCESS</span>
          <h2>API Token</h2>
          <p>用于镜像、题目、TeamLab 控制面和异步操作接口。Token 明文仅在创建成功后显示一次。</p>
        </div>
        <ActionButton icon={<KeyRound size={16} />} onClick={() => setCreateOpen(true)} tone="primary" type="button">
          创建 Token
        </ActionButton>
      </section>
      {feedback || error ? <InlineFeedback tone="danger">{feedback || 'Token 列表加载失败。'}</InlineFeedback> : null}
      {!tokens && !error ? (
        <DataState description="正在读取 Token 列表。" loading title="Token 加载中" />
      ) : (
        <div className={styles.tableWrap}>
          <table className={styles.table}>
            <thead>
              <tr>
                <th>名称</th>
                <th>权限范围</th>
                <th>资源授权</th>
                <th>配额</th>
                <th>最后使用</th>
                <th>过期时间</th>
                <th>状态</th>
                <th />
              </tr>
            </thead>
            <tbody>
              {activeTokens.map((token) => (
                <TokenRow key={token.id} onRevoke={(id) => setRevokeId(id)} token={token} />
              ))}
              {!activeTokens.length ? (
                <tr>
                  <td className={styles.emptyCell} colSpan={8}>
                    尚未创建 API Token。
                  </td>
                </tr>
              ) : null}
            </tbody>
          </table>
        </div>
      )}

      <TokenCreateDialog
        canGrantScopes={canGrantScopes}
        onClose={() => setCreateOpen(false)}
        onIssued={(issued) => {
          setSecret(issued)
          void mutate()
        }}
        open={createOpen}
      />

      <SecretDialog onClose={() => setSecret(null)} secret={secret} />

      <VNextConfirmDialog
        confirmLabel="撤销 Token"
        message="撤销后使用此凭据的脚本和集成会立即失去访问权限，并且无法恢复。"
        onClose={() => setRevokeId(null)}
        onConfirm={revoke}
        open={revokeId !== null}
        title="确认撤销此 Token？"
      />
    </div>
  )
}
