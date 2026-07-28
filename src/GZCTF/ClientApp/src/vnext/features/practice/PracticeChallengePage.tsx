import { useCallback, useState } from 'react'
import { useParams, Link } from 'react-router'
import { ArrowLeft } from 'lucide-react'
import { useExerciseDetail, submitFlag, createContainer } from './api/practiceApi'
import { useVNextPageTitle } from '../../shared/useVNextPageTitle'
import { DataState } from '../../shared/Primitives'
import { MarkdownContent } from '../../shared/MarkdownContent'
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
  useVNextPageTitle(detail?.title ?? '题目')

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

  const containerChallenge = detail?.type === 'StaticContainer' || detail?.type === 'DynamicContainer'
  const runtimeController: RuntimeInstanceController | null = detail && containerChallenge
    ? {
        kind: 'docker' as const,
        phase: (detail.context.instanceEntry || detail.context.closeTime ? 'running' : 'idle') as RuntimeInstancePhase,
        entry: detail.context.instanceEntry,
        entryStatus: detail.context.instanceEntryStatus,
        entryReadyAt: detail.context.instanceEntryReadyAt,
        entryError: detail.context.instanceEntryError,
        closeTime: detail.context.closeTime,
        error: null,
        busy: containerPending,
        create: onCreateContainer,
        extend: async () => {},
        destroy: async () => {},
        refresh: async () => {},
        vmStatus: null,
      }
    : null

  const flagChallenge: FlagChallengeState = {
    flags: [],
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
                  {categoryLabels[detail.category] ?? detail.category}
                </span>
                <span>{detail.difficulty}</span>
                {detail.tags?.map((t: string) => (
                  <span key={t} className={styles.tag}>{t}</span>
                ))}
              </div>
              <h1 className={styles.challengeDetailTitle}>{detail.title}</h1>
            </div>

            <div className={styles.challengeDetailContent}>
              <div className={styles.challengeDescription}>
                <MarkdownContent source={detail.content} />
                {detail.hints?.map((hint: string, i: number) => (
                  <div key={i} className={styles.hint}>{hint}</div>
                ))}
              </div>

              {runtimeController && (
                <div className={styles.challengeSidebar}>
                  <InstanceControl controller={runtimeController} />
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
