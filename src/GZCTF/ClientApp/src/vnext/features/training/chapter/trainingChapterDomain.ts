import { TheoryAnswerSheetStatus, TrainingCourseChapterModel, TrainingCourseProgressStatus } from '@Api'

export function chapterDepth(chapter: TrainingCourseChapterModel, chapters: TrainingCourseChapterModel[]) {
  const parents = new Map(chapters.filter((item) => item.id !== undefined).map((item) => [item.id as number, item]))
  let parentId = chapter.parentId
  let depth = 0
  while (parentId && parents.has(parentId) && depth < 2) {
    depth += 1
    parentId = parents.get(parentId)?.parentId
  }
  return depth
}

export function trainingChapterProgress(chapter: TrainingCourseChapterModel) {
  const policy = chapter.completionPolicy ?? {}
  const candidateChallenges = policy.requireAllRequiredChallenges
    ? (chapter.challenges ?? []).filter((item) => item.isRequired)
    : (chapter.challenges ?? [])
  const requiredChallengeCount = policy.requireAllRequiredChallenges
    ? candidateChallenges.length
    : Math.min(policy.requiredChallengeCount ?? 0, candidateChallenges.length)
  const solvedChallengeCount = candidateChallenges.filter((item) => item.solved).length
  const challengesSatisfied = solvedChallengeCount >= requiredChallengeCount
  const theoryRequired = Boolean(chapter.theoryPaper?.isPublished)
  const theoryTotal = chapter.theoryPaper?.totalScore ?? 0
  const theoryScore = chapter.theoryPaper?.score ?? 0
  const theoryRate = policy.theoryPassRate ?? chapter.theoryPaper?.passRate ?? 0
  const theorySubmitted = chapter.theoryPaper?.status === TheoryAnswerSheetStatus.Submitted
  const theorySatisfied =
    !theoryRequired || (theorySubmitted && (theoryTotal === 0 || theoryScore * 100 >= theoryTotal * theoryRate))
  const contentSatisfied = !policy.requireContentRead || (chapter.readPercent ?? 0) >= 100
  const completed = chapter.progressStatus === TrainingCourseProgressStatus.Completed || Boolean(chapter.completedAt)

  return {
    requiredChallengeCount,
    solvedChallengeCount,
    challengesSatisfied,
    theoryRequired,
    theoryTotal,
    theoryScore,
    theoryRate,
    theorySubmitted,
    theorySatisfied,
    contentSatisfied,
    completed,
    blockingConditions: challengesSatisfied && theorySatisfied,
  }
}
