import { VNextDrawer } from '../../../../../shared/Interaction'
import type { TeamLabValidationIssue, TeamLabValidationResult } from '../../api'
import { ValidationIssueList } from './ValidationIssueList'

export function ValidationDrawer({
  open,
  result,
  onClose,
  onLocate,
}: {
  open: boolean
  result: TeamLabValidationResult | null
  onClose: () => void
  onLocate: (issue: TeamLabValidationIssue) => void
}) {
  return (
    <VNextDrawer
      description={result?.valid ? '当前服务端修订已通过发布门禁。' : '修正问题后重新运行服务端校验。'}
      eyebrow="服务端校验"
      onClose={onClose}
      open={open}
      size="medium"
      title={result?.valid ? '校验通过' : `${result?.issues.length ?? 0} 个校验问题`}
    >
      <ValidationIssueList issues={result?.issues ?? []} onLocate={onLocate} />
    </VNextDrawer>
  )
}
