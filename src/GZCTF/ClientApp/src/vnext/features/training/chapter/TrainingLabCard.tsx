import { Download, ExternalLink } from 'lucide-react'
import { useCallback, useEffect, useMemo, useState } from 'react'
import { ChallengeType, EnvironmentType, TrainingCourseChallengeDetailModel, TrainingCourseChallengeModel } from '@Api'
import { InlineFeedback } from '../../../shared/Interaction'
import { MarkdownContent } from '../../../shared/MarkdownContent'
import { DataState, StatusPill } from '../../../shared/Primitives'
import { safeResourceHref } from '../../../shared/urls'
import { FlagSubmission } from '../../challenge-runtime/FlagSubmission'
import { InstanceControl } from '../../challenge-runtime/InstanceControl'
import { formatFileSize } from '../training'
import styles from './TrainingChapterPage.module.css'
import { useTrainingChallenge } from './trainingChapterApi'
import { useTrainingFlagSubmission } from './useTrainingFlagSubmission'
import { useTrainingInstance } from './useTrainingInstance'

function challengeTypeLabel(type?: ChallengeType) {
  if (type === ChallengeType.DynamicContainer) return '动态容器'
  if (type === ChallengeType.StaticContainer) return '静态容器'
  if (type === ChallengeType.DynamicAttachment) return '动态附件'
  return '静态附件'
}

function environmentLabel(environment?: EnvironmentType) {
  if (environment === EnvironmentType.WindowsVM) return 'Windows'
  if (environment === EnvironmentType.Docker) return 'Docker'
  return '无运行环境'
}

interface TrainingLabCardProps {
  courseId: number
  chapterId: number
  challenge: TrainingCourseChallengeModel
  refreshProgress: () => Promise<void>
}

export function TrainingLabCard({ courseId, chapterId, challenge, refreshProgress }: TrainingLabCardProps) {
  const challengeId = challenge.exerciseChallengeId ?? 0
  const challengeRequest = useTrainingChallenge(courseId, chapterId, challengeId, challengeId > 0)
  const detail = challengeRequest.data
  const [flagValue, setFlagValue] = useState('')
  const [activeFlagId, setActiveFlagId] = useState<number | null>(null)

  const updateChallenge = useCallback(
    (next: TrainingCourseChallengeDetailModel) => {
      void challengeRequest.mutate(next, { revalidate: false })
    },
    [challengeRequest]
  )
  const refreshChallenge = useCallback(async () => challengeRequest.mutate(), [challengeRequest])
  const instance = useTrainingInstance({
    courseId,
    chapterId,
    challenge: detail,
    updateChallenge,
    refreshChallenge,
  })
  const flagSubmission = useTrainingFlagSubmission({
    courseId,
    chapterId,
    challenge: detail,
    updateChallenge,
    refreshChallenge,
    onAccepted: refreshProgress,
  })

  useEffect(() => {
    if (activeFlagId || !detail?.flags?.length) return
    setActiveFlagId(detail.flags[0].id ?? null)
  }, [activeFlagId, detail?.flags])

  const solvedFlagIds = useMemo(
    () => new Set(detail?.solved ? (detail.flags ?? []).map((flag, index) => flag.id ?? index + 1) : []),
    [detail?.flags, detail?.solved]
  )
  const pending = detail ? flagSubmission.isPending(detail.id ?? challengeId, activeFlagId, flagValue) : false
  const submissionLocked = detail?.limit && (detail.attempts ?? 0) >= detail.limit ? '该实验的提交次数已用完。' : null
  const attachmentUrl = safeResourceHref(detail?.context?.url)

  return (
    <article className={styles.labItem}>
      <header className={styles.labHeader}>
        <div>
          <div className={styles.labTags}>
            <StatusPill tone="info">{String(challenge.category ?? '实验')}</StatusPill>
            <StatusPill>{challengeTypeLabel(challenge.type)}</StatusPill>
            <StatusPill>{environmentLabel(challenge.environment)}</StatusPill>
            <StatusPill tone={challenge.isRequired ? 'warning' : 'neutral'}>
              {challenge.isRequired ? '必做' : '选做'}
            </StatusPill>
          </div>
          <h3>{challenge.displayTitle || challenge.title || `实验 ${challengeId}`}</h3>
        </div>
        <StatusPill tone={detail?.solved || challenge.solved ? 'success' : 'neutral'}>
          {detail?.solved || challenge.solved ? '已完成' : '待完成'}
        </StatusPill>
      </header>

      {!detail && !challengeRequest.error ? (
        <DataState description="正在读取实验配置、附件和运行状态。" loading title="实验加载中" />
      ) : challengeRequest.error || !detail ? (
        <DataState description="实验配置暂时无法读取，请刷新页面后重试。" title="实验加载失败" />
      ) : (
        <>
          {detail.content ? <MarkdownContent className={styles.labDescription} source={detail.content} /> : null}
          {attachmentUrl ? (
            <a className={styles.attachment} href={attachmentUrl} rel="noreferrer noopener" target="_blank">
              <span>
                <Download size={18} />
                <span>
                  <strong>{challenge.attachmentFileName || '下载实验附件'}</strong>
                  <small>{formatFileSize(detail.context?.fileSize)}</small>
                </span>
              </span>
              <ExternalLink size={17} />
            </a>
          ) : challenge.hasAttachment ? (
            <InlineFeedback>实验已绑定附件，但当前下载地址暂不可用。</InlineFeedback>
          ) : null}
          <InstanceControl controller={instance} />
          <FlagSubmission
            activeFlagId={activeFlagId}
            challenge={detail}
            disabledReason={submissionLocked}
            feedback={flagSubmission.feedback}
            onFlagChange={setActiveFlagId}
            onSubmit={() =>
              void flagSubmission.submit({
                challengeId: detail.id ?? challengeId,
                flagId: activeFlagId,
                value: flagValue,
              })
            }
            onValueChange={setFlagValue}
            pending={pending}
            solved={Boolean(detail.solved)}
            solvedFlagIds={solvedFlagIds}
            value={flagValue}
          />
        </>
      )}
    </article>
  )
}
