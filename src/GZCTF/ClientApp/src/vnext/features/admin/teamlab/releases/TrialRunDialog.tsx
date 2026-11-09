import { ActionButton, InlineFeedback, VNextDialog } from '../../../../shared/Interaction'
import type { TeamLabRelease, TeamLabRuntimeOverlay } from '../api'
import styles from './TrialRunDialog.module.css'

export function createTrialIdempotencyKey(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function')
    return `teamlab-trial-${crypto.randomUUID()}`

  if (typeof crypto !== 'undefined' && typeof crypto.getRandomValues === 'function') {
    const bytes = new Uint8Array(16)
    crypto.getRandomValues(bytes)
    return `teamlab-trial-${Array.from(bytes, (byte) => byte.toString(16).padStart(2, '0')).join('')}`
  }

  return `teamlab-trial-${Date.now().toString(36)}-${Math.random().toString(36).slice(2)}`
}

export function TrialRunDialog({
  release,
  error,
  submitting = false,
  open,
  onClose,
  onConfirm,
}: {
  release: TeamLabRelease | null
  error?: string | null
  submitting?: boolean
  open: boolean
  onClose: () => void
  onConfirm: (overlays: readonly TeamLabRuntimeOverlay[] | null) => Promise<boolean>
}) {
  const submit = async () => {
    return onConfirm(null)
  }

  return (
    <VNextDialog
      closeDisabled={submitting}
      eyebrow="RUNTIME"
      footer={
        <>
          <ActionButton disabled={submitting} onClick={onClose} type="button">取消</ActionButton>
          <ActionButton loading={submitting} loadingLabel="正在启动" onClick={submit} tone="primary" type="button">启动环境</ActionButton>
        </>
      }
      onClose={onClose}
      open={open && release !== null}
      title="启动运行环境？"
    >
      <div className={styles.content}>
        <p>{release ? `使用发布版本 v${release.version} 创建独立环境。` : ''}</p>
        <p className={styles.note}>首次启动可能较慢，可在运行详情查看部署进度。</p>
        {submitting ? <p role="status">正在创建运行环境，请稍候。</p> : null}
        {error ? <InlineFeedback tone="danger">{error}</InlineFeedback> : null}
      </div>
    </VNextDialog>
  )
}
