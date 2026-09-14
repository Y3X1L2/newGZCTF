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

const linkPolicyKinds: readonly Exclude<TeamLabLinkPolicyKind, 'nat'>[] = [
  'latency', 'jitter', 'packet-loss', 'duplication', 'bandwidth-limit', 'link-break', 'access-rule',
]

const statusTones = { active: 'info', recovered: 'success', failed: 'danger' } as const

export function RuntimeLinkPolicyPanel({
  networks,
  assets,
  runtimeId,
}: {
  networks: readonly { key: string; name: string }[]
  assets: readonly { key: string; name: string; networkKeys: readonly string[] }[]
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
        策略可作用于整个网段，也可限定到该网段中的一个资产。NAT 和公网端口请在资产的“服务开放”中配置。
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
  assets: readonly { key: string; name: string; networkKeys: readonly string[] }[]
  networks: readonly { key: string; name: string }[]
  onClose: () => void
  onApplied: () => void
  open: boolean
  runtimeId: string
}) {
  const formId = useId()
  const [kind, setKind] = useState<Exclude<TeamLabLinkPolicyKind, 'nat'>>('latency')
  const [networkKey, setNetworkKey] = useState(networks[0]?.key ?? '')
  const [assetKey, setAssetKey] = useState('')
  const [value, setValue] = useState('100')
  const [burst, setBurst] = useState('')
  const [direction, setDirection] = useState('inbound')
  const [action, setAction] = useState('deny')
  const [protocol, setProtocol] = useState('tcp')
  const [sourceCidr, setSourceCidr] = useState('')
  const [destinationCidr, setDestinationCidr] = useState('')
  const [sourcePort, setSourcePort] = useState('')
  const [destinationPort, setDestinationPort] = useState('')
  const [recoverMinutes, setRecoverMinutes] = useState('')
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState<unknown>(null)

  const apply = async () => {
    if (submitting || !networkKey) return
    setSubmitting(true)
    setError(null)
    try {
      const number = Number(value)
      const optionalNumber = (input: string) => input ? Number(input) : undefined
      const parameters = kind === 'latency' ? { delayMillis: number }
        : kind === 'jitter' ? { jitterMillis: number }
        : kind === 'packet-loss' ? { lossPercent: number }
        : kind === 'duplication' ? { duplicatePercent: number }
        : kind === 'bandwidth-limit' ? { rateMbps: number, burstKilobytes: optionalNumber(burst) }
        : kind === 'access-rule' ? {
            direction, action, protocol,
            sourceCidr: sourceCidr || undefined,
            destinationCidr: destinationCidr || undefined,
            sourcePort: optionalNumber(sourcePort),
            destinationPort: optionalNumber(destinationPort),
          }
        : {}
      const minutes = Number(recoverMinutes)
      await teamLabRuntimeApi.applyLinkPolicy(runtimeId, {
        runtimeId,
        networkKey,
        assetKey: assetKey || null,
        kind,
        parameters,
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
            const next = event.currentTarget.value as Exclude<TeamLabLinkPolicyKind, 'nat'>
            setKind(next)
            setValue(next === 'latency' ? '100' : next === 'jitter' ? '20' : next === 'bandwidth-limit' ? '10' : '5')
          }}
          value={kind}
        >
          {linkPolicyKinds.map((key) => (
            <option key={key} value={key}>
              {linkPolicyKindLabels[key]}
            </option>
          ))}
        </select>
        <label htmlFor={`${formId}-network`}>目标网段</label>
        <select
          aria-label="目标网段"
          id={`${formId}-network`}
          onChange={(event) => {
            setNetworkKey(event.currentTarget.value)
            setAssetKey('')
          }}
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
          {assets.filter((asset) => asset.networkKeys.includes(networkKey)).map((asset) => (
            <option key={asset.key} value={asset.key}>
              {asset.name} ({asset.key})
            </option>
          ))}
        </select>
        <PolicyFields
          action={action}
          burst={burst}
          destinationCidr={destinationCidr}
          destinationPort={destinationPort}
          direction={direction}
          formId={formId}
          kind={kind}
          protocol={protocol}
          setAction={setAction}
          setBurst={setBurst}
          setDestinationCidr={setDestinationCidr}
          setDestinationPort={setDestinationPort}
          setDirection={setDirection}
          setProtocol={setProtocol}
          setSourceCidr={setSourceCidr}
          setSourcePort={setSourcePort}
          setValue={setValue}
          sourceCidr={sourceCidr}
          sourcePort={sourcePort}
          value={value}
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

type SetValue = (value: string) => void

function PolicyFields({
  action, burst, destinationCidr, destinationPort, direction, formId, kind, protocol,
  setAction, setBurst, setDestinationCidr, setDestinationPort, setDirection, setProtocol,
  setSourceCidr, setSourcePort, setValue, sourceCidr, sourcePort, value,
}: {
  action: string; burst: string; destinationCidr: string; destinationPort: string; direction: string
  formId: string; kind: Exclude<TeamLabLinkPolicyKind, 'nat'>; protocol: string
  setAction: SetValue; setBurst: SetValue; setDestinationCidr: SetValue; setDestinationPort: SetValue
  setDirection: SetValue; setProtocol: SetValue; setSourceCidr: SetValue; setSourcePort: SetValue
  setValue: SetValue; sourceCidr: string; sourcePort: string; value: string
}) {
  if (kind === 'link-break') return <p className={styles.panelHint}>应用后该链路会立即中断，恢复策略后重新连通。</p>
  if (kind === 'access-rule') return <>
    <label htmlFor={`${formId}-direction`}>方向</label>
    <select id={`${formId}-direction`} onChange={(event) => setDirection(event.currentTarget.value)} value={direction}>
      <option value="inbound">进入资产</option><option value="outbound">离开资产</option><option value="both">双向</option>
    </select>
    <label htmlFor={`${formId}-action`}>处理</label>
    <select id={`${formId}-action`} onChange={(event) => setAction(event.currentTarget.value)} value={action}>
      <option value="deny">阻断</option><option value="allow">允许</option>
    </select>
    <label htmlFor={`${formId}-protocol`}>协议</label>
    <select id={`${formId}-protocol`} onChange={(event) => setProtocol(event.currentTarget.value)} value={protocol}>
      <option value="any">全部</option><option value="tcp">TCP</option><option value="udp">UDP</option><option value="icmp">ICMP</option>
    </select>
    <label htmlFor={`${formId}-source-cidr`}>来源地址（可选）</label>
    <input id={`${formId}-source-cidr`} onChange={(event) => setSourceCidr(event.currentTarget.value)} placeholder="例如 10.10.0.0/24" value={sourceCidr} />
    <label htmlFor={`${formId}-destination-cidr`}>目标地址（可选）</label>
    <input id={`${formId}-destination-cidr`} onChange={(event) => setDestinationCidr(event.currentTarget.value)} placeholder="例如 10.20.0.10/32" value={destinationCidr} />
    {protocol === 'tcp' || protocol === 'udp' ? <>
      <label htmlFor={`${formId}-source-port`}>来源端口（可选）</label>
      <input id={`${formId}-source-port`} max={65535} min={1} onChange={(event) => setSourcePort(event.currentTarget.value)} type="number" value={sourcePort} />
      <label htmlFor={`${formId}-destination-port`}>目标端口（可选）</label>
      <input id={`${formId}-destination-port`} max={65535} min={1} onChange={(event) => setDestinationPort(event.currentTarget.value)} type="number" value={destinationPort} />
    </> : null}
  </>

  const field = kind === 'latency' ? ['时延', '毫秒', 1, 10000]
    : kind === 'jitter' ? ['抖动', '毫秒', 0, 5000]
    : kind === 'packet-loss' ? ['丢包率', '%', 0, 100]
    : kind === 'duplication' ? ['重复率', '%', 0, 100]
    : ['带宽', 'Mbps', 0.1, 100000]
  return <>
    <label htmlFor={`${formId}-value`}>{field[0]}（{field[1]}）</label>
    <input id={`${formId}-value`} max={field[3]} min={field[2]} onChange={(event) => setValue(event.currentTarget.value)} required step="any" type="number" value={value} />
    {kind === 'bandwidth-limit' ? <>
      <label htmlFor={`${formId}-burst`}>突发容量（KiB，可选）</label>
      <input id={`${formId}-burst`} min={0} onChange={(event) => setBurst(event.currentTarget.value)} type="number" value={burst} />
    </> : null}
  </>
}
