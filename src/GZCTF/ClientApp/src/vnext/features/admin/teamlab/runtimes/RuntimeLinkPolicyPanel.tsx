import { Network, RotateCcw } from 'lucide-react'
import { useId, useState } from 'react'
import { ActionButton, InlineFeedback, VNextDialog } from '../../../../shared/Interaction'
import { DataState } from '../../../../shared/Primitives'
import { errorMessage } from '../../../../shared/errors'
import { FilterToolbar, StatusBadge, ToolbarGroup } from '../../shared/AdminWorkbench'
import { formatAdminDate } from '../../shared/adminFormat'
import { teamLabRuntimeApi } from '../api'
import type { TeamLabLinkPolicy, TeamLabLinkPolicyKind } from '../api/teamlabResourcesContracts'
import {
  linkPolicyKindLabels,
  summarizeLinkPolicyParameters,
  toAdminDate,
} from '../resources/resourcesPresentation'
import { useRuntimeLinkPolicies, type TeamLabLinkPolicyStatusFilter } from './useRuntimeLinkPolicies'
import styles from './RuntimePanels.module.css'

const policyParameterTemplates: Record<TeamLabLinkPolicyKind, string> = {
  latency: '{"delayMillis": 100}',
  jitter: '{"jitterMillis": 20}',
  'packet-loss': '{"lossPercent": 5}',
  duplication: '{"duplicatePercent": 2}',
  'bandwidth-limit': '{"rateMbps": 10}',
  'link-break': '{}',
  'access-rule': '{"direction": "inbound", "action": "deny", "protocol": "tcp", "sourceCidr": "10.0.0.0/8"}',
  nat: '{"mode": "snat", "translatedAddress": "172.16.0.9"}',
}

const statusTones = { active: 'info', recovered: 'success', failed: 'danger' } as const

export function RuntimeLinkPolicyPanel({
  networks,
  assets,
  runtimeId,
}: {
  networks: readonly { key: string; name: string }[]
  assets: readonly { key: string; name: string }[]
  runtimeId: string
}) {
  const [status, setStatus] = useState<TeamLabLinkPolicyStatusFilter>('active')
  const policies = useRuntimeLinkPolicies(runtimeId, status)
  const [applyOpen, setApplyOpen] = useState(false)
  const [actionError, setActionError] = useState<unknown>(null)
  const [busyPolicyId, setBusyPolicyId] = useState<string | null>(null)

  const recover = async (policyId: string) => {
    if (busyPolicyId) return
    setBusyPolicyId(policyId)
    setActionError(null)
    try {
      await teamLabRuntimeApi.recoverLinkPolicy(runtimeId, policyId)
      await policies.mutate()
    } catch (error) {
      setActionError(error)
    } finally {
      setBusyPolicyId(null)
    }
  }

  return (
    <section aria-labelledby="link-policy-title" className={styles.panel}>
      <header className={styles.panelHeader}>
        <div>
          <span>链路策略</span>
          <h3 id="link-policy-title">损伤与访问控制</h3>
        </div>
        <ActionButton icon={<Network size={16} />} onClick={() => setApplyOpen(true)} tone="primary" type="button">
          应用策略
        </ActionButton>
      </header>
      <p className={styles.panelHint}>
        策略作用于运行时网段或单个资产：时延、丢包、限速、断链等损伤可设定时自动恢复，访问控制与 NAT 由执行面下发。
      </p>
      <FilterToolbar>
        <ToolbarGroup>
          <select
            aria-label="筛选链路策略状态"
            onChange={(event) => setStatus(event.currentTarget.value as TeamLabLinkPolicyStatusFilter)}
            value={status}
          >
            <option value="active">生效中</option>
            <option value="recovered">已恢复</option>
            <option value="failed">失败</option>
            <option value="">全部未恢复</option>
          </select>
        </ToolbarGroup>
      </FilterToolbar>

      {actionError ? <InlineFeedback tone="danger">{errorMessage(actionError, '链路策略操作失败。')}</InlineFeedback> : null}
      {policies.isLoading ? (
        <DataState description="正在读取链路策略。" loading title="链路策略加载中" />
      ) : policies.error ? (
        <DataState description={errorMessage(policies.error, '链路策略暂不可用。')} title="链路策略加载失败" />
      ) : policies.policies.length === 0 ? (
        <DataState
          description={status === 'active' ? '当前没有生效中的链路策略，可按需应用损伤或访问控制。' : '当前筛选条件下没有策略记录。'}
          title="暂无链路策略"
        />
      ) : (
        <ol className={styles.policyList} aria-label="链路策略列表">
          {policies.policies.map((policy) => (
            <PolicyRow
              busy={busyPolicyId === policy.id}
              key={policy.id}
              onRecover={() => void recover(policy.id)}
              policy={policy}
            />
          ))}
        </ol>
      )}

      <ApplyLinkPolicyDialog
        assets={assets}
        networks={networks}
        onClose={() => setApplyOpen(false)}
        onApplied={() => {
          setApplyOpen(false)
          setStatus('active')
          void policies.mutate()
        }}
        open={applyOpen}
        runtimeId={runtimeId}
      />
    </section>
  )
}

