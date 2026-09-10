import { Pause, Play, Power, RotateCcw, RefreshCw } from 'lucide-react'
import { useId } from 'react'
import { ActionButton, InlineFeedback, VNextConfirmDialog } from '../../../../shared/Interaction'
import { DataState } from '../../../../shared/Primitives'
import { errorMessage } from '../../../../shared/errors'
import type { TeamLabRuntime } from '../api'
import type { AssetControlAction } from '../api/teamlabAssetControlApi'
import { TeamLabRuntimeStatusBadge } from '../shared/TeamLabStatusBadge'
import { useAssetControls } from './useAssetControls'
import styles from './RuntimePanels.module.css'

const labels: Record<AssetControlAction, string> = { start: '启动', stop: '停止', restart: '重启', rebuild: '重建', pause: '暂停', resume: '恢复' }
const taskLabels: Record<string, string> = { pending: '排队中', scheduling: '调度中', scheduled: '等待执行', running: '执行中', succeeded: '已完成', failed: '失败', cancelled: '已取消' }
export function AssetControlPanel({ runtime }: { runtime: TeamLabRuntime }) {
  const controls = useAssetControls(runtime)
  const title = useId()
  const asset = controls.asset
  const queueActive = !!runtime.queueStatus && ['pending', 'scheduling', 'scheduled', 'running'].includes(runtime.queueStatus)
  const unavailable = !controls.allowed || controls.active || queueActive || !['running', 'failed'].includes(runtime.status) || !!runtime.managedRolloutId
  const available = (action: AssetControlAction) => action === 'rebuild' || action === 'start' && asset?.status === 'stopped' ||
    action === 'resume' && asset?.status === 'paused' || action === 'stop' && ['running', 'paused'].includes(asset?.status ?? '') ||
    ['restart', 'pause'].includes(action) && asset?.status === 'running'
  return <section className={styles.panel} aria-labelledby={title}>
    <header className={styles.panelHeader}><h3 id={title}>单资产生命周期</h3>{asset ? <TeamLabRuntimeStatusBadge status={asset.status} /> : null}</header>
    {!asset ? <DataState title="暂无可操作资产" /> : <>
      <div className={styles.diagnosticsControls}>
        <label>操作资产<select value={asset.id} disabled={controls.busy} onChange={event => controls.select(Number(event.target.value))}>
          {runtime.assets.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}
        </select></label>
        <label>操作原因<input value={controls.reason} maxLength={500} disabled={controls.busy} onChange={event => controls.setReason(event.target.value)} /></label>
      </div>
      <div className={styles.fileToolbar}>{(Object.keys(labels) as AssetControlAction[]).map(action => <ActionButton key={action} type="button"
        disabled={unavailable || !available(action) || controls.reason.trim().length < 4}
        icon={action === 'stop' ? <Power size={16} /> : action === 'pause' ? <Pause size={16} /> : ['start', 'resume'].includes(action) ? <Play size={16} /> : <RotateCcw size={16} />}
        tone={action === 'rebuild' || action === 'stop' ? 'danger' : 'secondary'} onClick={() => controls.setPending(action)}>{labels[action]}</ActionButton>)}</div>
      {controls.error ? <InlineFeedback tone="danger">{errorMessage(controls.error, '资产操作失败。')}</InlineFeedback> : null}
      {controls.unavailableReason ? <InlineFeedback tone="neutral">{controls.unavailableReason}</InlineFeedback> : null}
      {controls.task ? <div aria-live="polite">
        <strong>{taskLabels[controls.task.status] ?? controls.task.status}</strong><p>{controls.task.stage}</p>
        {controls.task.errorCode ? <InlineFeedback tone="danger">{controls.task.errorCode}</InlineFeedback> : null}
        {controls.task.canRetry ? <ActionButton type="button" icon={<RefreshCw size={16} />} disabled={unavailable}
          onClick={() => void controls.retry()}>继续未完成步骤</ActionButton> : null}
      </div> : null}
      <VNextConfirmDialog open={controls.pending !== null} onClose={() => controls.setPending(null)} onConfirm={controls.submit}
        title={`${controls.pending ? labels[controls.pending] : ''} ${asset.name}`} confirmLabel="确认执行"
        description={controls.pending === 'rebuild' ? '仅替换此资产的运行资源和可写磁盘，恢复原始发布配置；其他资产不重建。' : '仅操作此资产，保留其他资产和场景网络。'}
        message={asset.kind === 'vm' && ['stop', 'restart', 'rebuild'].includes(controls.pending ?? '')
          ? '虚拟机将强制断电，未保存的数据会丢失，当前远程连接将关闭。'
          : controls.pending === 'rebuild' ? '此资产的可写层数据将丢失，当前远程连接将关闭。' : '当前远程连接将关闭。'}
        tone={controls.pending === 'rebuild' ? 'danger' : 'primary'} />
    </>}
  </section>
}
