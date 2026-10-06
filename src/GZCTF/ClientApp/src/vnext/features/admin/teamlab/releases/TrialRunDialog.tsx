import { ActionButton, VNextDialog } from '../../../../shared/Interaction'
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
  submitting = false,
  open,
  onClose,
  onConfirm,
}: {
  release: TeamLabRelease | null
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
      description="平台将按服务端执行计划预留资源、分发镜像并启动该不可变版本。"
      eyebrow="RUNTIME"
      footer={
        <>
          <ActionButton disabled={submitting} onClick={onClose} type="button">取消</ActionButton>
          <ActionButton disabled={submitting} onClick={() => void submit()} tone="primary" type="button">
            {submitting ? '正在启动' : '启动环境'}
          </ActionButton>
        </>
      }
      onClose={onClose}
      open={open && release !== null}
      title="启动运行环境？"
    >
      <div className={styles.content}>
        <p>{release ? `使用不可变发布版本 v${release.version} 启动一套独立环境。` : ''}</p>
      </div>
    </VNextDialog>
  )
}
