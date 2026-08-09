import { VNextConfirmDialog } from '../../../../shared/Interaction'
import type { TeamLabRelease } from '../api'

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
  open,
  onClose,
  onConfirm,
}: {
  release: TeamLabRelease | null
  open: boolean
  onClose: () => void
  onConfirm: () => Promise<boolean>
}) {
  return (
    <VNextConfirmDialog
      confirmLabel="创建试运行"
      description="平台将按服务端执行计划预留资源、分发镜像并启动该不可变版本。"
      message={release ? `为发布版本 v${release.version} 创建一套独立试运行环境。` : ''}
      onClose={onClose}
      onConfirm={onConfirm}
      open={open && release !== null}
      title="启动 TeamLab 试运行？"
      tone="primary"
    />
  )
}
