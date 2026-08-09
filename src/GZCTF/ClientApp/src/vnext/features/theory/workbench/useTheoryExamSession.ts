import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { TheoryAnswerModel, TheoryAnswerSheetEditModel, TheoryAnswerSheetStatus, TheoryPlayerPaperModel } from '@Api'
import { errorMessage } from '../../../shared/errors'

export type TheorySaveState = 'idle' | 'dirty' | 'saving' | 'saved' | 'error'

type AnswerRecord = Record<number, number[]>

interface TheoryExamSource {
  initialPaper: TheoryPlayerPaperModel
  saveDraft: (data: TheoryAnswerSheetEditModel) => Promise<TheoryPlayerPaperModel>
  submit: (data: TheoryAnswerSheetEditModel) => Promise<TheoryPlayerPaperModel>
  onSubmitted?: (paper: TheoryPlayerPaperModel) => void
}

function answersFromPaper(paper: TheoryPlayerPaperModel): AnswerRecord {
  return Object.fromEntries(
    (paper.answers ?? [])
      .filter((answer) => answer.paperQuestionId !== undefined)
      .map((answer) => [answer.paperQuestionId as number, [...(answer.selectedIndexes ?? [])].sort((a, b) => a - b)])
  )
}

function paperRevision(paper: TheoryPlayerPaperModel) {
  const answers = (paper.answers ?? [])
    .map((answer) => `${answer.paperQuestionId ?? 0}:${(answer.selectedIndexes ?? []).join(',')}`)
    .join('|')
  return [paper.paperId ?? 0, paper.status ?? 'draft', paper.updatedAt ?? 0, paper.submittedAt ?? 0, answers].join(':')
}

function answerPayload(answers: AnswerRecord): TheoryAnswerSheetEditModel {
  const models: TheoryAnswerModel[] = Object.entries(answers).map(([paperQuestionId, selectedIndexes]) => ({
    paperQuestionId: Number(paperQuestionId),
    selectedIndexes: [...selectedIndexes].sort((a, b) => a - b),
  }))
  return { answers: models }
}

function sameAnswer(left: number[], right: number[]) {
  return left.length === right.length && left.every((value, index) => value === right[index])
}

