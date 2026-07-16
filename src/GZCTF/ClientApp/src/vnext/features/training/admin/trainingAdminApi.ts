import api, {
  TheoryQuestionEditModel,
  ImageStatus,
  TrainingCourseChallengeCreateModel,
  TrainingCourseChapterEditModel,
  TrainingCourseChapterTheoryPaperEditModel,
  TrainingCourseEditModel,
  TrainingCourseEnrollmentStatus,
  TrainingCourseImageTemplateModel,
  TrainingCourseResourceEditModel,
  TrainingCourseTeacherRole,
} from '@Api'

const swrOptions = { revalidateOnFocus: false } as const
const resilientSwrOptions = { ...swrOptions, shouldRetryOnError: false } as const

export function useTrainingAdminCourse(courseId: number, enabled: boolean) {
  return api.trainingCourseAdmin.useTrainingCourseAdminCourse(courseId, swrOptions, enabled)
}

export function useTrainingAdminChapter(courseId: number, chapterId: number, enabled: boolean) {
  return api.trainingCourse.useTrainingCourseChapter(courseId, chapterId, swrOptions, enabled)
}

export function useTrainingAdminChallenge(courseId: number, challengeId: number, enabled: boolean) {
  return api.trainingCourseAdmin.useTrainingCourseAdminCourseChallengeEditDetail(
    courseId,
    challengeId,
    swrOptions,
    enabled
  )
}

export function useTrainingAdminImageTemplates(courseId: number, enabled: boolean) {
  return api.trainingCourseAdmin.useTrainingCourseAdminImageTemplates(
    courseId,
    {
      ...swrOptions,
      refreshInterval: (data?: TrainingCourseImageTemplateModel[]) =>
        data?.some((item) => item.status === ImageStatus.Importing) ? 5000 : 0,
    },
    enabled
  )
}

export function useTrainingAdminTheoryQuestions(courseId: number, count: number, enabled: boolean) {
  return api.trainingCourseAdmin.useTrainingCourseAdminTheoryQuestions(courseId, { count }, swrOptions, enabled)
}

export function useTrainingAdminTheoryPaper(courseId: number, chapterId: number, enabled: boolean) {
  return api.trainingCourseAdmin.useTrainingCourseAdminChapterTheoryPaper(courseId, chapterId, swrOptions, enabled)
}

export function useTrainingAdminEnrollments(courseId: number, enabled: boolean) {
  return api.trainingCourseAdmin.useTrainingCourseAdminEnrollments(courseId, resilientSwrOptions, enabled)
}

export function useTrainingAdminLearningSummaries(courseId: number, enabled: boolean) {
  return api.trainingCourseAdmin.useTrainingCourseAdminLearningSummaries(courseId, resilientSwrOptions, enabled)
}

export async function uploadTrainingAsset(file: File) {
  const response = await api.assets.assetsUpload({ files: [file] }, { filename: file.name })
  const hash = response.data?.[0]?.hash
  if (!hash) throw new Error('文件已上传，但服务器没有返回可用的文件标识。')
  return hash
}

