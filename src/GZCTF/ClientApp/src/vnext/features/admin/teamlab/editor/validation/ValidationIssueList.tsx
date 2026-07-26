import { AlertTriangle, LocateFixed } from 'lucide-react'
import type { TeamLabValidationIssue } from '../../api'
import styles from './Validation.module.css'
import { formatValidationMessage, formatValidationPath } from './validationPresentation'

export function ValidationIssueList({
  issues,
  onLocate,
}: {
  issues: readonly TeamLabValidationIssue[]
  onLocate: (issue: TeamLabValidationIssue) => void
}) {
  if (issues.length === 0) return <p className={styles.empty}>当前修订没有校验问题。</p>
  return (
    <ol className={styles.issueList}>
      {issues.map((issue, index) => {
        const path = formatValidationPath(issue.path)
        return (
          <li key={`${issue.code}:${issue.path}:${index}`}>
            <AlertTriangle aria-hidden size={17} />
            <div>
              <strong>{formatValidationMessage(issue)}</strong>
              <code>{path}</code>
            </div>
            <button aria-label={`定位到${path}`} onClick={() => onLocate(issue)} title="在画布中定位" type="button">
              <LocateFixed size={16} />
            </button>
          </li>
        )
      })}
    </ol>
  )
}