export function useTheoryExamSession({ initialPaper, saveDraft, submit, onSubmitted }: TheoryExamSource) {
  const initialRevision = paperRevision(initialPaper)
  const initialAnswers = useMemo(() => answersFromPaper(initialPaper), [initialRevision])
  const [paper, setPaper] = useState(initialPaper)
  const [answers, setAnswers] = useState<AnswerRecord>(initialAnswers)
  const [answerRevision, setAnswerRevision] = useState(0)
  const [saveState, setSaveState] = useState<TheorySaveState>(
    initialPaper.status === TheoryAnswerSheetStatus.Submitted ? 'saved' : 'idle'
  )
  const [savedAt, setSavedAt] = useState<number | null>(initialPaper.updatedAt ?? null)
  const [saveError, setSaveError] = useState<string | null>(null)
  const [submitError, setSubmitError] = useState<string | null>(null)
  const [submitting, setSubmitting] = useState(false)
  const [reviewQuestionIds, setReviewQuestionIds] = useState<Set<number>>(() => new Set())

  const answersRef = useRef(initialAnswers)
  const revisionRef = useRef(0)
  const savedRevisionRef = useRef(0)
  const savePromiseRef = useRef<Promise<boolean> | null>(null)
  const submittingRef = useRef(false)
  const submittedRef = useRef(initialPaper.status === TheoryAnswerSheetStatus.Submitted)
  const mountedRef = useRef(true)

  useEffect(() => {
    mountedRef.current = true
    return () => {
      mountedRef.current = false
    }
  }, [])

  useEffect(() => {
    const nextAnswers = answersFromPaper(initialPaper)
    setPaper(initialPaper)
    setAnswers(nextAnswers)
    answersRef.current = nextAnswers
    revisionRef.current = 0
    savedRevisionRef.current = 0
    setAnswerRevision(0)
    submittedRef.current = initialPaper.status === TheoryAnswerSheetStatus.Submitted
    setSaveState(submittedRef.current ? 'saved' : 'idle')
    setSavedAt(initialPaper.updatedAt ?? null)
    setSaveError(null)
    setSubmitError(null)
    setReviewQuestionIds(new Set())
  }, [initialRevision])

  const submitted = paper.status === TheoryAnswerSheetStatus.Submitted

  const updateAnswer = useCallback((questionId: number, selectedIndexes: number[]) => {
    if (submittedRef.current) return
    const normalized = [...new Set(selectedIndexes)].sort((a, b) => a - b)
    const current = answersRef.current[questionId] ?? []
    if (sameAnswer(current, normalized)) return

    const next = { ...answersRef.current, [questionId]: normalized }
    answersRef.current = next
    revisionRef.current += 1
    setAnswers(next)
    setAnswerRevision(revisionRef.current)
    setSaveState('dirty')
    setSaveError(null)
  }, [])

  const saveDraftNow = useCallback(async () => {
    if (submittedRef.current || revisionRef.current <= savedRevisionRef.current) return true
    if (savePromiseRef.current) return savePromiseRef.current

    const request = (async () => {
      while (!submittedRef.current && revisionRef.current > savedRevisionRef.current) {
        const capturedRevision = revisionRef.current
        const payload = answerPayload(answersRef.current)
        if (mountedRef.current) {
          setSaveState('saving')
          setSaveError(null)
        }

        try {
          const response = await saveDraft(payload)
          savedRevisionRef.current = capturedRevision
          submittedRef.current = response.status === TheoryAnswerSheetStatus.Submitted
          if (mountedRef.current) {
            setPaper(response)
            setSavedAt(response.updatedAt ?? Date.now())
          }
        } catch (requestError) {
          if (mountedRef.current) {
            setSaveState('error')
            setSaveError(errorMessage(requestError, '草稿保存失败，本页答案仍然保留，请重试。'))
          }
          return false
        }
      }

      if (mountedRef.current) setSaveState('saved')
      return true
    })()

    savePromiseRef.current = request
    try {
      return await request
    } finally {
      savePromiseRef.current = null
    }
  }, [saveDraft])

  useEffect(() => {
    if (submitted || answerRevision <= savedRevisionRef.current) return undefined
    const timer = window.setTimeout(() => void saveDraftNow(), 800)
    return () => window.clearTimeout(timer)
  }, [answerRevision, saveDraftNow, submitted])

  useEffect(() => {
    const onVisibilityChange = () => {
      if (document.hidden) void saveDraftNow()
    }
    const onBeforeUnload = (event: BeforeUnloadEvent) => {
      if (submittedRef.current || revisionRef.current <= savedRevisionRef.current) return
      void saveDraftNow()
      event.preventDefault()
    }
    document.addEventListener('visibilitychange', onVisibilityChange)
    window.addEventListener('beforeunload', onBeforeUnload)
    return () => {
      document.removeEventListener('visibilitychange', onVisibilityChange)
      window.removeEventListener('beforeunload', onBeforeUnload)
      if (!submittedRef.current && revisionRef.current > savedRevisionRef.current) {
        void saveDraft(answerPayload(answersRef.current)).catch(() => undefined)
      }
    }
  }, [saveDraft, saveDraftNow])

  const submitAnswers = useCallback(async () => {
    if (submittedRef.current || submittingRef.current) return false
    submittingRef.current = true
    setSubmitting(true)
    setSubmitError(null)
    try {
      await saveDraftNow()
      if (submittedRef.current) return true
      const response = await submit(answerPayload(answersRef.current))
      const serverAnswers = answersFromPaper(response)
      if (Object.keys(serverAnswers).length) {
        answersRef.current = serverAnswers
        setAnswers(serverAnswers)
      }
      submittedRef.current = true
      revisionRef.current += 1
      savedRevisionRef.current = revisionRef.current
      setAnswerRevision(revisionRef.current)
      setPaper(response)
      setSaveState('saved')
      setSavedAt(response.updatedAt ?? response.submittedAt ?? Date.now())
      onSubmitted?.(response)
      return true
    } catch (requestError) {
      setSubmitError(errorMessage(requestError, '答卷提交失败，请检查网络后重试。'))
      return false
    } finally {
      submittingRef.current = false
      setSubmitting(false)
    }
  }, [onSubmitted, saveDraftNow, submit])

  const toggleReview = useCallback((questionId: number) => {
    setReviewQuestionIds((current) => {
      const next = new Set(current)
      if (next.has(questionId)) next.delete(questionId)
      else next.add(questionId)
      return next
    })
  }, [])

  return {
    paper,
    answers,
    submitted,
    submitting,
    saveState,
    savedAt,
    saveError,
    submitError,
    reviewQuestionIds,
    updateAnswer,
    saveDraftNow,
    submitAnswers,
    toggleReview,
  }
}