export const trainingAdminApi = {
  async createCourse(data: TrainingCourseEditModel) {
    const response = await api.trainingCourseAdmin.trainingCourseAdminCreateCourse(data)
    return response.data
  },
  async updateCourse(courseId: number, data: TrainingCourseEditModel) {
    await api.trainingCourseAdmin.trainingCourseAdminUpdateCourse(courseId, data)
  },
  async publishCourse(courseId: number) {
    await api.trainingCourseAdmin.trainingCourseAdminPublish(courseId)
  },
  async archiveCourse(courseId: number) {
    await api.trainingCourseAdmin.trainingCourseAdminArchive(courseId)
  },
  async moveCourseToDraft(courseId: number) {
    await api.trainingCourseAdmin.trainingCourseAdminMoveToDraft(courseId)
  },
  async deleteCourse(courseId: number) {
    await api.trainingCourseAdmin.trainingCourseAdminDeleteCourse(courseId)
  },
  async createChapter(courseId: number, data: TrainingCourseChapterEditModel) {
    const response = await api.trainingCourseAdmin.trainingCourseAdminCreateChapter(courseId, data)
    return response.data
  },
  async updateChapter(courseId: number, chapterId: number, data: TrainingCourseChapterEditModel) {
    await api.trainingCourseAdmin.trainingCourseAdminUpdateChapter(courseId, chapterId, data)
  },
  async deleteChapter(courseId: number, chapterId: number) {
    await api.trainingCourseAdmin.trainingCourseAdminDeleteChapter(courseId, chapterId)
  },
  async createChallenge(courseId: number, data: TrainingCourseChallengeCreateModel) {
    await api.trainingCourseAdmin.trainingCourseAdminCreateCourseChallenge(courseId, data)
  },
  async updateChallenge(courseId: number, challengeId: number, data: TrainingCourseChallengeCreateModel) {
    await api.trainingCourseAdmin.trainingCourseAdminUpdateCourseChallenge(courseId, challengeId, data)
  },
  async removeChallenge(courseId: number, challengeId: number) {
    await api.trainingCourseAdmin.trainingCourseAdminRemoveChallenge(courseId, challengeId)
  },
  async createTheoryQuestion(courseId: number, data: TheoryQuestionEditModel) {
    await api.trainingCourseAdmin.trainingCourseAdminCreateTheoryQuestion(courseId, data)
  },
  async updateTheoryQuestion(courseId: number, questionId: number, data: TheoryQuestionEditModel) {
    await api.trainingCourseAdmin.trainingCourseAdminUpdateTheoryQuestion(courseId, questionId, data)
  },
  async deleteTheoryQuestion(courseId: number, questionId: number) {
    await api.trainingCourseAdmin.trainingCourseAdminDeleteTheoryQuestion(courseId, questionId)
  },
  async saveTheoryPaper(courseId: number, chapterId: number, data: TrainingCourseChapterTheoryPaperEditModel) {
    const response = await api.trainingCourseAdmin.trainingCourseAdminSaveChapterTheoryPaper(courseId, chapterId, data)
    return response.data
  },
  async createResource(courseId: number, data: TrainingCourseResourceEditModel) {
    await api.trainingCourseAdmin.trainingCourseAdminCreateResource(courseId, data)
  },
  async updateResource(courseId: number, resourceId: number, data: TrainingCourseResourceEditModel) {
    await api.trainingCourseAdmin.trainingCourseAdminUpdateResource(courseId, resourceId, data)
  },
  async deleteResource(courseId: number, resourceId: number) {
    await api.trainingCourseAdmin.trainingCourseAdminDeleteResource(courseId, resourceId)
  },
  async findTeacherCandidates(courseId: number, keyword: string | null) {
    const response = await api.trainingCourseAdmin.trainingCourseAdminTeacherCandidates(courseId, { keyword })
    return response.data ?? []
  },
  async addTeacher(courseId: number, teacherId: string) {
    await api.trainingCourseAdmin.trainingCourseAdminAddTeacher(courseId, {
      teacherId,
      role: TrainingCourseTeacherRole.Teacher,
    })
  },
  async removeTeacher(courseId: number, teacherId: string) {
    await api.trainingCourseAdmin.trainingCourseAdminRemoveTeacher(courseId, teacherId)
  },
  async reviewEnrollment(courseId: number, userId: string, status: TrainingCourseEnrollmentStatus) {
    await api.trainingCourseAdmin.trainingCourseAdminReviewEnrollment(courseId, userId, { status })
  },
  async findStudentCandidates(courseId: number, keyword: string | null) {
    const response = await api.trainingCourseAdmin.trainingCourseAdminStudentCandidates(courseId, { keyword })
    return response.data ?? []
  },
  async addEnrollment(courseId: number, userId: string) {
    await api.trainingCourseAdmin.trainingCourseAdminAddEnrollment(courseId, { userId })
  },
  async studentLearningDetail(courseId: number, userId: string) {
    const response = await api.trainingCourseAdmin.trainingCourseAdminStudentLearningDetail(courseId, userId)
    return response.data
  },
}
