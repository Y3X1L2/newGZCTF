import type { TrainingCourseChallengeModel, TrainingCourseChapterModel } from '@Api'

export function challengeChapterIds(challenge: TrainingCourseChallengeModel, chapters: TrainingCourseChapterModel[]) {
  const ids = chapters
    .filter((chapter) => chapter.challenges?.some((item) => item.exerciseChallengeId === challenge.exerciseChallengeId))
    .map((chapter) => chapter.id)
    .filter((id): id is number => id !== undefined)
  if (ids.length) return [...new Set(ids)]
  return challenge.chapterId ? [challenge.chapterId] : []
}

export function challengeChapterLabel(challenge: TrainingCourseChallengeModel, chapters: TrainingCourseChapterModel[]) {
  const ids = challengeChapterIds(challenge, chapters)
  return ids.length ? `章节 ${ids.map((id) => `#${id}`).join('、')}` : '未绑定章节'
}
