import { useCallback, useEffect, useMemo, useState } from 'react'
import { TrainingCourseVideoProvider } from '@Api'
import { markdownOutline } from '../../../shared/MarkdownContent'
import { errorMessage } from '../../../shared/errors'
import { trainingChapterApi, useTrainingChapter, useTrainingCourse } from './trainingChapterApi'
import { trainingChapterProgress } from './trainingChapterDomain'

export function useTrainingChapterController(courseId: number, chapterId: number, locationHash: string) {
  const validIds = Number.isInteger(courseId) && courseId > 0 && Number.isInteger(chapterId) && chapterId > 0
  const courseRequest = useTrainingCourse(courseId, validIds)
  const chapterRequest = useTrainingChapter(courseId, chapterId, validIds)
  const course = courseRequest.data
  const chapter = chapterRequest.data
  const [completing, setCompleting] = useState(false)
  const [completionFeedback, setCompletionFeedback] = useState<{ tone: 'success' | 'danger'; message: string } | null>(
    null
  )

  const orderedChapters = useMemo(
    () =>
      [...(course?.chapters ?? [])]
        .filter((item) => item.id !== undefined && (course?.canEdit || item.isPublished))
        .sort((left, right) => (left.order ?? 0) - (right.order ?? 0) || (left.id ?? 0) - (right.id ?? 0)),
    [course]
  )
  const currentIndex = orderedChapters.findIndex((item) => item.id === chapterId)
  const previousChapter = currentIndex > 0 ? orderedChapters[currentIndex - 1] : null
  const nextChapter =
    currentIndex >= 0 && currentIndex < orderedChapters.length - 1 ? orderedChapters[currentIndex + 1] : null
  const progress = chapter ? trainingChapterProgress(chapter) : null
  const outline = useMemo(
    () =>
      chapter
        ? [
            ...(chapter.videoProvider && chapter.videoProvider !== TrainingCourseVideoProvider.None
              ? [{ id: 'chapter-video', label: '章节视频', level: 2 as const }]
              : []),
            { id: 'chapter-content', label: '章节正文', level: 2 as const },
            ...markdownOutline(chapter.content ?? ''),
            ...((chapter.challenges?.length ?? 0) > 0
              ? [{ id: 'chapter-labs', label: '章节实验', level: 2 as const }]
              : []),
            ...(chapter.theoryPaper ? [{ id: 'chapter-theory', label: '课后练习', level: 2 as const }] : []),
            { id: 'chapter-completion', label: '章节完成', level: 2 as const },
          ]
        : [],
    [chapter]
  )

  const refreshProgress = useCallback(async () => {
    await Promise.all([chapterRequest.mutate(), courseRequest.mutate()])
  }, [chapterRequest, courseRequest])

  useEffect(() => {
    window.scrollTo({ top: 0 })
  }, [chapterId])

  useEffect(() => {
    if (!chapter || !locationHash) return
    const encodedTargetId = locationHash.slice(1)
    let targetId = encodedTargetId
    try {
      targetId = decodeURIComponent(encodedTargetId)
    } catch {
      // Keep malformed external fragments inert instead of failing the route.
    }
    const timer = window.setTimeout(() => document.getElementById(targetId)?.scrollIntoView({ block: 'start' }), 80)
    return () => window.clearTimeout(timer)
  }, [chapter, locationHash])

  const completeChapter = async () => {
    if (!progress || progress.completed || completing || !progress.blockingConditions) return
    setCompleting(true)
    setCompletionFeedback(null)
    try {
      await trainingChapterApi.complete(courseId, chapterId)
      await refreshProgress()
      setCompletionFeedback({ tone: 'success', message: '章节已经完成，课程进度已刷新。' })
    } catch (requestError) {
      setCompletionFeedback({ tone: 'danger', message: errorMessage(requestError, '章节完成条件尚未满足。') })
      await refreshProgress()
    } finally {
      setCompleting(false)
    }
  }

  return {
    validIds,
    course,
    chapter,
    loading: (!course || !chapter) && !courseRequest.error && !chapterRequest.error,
    orderedChapters,
    currentIndex,
    previousChapter,
    nextChapter,
    progress,
    outline,
    completing,
    completionFeedback,
    refreshProgress,
    completeChapter,
  }
}
