import { useCallback, useState } from 'react'
import { useParams, Link } from 'react-router'
import { ArrowLeft, LoaderCircle } from 'lucide-react'
import { useExerciseDetail, submitFlag, createContainer } from './api/practiceApi'
import { useVNextPageTitle } from '../../shared/useVNextPageTitle'
import { DataState, PageHeading, StatusPill } from '../../shared/Primitives'
import { ActionButton, InlineFeedback } from '../../shared/Interaction'
import { FlagSubmission } from '../challenge-runtime/FlagSubmission'
import { InstanceControl } from '../challenge-runtime/InstanceControl'
import { ChallengeFeedback, FlagChallengeState, RuntimeInstanceController, RuntimeInstancePhase } from '../challenge-runtime/types'
import styles from './PracticePage.module.css'

const categoryLabels: Record<string, string> = {
  Web: 'Web', Pwn: 'Pwn', Reverse: 'Reverse', Crypto: 'Crypto',
  Misc: 'Misc', Forensics: 'Forensics', Mobile: 'Mobile',
  Blockchain: 'Blockchain', Programming: 'Programming',
  OSint: 'OSINT', Hardware: 'Hardware',
}

export function PracticeChallengePage() {
  const { id } = useParams<{ id: string }>()
  const exerciseId = Number(id)
  const { data: detail, error, mutate } = useExerciseDetail(exerciseId)
  useVNextPageTitle(detail?.exercise?.title ?? '题目')

  const [flagValue, setFlagValue] = useState('')
  const [activeFlagId, setActiveFlagId] = useState<number | null>(null)
  const [flagPending, setFlagPending] = useState(false)
  const [feedback, setFeedback] = useState<ChallengeFeedback | null>(null)
  const [solvedFlagIds, setSolvedFlagIds] = useState<Set<number>>(new Set())

  const [containerPending, setContainerPending] = useState(false)

  const onFlagSubmit = useCallback(async () => {
    if (!flagValue.trim() || flagPending) return
    setFlagPending(true)
    setFeedback(null)
    try {
      const result = await submitFlag(exerciseId, flagValue, activeFlagId ?? undefined) as { status?: string }
      if (result?.status === 'Accepted') {
        setFeedback({ tone: 'success', message: '回答正确！', result: 'Accepted' as never })
        if (activeFlagId) setSolvedFlagIds(prev => new Set([...prev, activeFlagId]))
        mutate()
      } else if (result?.status === 'WrongAnswer') {
        setFeedback({ tone: 'danger', message: '答案错误，请重试', result: 'WrongAnswer' as never })
      } else {
        setFeedback({ tone: 'danger', message: '提交失败', result: 'WrongAnswer' as never })
      }
    } catch {
      setFeedback({ tone: 'danger', message: '提交异常，请稍后再试', result: 'Error' as never })
    } finally {
      setFlagPending(false)
    }
  }, [flagValue, activeFlagId, exerciseId, flagPending, mutate])

  const onCreateContainer = useCallback(async () => {
    setContainerPending(true)
    try {
      await createContainer(exerciseId)
      mutate()
    } finally {
      setContainerPending(false)
    }
  }, [exerciseId, mutate])

  const runtimeController: RuntimeInstanceController | null = detail?.container
    ? {
        kind: 'container' as const,
        phase: 'running' as RuntimeInstancePhase,
        ...detail.container,
        entry: detail.container.entry ?? null,
        entryStatus: (detail.container.entryStatus ?? null) as never,
        entryReadyAt: detail.container.entryReadyAt ?? null,
        entryError: detail.container.entryError ?? null,
        closeTime: detail.container.closeTime ?? null,
        error: detail.container.error ?? null,
        busy: false,
        create: onCreateContainer,
        extend: async () => {},
        destroy: async () => {},
        refresh: async () => {},
        vmStatus: null,
      }
    : null

  const flagChallenge: FlagChallengeState = {
    flags: (detail?.exercise?.flags ?? []).map(f => ({ id: f.id, orderIndex: null, description: null })),
    attempts: 0,
    limit: null,
  }

  const isSolved = solvedFlagIds.size > 0

  return (
    <div className={styles.page}>
      <Link to="/practice/browse" className={styles.backLink}>
        <ArrowLeft size={16} /> 返回题库
      </Link>

      <DataState data={detail} error={error} loading={!detail && !error}>
        {detail && (
          <>
            <div className={styles.challengeDetailHeader}>
              <div className={styles.challengeDetailMeta}>
                <span className={styles.challengeCategory}>
                  {categoryLabels[detail.exercise?.category ?? ''] ?? detail.exercise?.category}
                </span>
                <span>{detail.exercise?.difficulty}</span>
                {detail.exercise?.tags?.map((t: string) => (
                  <span key={t} className={styles.tag}>{t}</span>
                ))}
              </div>
              <h1 className={styles.challengeDetailTitle}>{detail.exercise?.title}</h1>
            </div>

            <div className={styles.challengeDetailContent}>
              <div className={styles.challengeDescription}>
                <div dangerouslySetInnerHTML={{ __html: detail.exercise?.content ?? '' }} />
                {detail.exercise?.hints?.map((hint: string, i: number) => (
                  <div key={i} className={styles.hint}>{hint}</div>
                ))}
              </div>

              {runtimeController && (
                <div className={styles.challengeSidebar}>
                  {runtimeController.kind === 'idle' ? (
                    <ActionButton
                      icon={containerPending ? <LoaderCircle size={16} className={styles.spin} /> : undefined}
                      tone="primary"
                      onClick={onCreateContainer}
                      disabled={containerPending}
                    >
                      {containerPending ? '启动中' : '启动环境'}
                    </ActionButton>
                  ) : (
                    <InstanceControl controller={runtimeController} />
                  )}
                </div>
              )}

              <div className={styles.challengeSidebar}>
                <FlagSubmission
                  challenge={flagChallenge}
                  value={flagValue}
                  activeFlagId={activeFlagId}
                  solvedFlagIds={solvedFlagIds}
                  solved={isSolved}
                  disabledReason={null}
                  pending={flagPending}
                  feedback={feedback}
                  onValueChange={setFlagValue}
                  onFlagChange={setActiveFlagId}
                  onSubmit={onFlagSubmit}
                />
              </div>
            </div>
          </>
        )}
      </DataState>
    </div>
  )
}
