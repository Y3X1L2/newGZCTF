import { Network } from 'lucide-react'
import { ActionButton, InlineFeedback } from '../../../shared/Interaction'
import type { NodeSummary } from '../api'
import { StatusBadge } from '../shared/AdminWorkbench'
import styles from './NodeTeamLabPanel.module.css'
import { formatHeartbeat, tunnelStatusMeta } from './useAdminNodes'

export function NodeTeamLabPanel({
  node,
  working,
  disabled = working,
  onProbe,
}: {
  node: NodeSummary
  working: boolean
  disabled?: boolean
  onProbe: () => Promise<void>
}) {
  const tunnel = tunnelStatusMeta(node.teamLabTunnelStatus)
  const capabilities = [
    {
      label: '场景网络',
      ready: node.canHostTeamLabFabric,
      detail: '跨节点网络与 Fabric',
      reason: node.unschedulableByCapability.teamLabNetwork,
    },
    {
      label: 'Docker 场景',
      ready: node.canHostTeamLabDocker,
      detail: '容器资产与受管网络',
      reason: node.unschedulableByCapability.teamLabDocker,
    },
    {
      label: 'VM 场景',
      ready: node.canHostTeamLabVm,
      detail: '虚拟机资产与受管网络',
      reason: node.unschedulableByCapability.teamLabVm,
    },
  ]
  return (
    <section className={styles.panel}>
      <header>
        <div>
          <h2>TeamLab 组网能力</h2>
          <p>服务端当前调度判定；运行兼容性检测可更新节点探测结果。</p>
        </div>
        <StatusBadge tone={tunnel.tone}>{tunnel.label}</StatusBadge>
      </header>
      <div className={styles.capabilities}>
        {capabilities.map((capability) => (
          <article key={capability.label}>
            <h3>{capability.label}</h3>
            <StatusBadge tone={capability.ready ? 'success' : 'neutral'}>
              {capability.ready ? '可调度' : '暂不可调度'}
            </StatusBadge>
            <p>{capability.detail}</p>
            {!capability.ready && capability.reason ? <small>{capability.reason}</small> : null}
          </article>
        ))}
      </div>
      <dl className={styles.facts}>
        <div>
          <dt>隧道地址</dt>
          <dd>{node.teamLabTunnelIp || '未分配'}</dd>
        </div>
        <div>
          <dt>Fabric 地址</dt>
          <dd>{node.teamLabFabricIp || '未分配'}</dd>
        </div>
        <div>
          <dt>最近握手</dt>
          <dd>{formatHeartbeat(node.teamLabTunnelLastHandshake)}</dd>
        </div>
        <div>
          <dt>能力上报</dt>
          <dd>{formatHeartbeat(node.capabilityObservedAt)}</dd>
        </div>
      </dl>
      {node.teamLabTunnelLastError ? (
        <InlineFeedback tone="danger">{node.teamLabTunnelLastError}</InlineFeedback>
      ) : null}
      <div>
        <ActionButton
          loading={working}
          disabled={disabled}
          icon={<Network size={16} />}
          onClick={onProbe}
          type="button"
        >
          {working ? '正在检测兼容性…' : '运行兼容性检测'}
        </ActionButton>
      </div>
      <details className={styles.technical}>
        <summary>技术详情 · {node.agentFeatures.length} 项原始探测</summary>
        <p>以下是 Agent 上报的原始能力标识，供排障和协议核对使用，不代表场景已通过实际运行验收。</p>
        <ul>
          {node.agentFeatures.map((feature) => (
            <li key={feature}>
              <code>{feature}</code>
            </li>
          ))}
        </ul>
        {!node.agentFeatures.length ? <p>Agent 尚未上报能力标识。</p> : null}
        <dl className={styles.facts}>
          <div>
            <dt>Agent 版本</dt>
            <dd>{node.agentVersion || '未上报'}</dd>
          </div>
          <div>
            <dt>能力协议版本</dt>
            <dd>{node.capabilityManifestSchemaVersion || '未上报'}</dd>
          </div>
        </dl>
      </details>
    </section>
  )
}
