import { Play, RotateCcw } from 'lucide-react'
import { ActionMenu } from '../../../../shared/ActionMenu'
import { ActionButton, InlineFeedback, VNextConfirmDialog } from '../../../../shared/Interaction'
import { errorMessage } from '../../../../shared/errors'
import type { TeamLabRuntime } from '../api'
import type { AssetControlAction } from '../api/teamlabAssetControlApi'
import { useAssetControls } from './useAssetControls'
import styles from './AssetControlPanel.module.css'

const labels: Record<AssetControlAction, string> = {
  start: '启动', stop: '停止', restart: '重启', rebuild: '重建', pause: '暂停', resume: '恢复',
}

export function AssetControlPanel({ runtime }: { runtime: TeamLabRuntime }) {
  const controls = useAssetControls(runtime)
  const asset = controls.asset
  if (!asset) return null
  const queueActive = !!runtime.queueStatus && ['pending', 'scheduling', 'scheduled', 'running'].includes(runtime.queueStatus)
  const disabled = !controls.allowed || controls.active || queueActive || !['running', 'failed'].includes(runtime.status) || !!runtime.managedRolloutId
  const available = (action: AssetControlAction) => action === 'rebuild' || action === 'start' && asset.status === 'stopped' ||
    action === 'resume' && asset.status === 'paused' || action === 'stop' && ['running', 'paused'].includes(asset.status) ||
    ['restart', 'pause'].includes(action) && asset.status === 'running'
  const primary: AssetControlAction = asset.status === 'stopped' ? 'start' : asset.status === 'paused' ? 'resume' : 'restart'
  const choose = (action: AssetControlAction) => {
    if (action === 'start' || action === 'resume') void controls.submit(action)
    else controls.setPending(action)
  }

  return <div className={styles.controls}>
    <ActionButton disabled={disabled || !available(primary)} icon={primary === 'restart' ? <RotateCcw size={16} /> : <Play size={16} />}
      onClick={() => choose(primary)} type="button">{labels[primary]}</ActionButton>
    <ActionMenu label="更多资产操作" items={[
      ...(Object.keys(labels) as AssetControlAction[]).filter(action => action !== primary && available(action)).map(action => ({ label: labels[action], disabled, onSelect: () => choose(action) })),
    ]} />
    {controls.task ? <span className={styles.task} role="status">{controls.task.status === 'failed' ? controls.task.errorCode ?? '操作失败' : controls.task.status === 'succeeded' ? '已完成' : '执行中'}</span> : null}
    {controls.task?.canRetry ? <ActionButton disabled={disabled} onClick={() => void controls.retry()} type="button">继续执行</ActionButton> : null}
    {controls.error ? <InlineFeedback tone="danger">{errorMessage(controls.error, '资产操作失败。')}</InlineFeedback> : null}
    <VNextConfirmDialog open={controls.pending !== null} onClose={() => controls.setPending(null)} onConfirm={controls.submit}
      title={`${controls.pending ? labels[controls.pending] : ''} ${asset.name}`} confirmLabel="确认执行"
      message={controls.pending === 'rebuild' ? '将重建这台资产，可写数据会丢失。' : controls.pending === 'stop' && asset.kind === 'vm'
        ? '虚拟机会断电，未保存的数据会丢失。' : '当前连接会中断。'}
      tone={controls.pending === 'rebuild' || controls.pending === 'stop' ? 'danger' : 'primary'} />
  </div>
}
