import api, { TheoryAnswerSheetEditModel } from '@Api'

const swrOptions = { revalidateOnFocus: false } as const

export function useTrainingCourses() {
  return api.trainingCourse.useTrainingCourseCourses(swrOptions)
}

export function useTrainingOverview(enabled = true) {
  return api.trainingCourse.useTrainingCourseOverview(swrOptions, enabled)
}

export function useTrainingCourseDetail(courseId: number, enabled: boolean) {
  return api.trainingCourse.useTrainingCourseCourse(courseId, swrOptions, enabled)
}

export function useTrainingTheoryContext(courseId: number, chapterId: number, enabled: boolean) {
  const course = api.trainingCourse.useTrainingCourseCourse(courseId, swrOptions, enabled)
  const chapter = api.trainingCourse.useTrainingCourseChapter(courseId, chapterId, swrOptions, enabled)
  const paper = api.trainingCourse.useTrainingCourseChapterTheory(
    courseId,
    chapterId,
    { ...swrOptions, shouldRetryOnError: false },
    enabled
  )
  return { course, chapter, paper }
}

export const trainingLearnerApi = {
  async checkIn() {
    const response = await api.trainingCourse.trainingCourseCheckIn()
    return response.data
  },
  async enroll(courseId: number, applyReason: string) {
    await api.trainingCourse.trainingCourseEnroll(courseId, { applyReason })
  },
  async cancelEnrollment(courseId: number) {
    await api.trainingCourse.trainingCourseCancelEnroll(courseId)
  },
  async saveTheoryDraft(courseId: number, chapterId: number, data: TheoryAnswerSheetEditModel) {
    const response = await api.trainingCourse.trainingCourseSaveChapterTheoryDraft(courseId, chapterId, data)
    return response.data
  },
  async submitTheory(courseId: number, chapterId: number, data: TheoryAnswerSheetEditModel) {
    const response = await api.trainingCourse.trainingCourseSubmitChapterTheory(courseId, chapterId, data)
    return response.data
  },
  async retryTheory(courseId: number, chapterId: number) {
    const response = await api.trainingCourse.trainingCourseRetryChapterTheory(courseId, chapterId)
    return response.data
  },
}
