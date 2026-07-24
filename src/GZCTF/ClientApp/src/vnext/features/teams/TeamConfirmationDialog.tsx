import { VNextConfirmDialog } from '../../shared/Interaction'

export type TeamConfirmation =
  | { kind: 'kick'; userId: string; memberName: string }
  | { kind: 'transfer'; userId: string; memberName: string }
  | { kind: 'leave' }
  | { kind: 'delete' }

interface TeamConfirmationDialogProps {
  action: TeamConfirmation | null
  teamName?: string | null
  onClose: () => void
  onConfirm: () => boolean | Promise<boolean>
}

export function TeamConfirmationDialog({ action, teamName, onClose, onConfirm }: TeamConfirmationDialogProps) {
  const content = (() => {
    if (!action) return null
    if (action.kind === 'kick')
      return {
        title: `移出成员 ${action.memberName}？`,
        message: '该成员会立即失去当前战队资格，之后仍可重新申请加入。',
        label: '确认移除',
      }
    if (action.kind === 'transfer')
      return {
        title: `将队长转让给 ${action.memberName}？`,
        message: '转让完成后，你将失去队长管理权限。',
        label: '确认转让',
        tone: 'primary' as const,
      }
    if (action.kind === 'leave')
      return {
        title: `退出战队“${teamName ?? ''}”？`,
        message: '退出后将失去该战队的比赛报名与协作关系。',
        label: '确认退出',
      }
    return {
      title: `删除战队“${teamName ?? ''}”？`,
      message: '战队、成员关系和待处理申请将被永久删除，此操作不可恢复。',
      label: '永久删除',
      confirmationText: teamName ?? '',
    }
  })()

  return (
    <VNextConfirmDialog
      confirmLabel={content?.label}
      confirmationText={content?.confirmationText}
      message={content?.message ?? ''}
      onClose={onClose}
      onConfirm={onConfirm}
      open={action !== null}
      title={content?.title ?? '确认操作'}
      tone={content?.tone}
    />
  )
}
