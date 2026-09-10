import { useId } from 'react'
import { ActionButton, InlineFeedback } from '../../../../shared/Interaction'
import { errorMessage } from '../../../../shared/errors'
import { useDeviceHealth } from './useDeviceHealth'
import styles from './RuntimePanels.module.css'

const labels: Record<string, string> = { healthy: '正常', unhealthy: '异常', unavailable: '未能确认', stopped: '已停止或暂停', inactive: '场景已进入清理，停止检查', 'not-configured': '未配置健康检查' }
const errors: Record<string, string> = {
  'device.probe_failed': '探测失败，请检查设备服务、网络路由及节点探测能力。',
  'device.node_unavailable': '节点暂时不可查询，不能确认设备健康。',
  'device.telemetry_invalid': '设备健康接口未返回有效的协议计数。',
  'device.counter_regressed': '设备计数异常回退，请检查设备上报。',
  'device.execution_snapshot_missing': '缺少有效执行快照，暂时无法检查。',
  'device.identity_changed': '设备实例已变化，等待重新检查。',
}
export function DeviceHealthPanel({ runtimeId, generation }: { runtimeId: string; generation: number }) {
  const title = useId()
  const request = useDeviceHealth(runtimeId, generation)
  if (!request.error && (!request.data || request.data.length === 0)) return null
  return <section className={styles.panel} aria-labelledby={title}>
    <header className={styles.panelHeader}><h3 id={title}>设备健康与协议活动</h3>
      <ActionButton type="button" disabled={request.isValidating} onClick={() => void request.mutate()}>刷新结果</ActionButton>
    </header>
    <p>后台按设备包声明持续检查。协议次数来自设备实际处理请求的累计计数，重启后重新计数；新增活动记录在事件与日志中，不记录寄存器值或报文内容。</p>
    {request.error ? <InlineFeedback tone="danger">{errorMessage(request.error, '设备监督结果读取失败，请重试。')}</InlineFeedback> : null}
    <div className={styles.sessionTableScroll} tabIndex={0} role="region" aria-label="设备监督结果"><table className={`${styles.sessionTable} ${styles.deviceHealthTable}`}>
      <thead><tr><th>设备</th><th>状态</th><th>最近检查</th><th>协议活动</th></tr></thead>
      <tbody>{request.data?.filter(item => item.generation === generation).map(item => <tr key={item.assetId}>
        <td>{item.name}</td>
        <td>{item.nextProbeAt !== null && Date.now() > item.nextProbeAt + 30000 ? '结果已过期，等待后台检查'
          : item.observation ? labels[item.observation.status] ?? '未知状态' : '等待首次检查'}
          {item.observation?.errorCode ? <div>{errors[item.observation.errorCode] ?? '检查未完成，请查看事件与日志。'}</div> : null}</td>
        <td>{item.observation ? new Date(item.observation.observedAt).toLocaleString() : '—'}</td>
        <td>{item.observation && item.observation.counters.length > 0
          ? `${item.observation.status === 'healthy' ? '' : '上次采集：'}${item.observation.counters.map(counter => `${counter.type}：${counter.count}`).join('；')}` : '无协议计数'}</td>
      </tr>)}</tbody>
    </table></div>
  </section>
}
