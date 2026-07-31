import { CheckCircle2, Flag, LockKeyhole } from 'lucide-react'
import { useState } from 'react'
import { ActionButton, InlineFeedback } from '../../../shared/Interaction'
import { errorMessage } from '../../../shared/errors'
import { teamLabPlayerApi, type TeamLabPlayerWorkspaceProjection } from './api'
import styles from './TeamLabWorkspacePage.module.css'

export function PlayerObjectiveList({
  gameId,
  workspace,
  onSubmitted,
}: {
  gameId: number
  workspace: TeamLabPlayerWorkspaceProjection
  onSubmitted: () => void
}) {
  const [flags, setFlags] = useState<Record<number, string>>({})
  const [submitting, setSubmitting] = useState<number | null>(null)
  const [feedback, setFeedback] = useState<{ objectiveId: number; accepted: boolean; message: string } | null>(null)

  const submit = async (objectiveId: number) => {
    const flag = flags[objectiveId]?.trim()
    if (!flag || submitting !== null) return
    setSubmitting(objectiveId)
    setFeedback(null)
    try {
      const result = await teamLabPlayerApi.submitObjective(gameId, objectiveId, flag)
      setFeedback({ objectiveId, accepted: result.accepted, message: result.message })
      if (result.accepted) {
        setFlags((current) => ({ ...current, [objectiveId]: '' }))
        onSubmitted()
      }
    } catch (reason) {
      setFeedback({ objectiveId, accepted: false, message: errorMessage(reason, '目标提交失败。') })
    } finally {
      setSubmitting(null)
    }
  }

  return (
    <section className={styles.objectives}>
      <header><div><span>OBJECTIVES</span><h2>任务目标</h2></div><strong>{workspace.solvedCount} / {workspace.objectiveCount}</strong></header>
      <div className={styles.targetList}>
        {workspace.targets.map((target) => (
          <section className={styles.target} key={target.assetKey}>
            <header><div><Flag size={16} /><strong>{target.assetKey}</strong></div><span>{target.solvedCount} / {target.objectiveCount}</span></header>
            <ol>
              {target.objectives.map((objective) => (
                <li data-locked={!objective.available || undefined} data-solved={objective.solved || undefined} key={objective.id}>
                  <div className={styles.objectiveTitle}>
                    {objective.solved ? <CheckCircle2 size={17} /> : objective.available ? <Flag size={17} /> : <LockKeyhole size={17} />}
                    <div><strong>{objective.title}</strong><small>{objective.category} · {objective.score} 分</small></div>
                  </div>
                  {objective.description ? <p>{objective.description}</p> : null}
                  {!objective.solved && objective.available ? (
                    <div className={styles.flagForm}>
                      <input
                        aria-label={`${objective.title} Flag`}
                        autoComplete="off"
                        onChange={(event) => setFlags((current) => ({ ...current, [objective.id]: event.currentTarget.value }))}
                        onKeyDown={(event) => {
                          if (event.key === 'Enter') void submit(objective.id)
                        }}
                        placeholder="flag{...}"
                        type="password"
                        value={flags[objective.id] ?? ''}
                      />
                      <ActionButton disabled={!flags[objective.id]?.trim() || submitting !== null} onClick={() => void submit(objective.id)} tone="primary" type="button">
                        {submitting === objective.id ? '提交中' : '提交'}
                      </ActionButton>
                    </div>
                  ) : null}
                  {feedback?.objectiveId === objective.id ? (
                    <InlineFeedback tone={feedback.accepted ? 'success' : 'danger'}>{feedback.message}</InlineFeedback>
                  ) : null}
                </li>
              ))}
            </ol>
          </section>
        ))}
      </div>
    </section>
  )
}
