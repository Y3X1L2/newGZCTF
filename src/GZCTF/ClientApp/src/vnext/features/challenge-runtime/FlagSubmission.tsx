import { Check, Flag, LoaderCircle, Send } from 'lucide-react'
import { FormEvent, useId } from 'react'
import { AnswerResult } from '@Api'
import { ActionButton, InlineFeedback } from '../../shared/Interaction'
import styles from './FlagSubmission.module.css'
import { ChallengeFeedback, FlagChallengeState, FlagStep } from './types'

function flagLabel(flag: FlagStep, index: number) {
  return flag.description?.trim() || `Flag ${flag.orderIndex ?? index + 1}`
}

export function FlagSubmission({
  challenge,
  value,
  activeFlagId,
  solvedFlagIds,
  solved,
  disabledReason,
  pending,
  feedback,
  onValueChange,
  onFlagChange,
  onSubmit,
}: {
  challenge: FlagChallengeState
  value: string
  activeFlagId: number | null
  solvedFlagIds: Set<number>
  solved: boolean
  disabledReason: string | null
  pending: boolean
  feedback: ChallengeFeedback | null
  onValueChange: (value: string) => void
  onFlagChange: (flagId: number) => void
  onSubmit: () => void
}) {
  const titleId = useId()
  const flags = challenge.flags ?? []
  const hasMultipleFlags = flags.length > 1
  const attempts = challenge.attempts ?? 0
  const remaining = challenge.limit ? Math.max(0, challenge.limit - attempts) : null

  const submitForm = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    onSubmit()
  }

  return (
    <section aria-labelledby={titleId} className={styles.flagSection}>
      <header className={styles.sectionHeader}>
        <div>
          <span>SUBMISSION</span>
          <h2 id={titleId}>提交 Flag</h2>
        </div>
        <small>{remaining === null ? `已提交 ${attempts} 次` : `剩余 ${remaining} 次`}</small>
      </header>

      {hasMultipleFlags ? (
        <div aria-label="Flag 步骤" className={styles.flagSteps}>
          {flags.map((flag, index) => {
            const id = flag.id ?? index + 1
            const isSolved = solvedFlagIds.has(id)
            return (
              <button
                className={activeFlagId === id ? styles.flagStepActive : styles.flagStep}
                disabled={isSolved}
                key={id}
                onClick={() => onFlagChange(id)}
                type="button"
              >
                {isSolved ? <Check size={15} /> : <Flag size={15} />}
                <span>{flagLabel(flag, index)}</span>
              </button>
            )
          })}
        </div>
      ) : null}

      {feedback ? <InlineFeedback tone={feedback.tone}>{feedback.message}</InlineFeedback> : null}
      {solved ? <InlineFeedback tone="success">该题目已经完成。</InlineFeedback> : null}
      {disabledReason ? <InlineFeedback>{disabledReason}</InlineFeedback> : null}

      <form className={styles.flagForm} onSubmit={submitForm}>
        <label>
          <span className={styles.srOnly}>Flag 内容</span>
          <input
            autoComplete="off"
            disabled={solved || Boolean(disabledReason) || pending}
            onChange={(event) => onValueChange(event.currentTarget.value)}
            placeholder="flag{...}"
            spellCheck={false}
            type="text"
            value={value}
          />
        </label>
        <ActionButton
          disabled={!value.trim() || solved || Boolean(disabledReason) || pending}
          icon={pending ? <LoaderCircle className={styles.spin} size={16} /> : <Send size={16} />}
          tone="primary"
          type="submit"
        >
          {pending ? '判题中' : '提交'}
        </ActionButton>
      </form>

      {feedback?.result === AnswerResult.WrongAnswer ? (
        <p className={styles.submitNote}>错误结果不会清空输入，可直接检查并修改。</p>
      ) : null}
    </section>
  )
}
