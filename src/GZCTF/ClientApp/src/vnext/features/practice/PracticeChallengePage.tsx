import { useCallback, useEffect, useMemo, useState } from 'react'
import { useParams, Link } from 'react-router'
import { ArrowLeft, Download, FileArchive } from 'lucide-react'
import { AnswerResult } from '@Api'
import { useExerciseDetail, submitFlag } from './api/practiceApi'
import { useVNextPageTitle } from '../../shared/useVNextPageTitle'
import { DataState } from '../../shared/Primitives'
import { MarkdownContent } from '../../shared/MarkdownContent'
import { safeResourceHref } from '../../shared/urls'
import { FlagSubmission } from '../challenge-runtime/FlagSubmission'
import { InstanceControl } from '../challenge-runtime/InstanceControl'
import { ChallengeFeedback, FlagChallengeState } from '../challenge-runtime/types'
import { usePracticeInstance } from './usePracticeInstance'
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
  const refreshDetail = useCallback(() => mutate(), [mutate])
  const runtimeController = usePracticeInstance({ exerciseId, detail, refreshDetail })
  const solvedFlagIds = useMemo(() => new Set(detail?.solvedFlagIds ?? []), [detail?.solvedFlagIds])

  useEffect(() => {
    const flags = detail?.flags ?? []
    if (!flags.length) {
      setActiveFlagId(null)
      return
    }
    const activeAvailable = activeFlagId && flags.some(flag => flag.id === activeFlagId && !solvedFlagIds.has(flag.id))
    if (!activeAvailable)
      setActiveFlagId(flags.find(flag => !solvedFlagIds.has(flag.id))?.id ?? flags[0].id)
  }, [activeFlagId, detail?.flags, solvedFlagIds])

  const onFlagSubmit = useCallback(async () => {
    if (!flagValue.trim() || flagPending) return
    setFlagPending(true)
    setFeedback(null)
    try {
      const result = await submitFlag(exerciseId, flagValue, activeFlagId ?? undefined)
      if (result?.status === 'Accepted') {
        setFeedback({ tone: 'success', message: '回答正确！', result: AnswerResult.Accepted })
        setFlagValue('')
        await mutate()
      } else if (result?.status === 'WrongAnswer') {
        setFeedback({ tone: 'danger', message: '答案错误，请重试', result: AnswerResult.WrongAnswer })
      } else {
        setFeedback({ tone: 'danger', message: '提交失败', result: AnswerResult.WrongAnswer })
      }
    } catch {
      setFeedback({ tone: 'danger', message: '提交异常，请稍后再试', result: 'Error' as never })
    } finally {
      setFlagPending(false)
    }
  }, [flagValue, activeFlagId, exerciseId, flagPending, mutate])

  const containerChallenge = detail?.type === 'StaticContainer' || detail?.type === 'DynamicContainer'

  const flagChallenge: FlagChallengeState = {
    flags: detail?.flags.map(flag => ({
      id: flag.id,
      orderIndex: flag.orderIndex,
      description: flag.customName || flag.description,
    })) ?? [],
    attempts: detail?.attempts ?? 0,
    limit: detail?.limit ?? null,
  }

  const isSolved = detail?.solved ?? false
  const attachments = detail
    ? [
        detail.context.url ? {
          key: 'exercise',
          label: '题目附件',
          url: safeResourceHref(detail.context.url),
          size: detail.context.fileSize,
        } : null,
        ...detail.flags
          .filter(flag => flag.attachmentUrl && flag.attachmentUrl !== detail.context.url)
          .map(flag => ({
            key: `flag-${flag.id}`,
            label: flag.customName || flag.description || `Flag ${flag.orderIndex + 1} 附件`,
            url: safeResourceHref(flag.attachmentUrl),
            size: flag.attachmentFileSize,
          })),
      ].filter((item): item is { key: string; label: string; url: string; size: number | null } => Boolean(item?.url))
    : []

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
                  {attachments.length ? (
                    <section className={styles.attachmentSection}>
                      <header>
                        <span>ATTACHMENT</span>
                        <h2>题目附件</h2>
                      </header>
                      <div className={styles.attachmentList}>
                        {attachments.map(attachment => (
                          <a href={attachment.url} key={attachment.key} rel="noreferrer noopener" target="_blank">
                            <FileArchive size={19} />
                            <span>
                              <strong>{attachment.label}</strong>
                              {attachment.size ? <small>{Math.ceil(attachment.size / 1024)} KB</small> : null}
                            </span>
                            <Download size={17} />
                          </a>
                        ))}
                      </div>
                    </section>
                  ) : null}
                </div>

                <div className={styles.challengeSidebar}>
                  {containerChallenge ? <InstanceControl controller={runtimeController} /> : null}
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
