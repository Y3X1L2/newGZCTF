import { useEffect, useState } from 'react'
import { ApiTokenCreateModel } from '@Api'
import { ActionButton, InlineFeedback, VNextDialog } from '../../shared/Interaction'
import { errorMessage } from '../../shared/errors'
import { ControlScopeOption, settingsApi } from './settingsApi'
import { TokenResourceGrant } from './TokenResourceGrant'
import styles from './SettingsPage.module.css'

export const tokenScopeOptions = [
  ['images:read', '读取镜像'],
  ['images:write', '写入镜像'],
  ['images:delete', '删除镜像'],
  ['assets:read', '读取附件'],
  ['assets:write', '上传附件'],
  ['challenges:read', '读取比赛题目'],
  ['challenges:write', '导入比赛题目'],
  ['challenges:delete', '删除比赛题目'],
  ['exercises:read', '读取练习题库'],
  ['exercises:write', '导入练习题目'],
  ['exercises:delete', '删除练习题目'],
  ['training:write', '导入培训课程'],
  ['theory:write', '导入理论题库与试卷'],
  ['operations:read', '读取异步操作'],
  ['teamlab.topologies:read', 'TeamLab 拓扑读取'],
  ['teamlab.topologies:write', 'TeamLab 拓扑写入'],
  ['teamlab.runtimes:read', 'TeamLab 运行实例读取'],
  ['teamlab.runtimes:write', 'TeamLab 运行实例写入'],
  ['teamlab.traffic:read', 'TeamLab 流量观测读取'],
  ['teamlab.capture:read', 'TeamLab 取证文件读取'],
  ['teamlab.capture:write', 'TeamLab 取证文件写入'],
  ['bootstrap-profiles:read', '引导配置读取'],
  ['bootstrap-profiles:write', '引导配置写入'],
] as const

const adminTokenScopeOptions = [['teams:write', '导入战队']] as const

interface TokenCreateDialogProps {
  canGrantScopes: boolean
  onClose: () => void
  onIssued: (secret: string) => void
  open: boolean
}

export function TokenCreateDialog({ canGrantScopes, onClose, onIssued, open }: TokenCreateDialogProps) {
  const [name, setName] = useState('')
  const [scopes, setScopes] = useState<string[]>(['images:read'])
  const [resources, setResources] = useState<string[]>([])
  const [resourceType, setResourceType] = useState('')
  const [resourceId, setResourceId] = useState('')
  const [availableScopes, setAvailableScopes] = useState<ControlScopeOption[]>([])
  const [scopeLoadError, setScopeLoadError] = useState<string | null>(null)
  const [requestsPerMinute, setRequestsPerMinute] = useState(60)
  const [expiresAt, setExpiresAt] = useState('')
  const [submitting, setSubmitting] = useState(false)
  const [feedback, setFeedback] = useState<string | null>(null)
  const visibleScopeOptions = canGrantScopes
    ? [...tokenScopeOptions, ...adminTokenScopeOptions]
    : tokenScopeOptions

  const loadScopes = () => {
    if (!canGrantScopes) return
    setScopeLoadError(null)
    settingsApi
      .listControlScopes()
      .then(setAvailableScopes)
      .catch((requestError) => setScopeLoadError(errorMessage(requestError, '控制范围列表加载失败。')))
  }

  useEffect(() => {
    if (open && canGrantScopes) loadScopes()
  }, [open, canGrantScopes])

  const submit = async () => {
    const model: ApiTokenCreateModel = {
      name: name.trim(),
      scopes,
      requestsPerMinute,
      expiresAt: expiresAt ? new Date(expiresAt).getTime() : null,
      resources: [
        ...resources.map((scopeId) => ({ resourceType: 'teamlab-scope', resourceId: scopeId })),
        ...(resourceType.trim() && resourceId.trim()
          ? [{ resourceType: resourceType.trim(), resourceId: resourceId.trim() }]
          : []),
      ],
    }
    setSubmitting(true)
    setFeedback(null)
    try {
      const secret = await settingsApi.issueToken(model)
      setName('')
      setScopes(['images:read'])
      setResources([])
      setResourceType('')
      setResourceId('')
      setRequestsPerMinute(60)
      setExpiresAt('')
      onIssued(secret)
    } catch (requestError) {
      setFeedback(errorMessage(requestError, 'Token 创建失败。'))
    } finally {
      setSubmitting(false)
    }
  }

  const toggleScope = (value: string, checked: boolean) =>
    setScopes((current) => (checked ? [...current, value] : current.filter((item) => item !== value)))

  const toggleResource = (id: string, checked: boolean) =>
    setResources((current) => (checked ? [...current, id] : current.filter((item) => item !== id)))

  return (
    <VNextDialog
      description="选择最小必要权限；镜像写入和题目导入建议使用不同 Token。"
      eyebrow="ISSUE TOKEN"
      footer={
        <>
          <ActionButton onClick={onClose} type="button">
            取消
          </ActionButton>
          <ActionButton
            disabled={submitting || !name.trim() || !scopes.length}
            onClick={() => void submit()}
            tone="primary"
            type="button"
          >
            确认创建
          </ActionButton>
        </>
      }
      onClose={onClose}
      open={open}
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
            {visibleScopeOptions.map(([value, label]) => (
              <label key={value}>
                <input
                  checked={scopes.includes(value)}
                  onChange={(event) => toggleScope(value, event.currentTarget.checked)}
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
        {canGrantScopes ? (
          <fieldset>
            <legend>TeamLab 控制范围授权</legend>
            <p className={styles.hintText}>仅管理员可授权。授予后此 Token 可管理所选控制范围内的拓扑与运行实例。</p>
            {scopeLoadError ? <InlineFeedback tone="danger">{scopeLoadError}</InlineFeedback> : null}
            <div className={styles.checkGrid}>
              {availableScopes.map((scope) => (
                <label key={scope.id}>
                  <input
                    checked={resources.includes(scope.id)}
                    onChange={(event) => toggleResource(scope.id, event.currentTarget.checked)}
                    type="checkbox"
                  />
                  <span>
                    <strong>{scope.displayName}</strong>
                    <small>{scope.key}</small>
                  </span>
                </label>
              ))}
              {!availableScopes.length && !scopeLoadError ? (
                <span className={styles.mutedText}>暂无控制范围。</span>
              ) : null}
            </div>
          </fieldset>
        ) : null}
        <fieldset>
          <legend>外部资源授权</legend>
          <p className={styles.hintText}>练习题库使用 <code>exercise:*</code>，培训课程使用 <code>training-course:*</code>，理论题库使用 <code>theory-bank:*</code>，比赛题目或理论试卷使用 <code>game:比赛 ID</code>；管理员导入战队使用 <code>team:*</code>。</p>
          <TokenResourceGrant
            className={styles.formGrid}
            onResourceIdChange={setResourceId}
            onResourceTypeChange={setResourceType}
            resourceId={resourceId}
            resourceType={resourceType}
          />
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
        {feedback ? <InlineFeedback tone="danger">{feedback}</InlineFeedback> : null}
      </div>
    </VNextDialog>
  )
}
