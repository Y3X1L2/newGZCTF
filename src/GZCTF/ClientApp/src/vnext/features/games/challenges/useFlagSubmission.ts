import { useCallback, useMemo, useRef, useState } from 'react'
import { encryptApiData } from '@Utils/Crypto'
import api, { AnswerResult } from '@Api'
import { errorMessage } from '../../../shared/errors'
import { ChallengeFeedback } from '../../challenge-runtime/types'

export interface SessionSubmission {
  key: string
  challengeId: number
  challengeTitle: string
  answerPreview: string
  result: AnswerResult | 'Error'
  time: number
}

function submissionKey(challengeId: number, flagId: number | null, value: string) {
  return `${challengeId}:${flagId ?? 0}:${value}`
}

function previewAnswer(value: string) {
  if (value.length <= 18) return value
  return `${value.slice(0, 10)}…${value.slice(-6)}`
}

function resultFeedback(result: AnswerResult): ChallengeFeedback {
  if (result === AnswerResult.Accepted)
    return { tone: 'success', message: 'Flag 正确，题目状态与积分正在刷新。', result }
  if (result === AnswerResult.WrongAnswer)
    return { tone: 'danger', message: 'Flag 错误，请检查内容、格式和所选 Flag 步骤。', result }
  if (result === AnswerResult.CheatDetected)
    return { tone: 'danger', message: '提交被判定为异常，请联系比赛管理员核查。', result }
  if (result === AnswerResult.NotFound) return { tone: 'danger', message: '未找到对应判题记录，请重新提交。', result }
  return { tone: 'neutral', message: 'Flag 已提交，正在等待判题结果。', result }
}

async function resolveSubmission(gameId: number, challengeId: number, submitId: number, initial: AnswerResult) {
  if (initial !== AnswerResult.FlagSubmitted) return initial
  for (let attempt = 0; attempt < 24; attempt += 1) {
    await new Promise((resolve) => window.setTimeout(resolve, 1250))
    const response = await api.game.gameStatus(gameId, challengeId, submitId)
    if (response.data !== AnswerResult.FlagSubmitted) return response.data
  }
  return AnswerResult.FlagSubmitted
}

export function useFlagSubmission(gameId: number, publicKey?: string | null) {
  const [pendingKeys, setPendingKeys] = useState<Set<string>>(() => new Set())
  const pendingKeysRef = useRef<Set<string>>(new Set())
  const [feedback, setFeedback] = useState<Map<number, ChallengeFeedback>>(() => new Map())
  const [submissions, setSubmissions] = useState<SessionSubmission[]>([])

  const submit = useCallback(
    async ({
      challengeId,
      challengeTitle,
      flagId,
      value,
    }: {
      challengeId: number
      challengeTitle: string
      flagId: number | null
      value: string
    }) => {
      const captured = value.trim()
      const key = submissionKey(challengeId, flagId, captured)
      if (!captured || pendingKeysRef.current.has(key)) return null

      pendingKeysRef.current.add(key)
      setPendingKeys(new Set(pendingKeysRef.current))
      setFeedback((current) => new Map(current).set(challengeId, resultFeedback(AnswerResult.FlagSubmitted)))

      try {
        const encrypted = await encryptApiData((translationKey) => translationKey, captured, publicKey)
        const response = await api.game.gameSubmit(gameId, challengeId, {
          flag: encrypted,
          ...(flagId ? { flagId } : {}),
        })
        const result = await resolveSubmission(gameId, challengeId, response.data.id, response.data.status)
        const nextFeedback = resultFeedback(result)
        setFeedback((current) => new Map(current).set(challengeId, nextFeedback))
        setSubmissions((current) =>
          [
            {
              key: `${key}:${Date.now()}`,
              challengeId,
              challengeTitle,
              answerPreview: previewAnswer(captured),
              result,
              time: Date.now(),
            },
            ...current,
          ].slice(0, 12)
        )

        if (result === AnswerResult.Accepted) {
          await Promise.all([
            api.game.mutateGameChallengesWithTeamInfo(gameId),
            api.game.mutateGameGetChallenge(gameId, challengeId),
            api.game.mutateGameScoreboard(gameId),
          ])
        }
        return result
      } catch (requestError) {
        const nextFeedback: ChallengeFeedback = {
          tone: 'danger',
          message: errorMessage(requestError, 'Flag 提交失败，请检查网络后重试。'),
          result: 'Error',
        }
        setFeedback((current) => new Map(current).set(challengeId, nextFeedback))
        setSubmissions((current) =>
          [
            {
              key: `${key}:${Date.now()}`,
              challengeId,
              challengeTitle,
              answerPreview: previewAnswer(captured),
              result: 'Error' as const,
              time: Date.now(),
            },
            ...current,
          ].slice(0, 12)
        )
        return 'Error' as const
      } finally {
        pendingKeysRef.current.delete(key)
        setPendingKeys(new Set(pendingKeysRef.current))
      }
    },
    [gameId, publicKey]
  )

  return useMemo(
    () => ({
      submit,
      submissions,
      feedbackFor: (challengeId: number) => feedback.get(challengeId) ?? null,
      isPending: (challengeId: number, flagId: number | null, value: string) =>
        pendingKeys.has(submissionKey(challengeId, flagId, value.trim())),
    }),
    [feedback, pendingKeys, submissions, submit]
  )
}
