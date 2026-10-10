import { ActionButton, InlineFeedback, VNextDialog } from '../../../../shared/Interaction'
import { errorMessage } from '../../../../shared/errors'
import type { useTeamLabDeletion } from './useTeamLabDeletion'

export function TeamLabDeleteDialog({ deletion }: { deletion: ReturnType<typeof useTeamLabDeletion> }) {
  const runtime = deletion.target?.kind === 'runtime'
  return <VNextDialog
    open={deletion.target !== null}
    title={runtime ? '删除运行记录' : '删除场景'}
    description={deletion.target ? `${runtime ? '运行环境' : '场景'} ID：${deletion.target.id}` : undefined}
    eyebrow=""
    closeDisabled={deletion.isDeleting}
    onClose={deletion.close}
    footer={<>
      <ActionButton disabled={deletion.isDeleting} onClick={deletion.close} type="button">取消</ActionButton>
      <ActionButton disabled={deletion.isDeleting} onClick={() => void deletion.confirm()} tone="danger" type="button">
        {deletion.isDeleting ? '正在删除' : runtime ? '删除运行记录' : '删除场景'}
      </ActionButton>
    </>}
  >
    <p>{runtime ? `删除“${deletion.target?.name}”的已销毁运行记录及关联流量、审计和抓包历史。` : `删除场景“${deletion.target?.name}”、设计草稿及发布版本。`}</p>
    <p>{runtime ? '这是删除记录，不能代替销毁环境。资源尚未清理或仍有业务引用时，服务端会拒绝删除。' : '请先删除该场景的运行记录，并解除课程或比赛等业务引用。'}</p>
    <p>镜像模板、镜像文件及比赛数据保留。此操作不可撤销。</p>
    {deletion.error ? <InlineFeedback tone="danger">{errorMessage(deletion.error, '删除失败，请稍后重试。')}</InlineFeedback> : null}
  </VNextDialog>
}
