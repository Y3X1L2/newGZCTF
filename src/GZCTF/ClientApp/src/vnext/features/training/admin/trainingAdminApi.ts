import api, { TrainingCourseChapterEditModel, TrainingCourseEditModel } from '@Api'

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
}
