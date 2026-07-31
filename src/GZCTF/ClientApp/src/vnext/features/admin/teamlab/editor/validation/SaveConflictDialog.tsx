import { Download, RefreshCw } from 'lucide-react'
import { ActionButton, VNextDialog } from '../../../../../shared/Interaction'
import { downloadTopologyDraft, type TopologySaveConflict } from '../state/useSaveConflict'

export function SaveConflictDialog({
  conflict,
  onReload,
}: {
  conflict: TopologySaveConflict | null
  onReload: () => void
}) {
  return (
    <VNextDialog
      description="服务器上的场景已被其他会话修改。自动保存已停止，本地草稿不会被覆盖。"
      eyebrow="REVISION CONFLICT"
      footer={
        <>
          <ActionButton icon={<Download size={16} />} onClick={() => conflict && downloadTopologyDraft(conflict)} type="button">
            导出本地草稿
          </ActionButton>
          <ActionButton icon={<RefreshCw size={16} />} onClick={onReload} tone="primary" type="button">
            载入服务器版本
          </ActionButton>
        </>
      }
      onClose={() => undefined}
      open={conflict !== null}
      title="检测到并发编辑"
    >
      <p>本地草稿基于修订 {conflict?.expectedRevision ?? '—'}。可先导出留档，再载入服务器最新版本继续编辑。</p>
    </VNextDialog>
  )
}
