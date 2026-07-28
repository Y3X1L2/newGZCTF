import { Copy, KeyRound } from 'lucide-react'
import { useMemo, useState } from 'react'
import { ApiTokenCreateModel } from '@Api'
import { ActionButton, InlineFeedback, VNextConfirmDialog, VNextDialog } from '../../shared/Interaction'
import { DataState, StatusPill } from '../../shared/Primitives'
import { errorMessage } from '../../shared/errors'
import styles from './SettingsPage.module.css'
import { settingsApi, useApiTokens } from './settingsApi'
import { TokenResourceGrant, TokenResourceList } from './TokenResourceGrant'
const scopeOptions = [
  ['images:read', '读取镜像'],
  ['images:write', '写入镜像'],
  ['images:delete', '删除镜像'],
  ['challenges:read', '读取比赛题目'],
  ['challenges:write', '导入比赛题目'],
  ['challenges:delete', '删除比赛题目'],
  ['exercises:read', '读取练习题库'],
  ['exercises:write', '写入练习题库'],
  ['exercises:delete', '删除练习题库'],
  ['operations:read', '读取异步操作'],
] as const
function formatTime(value?: number | null) {
  return value ? new Intl.DateTimeFormat('zh-CN', { dateStyle: 'medium', timeStyle: 'short' }).format(value) : '未设置'
}
export function TokenSettings() {
  const { data: tokens, error, mutate } = useApiTokens()
  const [createOpen, setCreateOpen] = useState(false)
  const [name, setName] = useState('')
  const [scopes, setScopes] = useState<string[]>(['images:read'])
  const [requestsPerMinute, setRequestsPerMinute] = useState(60)
  const [expiresAt, setExpiresAt] = useState('')
  const [resourceType, setResourceType] = useState('')
  const [resourceId, setResourceId] = useState('')
  const [secret, setSecret] = useState<string | null>(null)
  const [revokeId, setRevokeId] = useState<string | null>(null)
  const [submitting, setSubmitting] = useState(false)
  const [feedback, setFeedback] = useState<string | null>(null)
  const activeTokens = useMemo(() => (tokens ?? []).filter((token) => !token.revokedAt), [tokens])

  const issue = async () => {
    const model: ApiTokenCreateModel = {
      name: name.trim(),
      scopes,
      requestsPerMinute,
      expiresAt: expiresAt ? new Date(expiresAt).getTime() : null,
      resources:
        resourceType.trim() && resourceId.trim()
          ? [{ resourceType: resourceType.trim(), resourceId: resourceId.trim() }]
          : [],
    }
    setSubmitting(true)
    setFeedback(null)
    try {
      const token = await settingsApi.issueToken(model)
      setCreateOpen(false)
      setSecret(token)
      setName('')
      setScopes(['images:read'])
      setRequestsPerMinute(60)
      setExpiresAt('')
      setResourceType('')
      setResourceId('')
      await mutate()
    } catch (requestError) {
      setFeedback(errorMessage(requestError, 'Token 创建失败。'))
    } finally {
      setSubmitting(false)
    }
  }

  const revoke = async () => {
    if (!revokeId) return false
    setSubmitting(true)
    setFeedback(null)
    try {
      await settingsApi.revokeToken(revokeId)
      await mutate()
      return true
    } catch (requestError) {
      setFeedback(errorMessage(requestError, 'Token 撤销失败。'))
      return false
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <div className={styles.settingsContent}>
      <section className={styles.sectionIntroRow}>
        <div>
          <span>API ACCESS</span>
          <h2>API Token</h2>
          <p>用于镜像、题目和异步操作接口。Token 明文仅在创建成功后显示一次。</p>
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
              {activeTokens.map((token) => {
                const expired = Boolean(token.expiresAt && token.expiresAt <= Date.now())
                return (
                  <tr key={token.id}>
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
                      <div className={styles.scopeList}><TokenResourceList resources={token.resources} /></div>
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
                        onClick={() => setRevokeId(token.id ?? null)}
                        type="button"
                      >
                        撤销
                      </button>
                    </td>
                  </tr>
                )
              })}
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

      <VNextDialog
        description="选择最小必要权限；镜像写入和题目导入建议使用不同 Token。"
        eyebrow="ISSUE TOKEN"
        footer={
          <>
            <ActionButton onClick={() => setCreateOpen(false)} type="button">
              取消
            </ActionButton>
            <ActionButton
              disabled={submitting || !name.trim() || !scopes.length}
              onClick={() => void issue()}
              tone="primary"
              type="button"
            >
              确认创建
            </ActionButton>
          </>
        }
        onClose={() => setCreateOpen(false)}
        open={createOpen}
        title="创建 API Token"
      >
        <div className={styles.dialogForm}>
          <label>
            <span>名称</span>
            <input
              maxLength={128}
              onChange={(event) => setName(event.currentTarget.value)}
              placeholder="例如：镜像上传脚本"
              value={name}
            />
          </label>
          <fieldset>
            <legend>权限范围</legend>
            <div className={styles.checkGrid}>
              {scopeOptions.map(([value, label]) => (
                <label key={value}>
                  <input
                    checked={scopes.includes(value)}
                    onChange={(event) => {
                      const checked = event.currentTarget.checked
                      setScopes((current) => (checked ? [...current, value] : current.filter((item) => item !== value)))
                    }}
                    type="checkbox"
                  />
                  <span>
                    <strong>{label}</strong>
                    <small>{value}</small>
                  </span>
                </label>
              ))}
            </div>
          </fieldset>
          <div className={styles.formGrid}>
            <label>
              <span>每分钟请求数</span>
              <input
                max={10000}
                min={1}
                onChange={(event) => setRequestsPerMinute(Number(event.currentTarget.value) || 60)}
                type="number"
                value={requestsPerMinute}
              />
            </label>
            <label>
              <span>过期时间</span>
              <input
                onChange={(event) => setExpiresAt(event.currentTarget.value)}
                type="datetime-local"
                value={expiresAt}
              />
            </label>
          </div>
          <TokenResourceGrant className={styles.formGrid} resourceId={resourceId} resourceType={resourceType} onResourceIdChange={setResourceId} onResourceTypeChange={setResourceType} />
          {feedback ? <InlineFeedback tone="danger">{feedback}</InlineFeedback> : null}
        </div>
      </VNextDialog>

      <VNextDialog
        description="关闭后无法再次查看明文，请立即存放到安全位置。"
        eyebrow="TOKEN ISSUED"
        footer={
          <ActionButton onClick={() => setSecret(null)} tone="primary" type="button">
            我已保存
          </ActionButton>
        }
        onClose={() => setSecret(null)}
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
