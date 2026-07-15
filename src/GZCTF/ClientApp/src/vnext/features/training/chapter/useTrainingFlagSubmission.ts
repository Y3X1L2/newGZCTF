import { useCallback, useMemo, useRef, useState } from 'react'
import api, { AnswerResult, TrainingCourseChallengeDetailModel } from '@Api'
import { errorMessage } from '../../../shared/errors'
import { ChallengeFeedback } from '../../challenge-runtime/types'

function feedbackForResult(result: AnswerResult): ChallengeFeedback {
  if (result === AnswerResult.Accepted)
    return { tone: 'success', message: 'Flag 正确，实验状态与章节进度已刷新。', result }
  if (result === AnswerResult.WrongAnswer) return { tone: 'danger', message: 'Flag 错误，请检查内容和格式。', result }
  if (result === AnswerResult.CheatDetected)
    return { tone: 'danger', message: '提交被判定为异常，请联系课程教师核查。', result }
  if (result === AnswerResult.NotFound) return { tone: 'danger', message: '未找到对应判题记录，请重新提交。', result }
  return { tone: 'neutral', message: 'Flag 已提交，正在等待判题结果。', result }
}

export function useTrainingFlagSubmission({
  courseId,
  chapterId,
  challenge,
  updateChallenge,
  refreshChallenge,
  onAccepted,
}: {
  courseId: number
  chapterId: number
  challenge?: TrainingCourseChallengeDetailModel
  updateChallenge: (next: TrainingCourseChallengeDetailModel) => void
  refreshChallenge: () => Promise<TrainingCourseChallengeDetailModel | undefined>
  onAccepted: () => Promise<void>
}) {
  const pendingRef = useRef<Set<string>>(new Set())
  const [pendingKeys, setPendingKeys] = useState<Set<string>>(() => new Set())
  const [feedback, setFeedback] = useState<ChallengeFeedback | null>(null)

  const submit = useCallback(
    async ({ challengeId, flagId, value }: { challengeId: number; flagId: number | null; value: string }) => {
      const captured = value.trim()
      const key = `${challengeId}:${flagId ?? 0}:${captured}`
      if (!captured || pendingRef.current.has(key)) return null

      pendingRef.current.add(key)
      setPendingKeys(new Set(pendingRef.current))
      setFeedback(feedbackForResult(AnswerResult.FlagSubmitted))

      try {
        const response = await api.trainingCourse.trainingCourseSubmitFlag(
          courseId,
          challengeId,
          { flag: captured, ...(flagId ? { flagId } : {}) },
          { chapterId }
        )
        const result = response.data.status ?? AnswerResult.NotFound
        setFeedback(feedbackForResult(result))
        if (result === AnswerResult.Accepted) {
          updateChallenge({
            ...challenge,
            solved: true,
            attempts: (challenge?.attempts ?? 0) + 1,
          })
          await Promise.all([refreshChallenge(), onAccepted()])
        } else {
          await refreshChallenge()
        }
        return result
      } catch (requestError) {
        const next: ChallengeFeedback = {
          tone: 'danger',
          message: errorMessage(requestError, 'Flag 提交失败，请检查网络后重试。'),
          result: 'Error',
        }
        setFeedback(next)
        return 'Error' as const
      } finally {
        pendingRef.current.delete(key)
        setPendingKeys(new Set(pendingRef.current))
      }
    },
    [challenge, chapterId, courseId, onAccepted, refreshChallenge, updateChallenge]
  )

  return useMemo(
    () => ({
      feedback,
      submit,
      isPending: (challengeId: number, flagId: number | null, value: string) =>
        pendingKeys.has(`${challengeId}:${flagId ?? 0}:${value.trim()}`),
    }),
    [feedback, pendingKeys, submit]
  )
}