function PolicyRow({ policy, onRecover, busy }: { policy: TeamLabLinkPolicy; onRecover: () => void; busy: boolean }) {
  return (
    <li className={styles.policyItem}>
      <div className={styles.policyIdentity}>
        <strong>{linkPolicyKindLabels[policy.kind]}</strong>
        <span>
          {policy.networkKey}
          {policy.assetKey ? ` · ${policy.assetKey}` : ''}
        </span>
        <span className={styles.policyParameters}>{summarizeLinkPolicyParameters(policy.kind, policy.parameters)}</span>
      </div>
      <div className={styles.policyMeta}>
        <StatusBadge tone={statusTones[policy.status]}>{policy.status === 'active' ? '生效中' : policy.status === 'recovered' ? '已恢复' : '失败'}</StatusBadge>
        <span>应用于 {formatAdminDate(toAdminDate(policy.appliedAt))}</span>
        {policy.recoverAt ? <span>定时恢复 {formatAdminDate(toAdminDate(policy.recoverAt))}</span> : null}
        {policy.recoveredAt && policy.recoverOrigin !== 'none' ? (
          <span>
            恢复于 {formatAdminDate(toAdminDate(policy.recoveredAt))}（
            {policy.recoverOrigin === 'scheduled' ? '定时' : policy.recoverOrigin === 'manual' ? '手工' : '运行时销毁'}）
          </span>
        ) : null}
        {policy.lastError ? <span className={styles.policyError}>{policy.lastError}</span> : null}
      </div>
      {policy.status !== 'recovered' ? (
        <ActionButton disabled={busy} icon={<RotateCcw size={15} />} onClick={onRecover} type="button">
          恢复
        </ActionButton>
      ) : null}
    </li>
  )
}

function ApplyLinkPolicyDialog({
  assets,
  networks,
  onClose,
  onApplied,
  open,
  runtimeId,
}: {
  assets: readonly { key: string; name: string }[]
  networks: readonly { key: string; name: string }[]
  onClose: () => void
  onApplied: () => void
  open: boolean
  runtimeId: string
}) {
  const formId = useId()
  const [kind, setKind] = useState<TeamLabLinkPolicyKind>('latency')
  const [networkKey, setNetworkKey] = useState(networks[0]?.key ?? '')
  const [assetKey, setAssetKey] = useState('')
  const [parameters, setParameters] = useState(policyParameterTemplates.latency)
  const [recoverMinutes, setRecoverMinutes] = useState('')
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState<unknown>(null)

  const apply = async () => {
    if (submitting || !networkKey) return
    setSubmitting(true)
    setError(null)
    try {
      let parsedParameters: unknown = undefined
      const trimmed = parameters.trim()
      if (trimmed) {
        parsedParameters = JSON.parse(trimmed)
        if (typeof parsedParameters !== 'object' || parsedParameters === null || Array.isArray(parsedParameters)) {
          throw new Error('策略参数必须是 JSON 对象')
        }
      }
      const minutes = Number(recoverMinutes)
      await teamLabRuntimeApi.applyLinkPolicy(runtimeId, {
        runtimeId,
        networkKey,
        assetKey: assetKey || null,
        kind,
        parameters: parsedParameters,
        recoverAt:
          Number.isFinite(minutes) && minutes > 0
            ? new Date(Date.now() + minutes * 60_000).toISOString()
            : null,
      })
      onApplied()
    } catch (reason) {
      setError(reason)
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <VNextDialog
      description="声明式应用：同参数重复应用幂等，不同参数需先恢复原策略。"
      eyebrow="LINK POLICY"
      footer={
        <>
          <ActionButton disabled={submitting} onClick={onClose} type="button">取消</ActionButton>
          <ActionButton disabled={submitting || !networkKey} onClick={() => void apply()} tone="primary" type="button">
            {submitting ? '正在应用' : '确认应用'}
          </ActionButton>
        </>
      }
      onClose={() => {
        if (!submitting) onClose()
      }}
      open={open}
      title="应用链路策略"
    >
      <div className={styles.policyForm}>
        <label htmlFor={`${formId}-kind`}>策略类型</label>
        <select
          aria-label="策略类型"
          id={`${formId}-kind`}
          onChange={(event) => {
            const next = event.currentTarget.value as TeamLabLinkPolicyKind
            setKind(next)
            setParameters(policyParameterTemplates[next])
          }}
          value={kind}
        >
          {(Object.keys(linkPolicyKindLabels) as TeamLabLinkPolicyKind[]).map((key) => (
            <option key={key} value={key}>
              {linkPolicyKindLabels[key]}
            </option>
          ))}
        </select>
        <label htmlFor={`${formId}-network`}>目标网段</label>
        <select
          aria-label="目标网段"
          id={`${formId}-network`}
          onChange={(event) => setNetworkKey(event.currentTarget.value)}
          value={networkKey}
        >
          {networks.map((network) => (
            <option key={network.key} value={network.key}>
              {network.name} ({network.key})
            </option>
          ))}
        </select>
        <label htmlFor={`${formId}-asset`}>限定资产（可选）</label>
        <select
          aria-label="限定资产"
          id={`${formId}-asset`}
          onChange={(event) => setAssetKey(event.currentTarget.value)}
          value={assetKey}
        >
          <option value="">整个网段</option>
          {assets.map((asset) => (
            <option key={asset.key} value={asset.key}>
              {asset.name} ({asset.key})
            </option>
          ))}
        </select>
        <label htmlFor={`${formId}-parameters`}>策略参数（JSON）</label>
        <textarea
          aria-label="策略参数"
          id={`${formId}-parameters`}
          onChange={(event) => setParameters(event.currentTarget.value)}
          rows={4}
          value={parameters}
        />
        <label htmlFor={`${formId}-recover`}>定时恢复（分钟，可选）</label>
        <input
          aria-label="定时恢复分钟"
          id={`${formId}-recover`}
          min={1}
          onChange={(event) => setRecoverMinutes(event.currentTarget.value)}
          placeholder="留空表示手工恢复"
          type="number"
          value={recoverMinutes}
        />
        {error ? <InlineFeedback tone="danger">{errorMessage(error, '链路策略应用失败。')}</InlineFeedback> : null}
      </div>
    </VNextDialog>
  )
}
