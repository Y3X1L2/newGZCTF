import api from '@Api'

const swrOptions = { revalidateOnFocus: false } as const

export function useTrainingCourse(courseId: number, enabled: boolean) {
  return api.trainingCourse.useTrainingCourseCourse(courseId, swrOptions, enabled)
}

export function useTrainingChapter(courseId: number, chapterId: number, enabled: boolean) {
  return api.trainingCourse.useTrainingCourseChapter(courseId, chapterId, swrOptions, enabled)
}

export function useTrainingChallenge(courseId: number, chapterId: number, challengeId: number, enabled: boolean) {
  return api.trainingCourse.useTrainingCourseChallenge(courseId, challengeId, { chapterId }, swrOptions, enabled)
}

export const trainingChapterApi = {
  async complete(courseId: number, chapterId: number) {
    await api.trainingCourse.trainingCourseCompleteChapter(courseId, chapterId)
  },
  async createInstance(courseId: number, chapterId: number, challengeId: number) {
    const response = await api.trainingCourse.trainingCourseCreateContainer(courseId, challengeId, { chapterId })
    return response.data
  },
  async extendInstance(courseId: number, chapterId: number, challengeId: number) {
    const response = await api.trainingCourse.trainingCourseExtendContainer(courseId, challengeId, { chapterId })
    return response.data
  },
  async destroyInstance(courseId: number, chapterId: number, challengeId: number) {
    await api.trainingCourse.trainingCourseDestroyContainer(courseId, challengeId, { chapterId })
  },
  async submitFlag(courseId: number, chapterId: number, challengeId: number, flag: string, flagId: number | null) {
    const response = await api.trainingCourse.trainingCourseSubmitFlag(
      courseId,
      challengeId,
      { flag, ...(flagId ? { flagId } : {}) },
      { chapterId }
    )
    return response.data.status
  },
}
