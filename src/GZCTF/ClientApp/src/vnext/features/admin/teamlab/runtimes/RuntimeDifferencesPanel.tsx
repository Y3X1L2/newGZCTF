import { useId } from 'react'
import { ActionButton, InlineFeedback, VNextConfirmDialog } from '../../../../shared/Interaction'
import { errorMessage } from '../../../../shared/errors'
import type { TeamLabRuntime } from '../api'
import { useRuntimeDifferences } from './useRuntimeDifferences'
import styles from './RuntimePanels.module.css'

const states: Record<string, string> = { running: '运行', stopped: '停止', exited: '停止', shutoff: '停止', paused: '暂停', absent: '不存在' }
const differences: Record<string, string> = { matched: '一致', missing: '资源缺失', 'power-drift': '电源状态不一致',
  'identity-conflict': '资源身份冲突', unavailable: '节点不可查询', unsupported: '节点不支持查询', busy: '任务执行中', orphan: '残留资源' }
const actions: Record<string, string> = { start: '启动', stop: '停止', pause: '暂停', resume: '恢复', rebuild: '重建' }

export function RuntimeDifferencesPanel({ runtime }: { runtime: TeamLabRuntime }) {
  const title = useId()
  const controller = useRuntimeDifferences(runtime)
  return <section className={styles.panel} aria-labelledby={title}>
    <header className={styles.panelHeader}><h3 id={title}>运行状态检查</h3>
      <ActionButton type="button" disabled={controller.busy} onClick={() => void controller.check()}>{controller.busy ? '检查与处理中…' : '检查差异'}</ActionButton>
    </header>
    <p>比较资产的期望状态与节点实际状态。检查不会修改资源；修复任务将在下方资产操作区显示进度。</p>
    {controller.error ? <InlineFeedback tone="danger">{errorMessage(controller.error, '检查或修复失败，请重试。')}</InlineFeedback> : null}
    {controller.preview?.operationInProgress ? <InlineFeedback tone="neutral">运行任务尚未结束，请完成后重新检查。</InlineFeedback> : null}
    {controller.preview ? <div className={styles.sessionTableScroll} tabIndex={0} role="region" aria-label="运行资产差异"><table className={styles.sessionTable}>
      <thead><tr><th>资源</th><th>期望</th><th>实际</th><th>检查结果</th><th>处理</th></tr></thead>
      <tbody>{controller.preview.items.map((item, index) => <tr key={`${item.workerNodeId}:${item.assetId}:${index}`}>
        <td>{item.name || item.resourceKind}</td><td>{states[item.expectedState] ?? item.expectedState}</td>
        <td>{item.actualState ? states[item.actualState] ?? item.actualState : '未确认'}</td><td>{differences[item.difference] ?? item.difference}</td>
        <td>{item.suggestedAction && !runtime.managedRolloutId ? <ActionButton type="button" disabled={controller.busy || controller.preview?.operationInProgress}
          onClick={() => controller.setPending(item)}>{actions[item.suggestedAction]}</ActionButton>
          : item.difference === 'identity-conflict' ? '核实节点资源身份后处理'
            : item.difference === 'orphan' ? '核实资源归属，按原执行计划清理' : '—'}</td>
      </tr>)}</tbody>
    </table></div> : null}
    {controller.preview?.items.length === 0 && !controller.preview.operationInProgress ? <p>当前没有可检查的运行资产。</p> : null}
    <VNextConfirmDialog open={controller.pending !== null} onClose={() => controller.setPending(null)} onConfirm={controller.repair}
      title={`${actions[controller.pending?.suggestedAction ?? ''] ?? '修复'} ${controller.pending?.name ?? ''}`} confirmLabel="提交修复"
      description="提交前会重新检查现场状态。只处理选定资产，任务复用原运行队列。"
      message={controller.pending?.suggestedAction === 'rebuild' ? '将从原始发布配置重建此资产，可写层数据无法保留。' : controller.pending?.resourceKind === 'vm' && controller.pending.suggestedAction === 'stop'
        ? '虚拟机将强制断电，未保存的数据会丢失，当前远程连接会关闭。' : '处理期间该资产的当前远程连接会关闭。'}
      tone={controller.pending?.suggestedAction === 'rebuild' ? 'danger' : 'primary'} />
  </section>
}
