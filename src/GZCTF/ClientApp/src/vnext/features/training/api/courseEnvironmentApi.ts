import api, { OSType, TrainingCourseImageTemplateModel } from '@Api'

export interface CourseRegistrySummary {
  enabled?: boolean
  address?: string
  namespace?: string
}

async function responseError(response: Response, fallback: string) {
  const payload = await response.json().catch(() => null)
  if (payload && typeof payload === 'object') {
    const data = payload as Record<string, unknown>
    if (typeof data.message === 'string') return data.message
    if (typeof data.title === 'string') return data.title
  }
  return fallback
}

export const courseEnvironmentApi = {
  async registry(courseId: number) {
    const response = await fetch(`/api/admin/training/courses/${courseId}/image-templates/docker-registry`, {
      credentials: 'include',
    })
    if (!response.ok) throw new Error(await responseError(response, 'Registry 状态读取失败。'))
    return (await response.json()) as CourseRegistrySummary
  },

  async registerDocker(courseId: number, data: { name: string; registryUrl: string; registryAuth: string | null }) {
    await api.trainingCourseAdmin.trainingCourseAdminRegisterDockerTemplate(courseId, {
      ...data,
      osType: OSType.Linux,
    })
  },

  async uploadDocker(courseId: number, data: { file: File; name: string; sourceImage: string }) {
    const form = new FormData()
    form.append('file', data.file)
    form.append('name', data.name)
    form.append('sourceImage', data.sourceImage)
    form.append('osType', String(OSType.Linux))
    const response = await fetch(`/api/admin/training/courses/${courseId}/image-templates/upload-docker`, {
      method: 'POST',
      body: form,
      credentials: 'include',
    })
    if (!response.ok) throw new Error(await responseError(response, 'Docker 镜像包上传失败。'))
  },

  async uploadVm(courseId: number, file: File, archive: boolean) {
    if (archive) {
      await api.trainingCourseAdmin.trainingCourseAdminUploadVmArchiveTemplate(courseId, { file })
      return
    }
    await api.trainingCourseAdmin.trainingCourseAdminUploadVmTemplate(courseId, { file })
  },

  async importLocal(courseId: number, data: { localPath: string; displayName: string | null }) {
    await api.trainingCourseAdmin.trainingCourseAdminImportLocalTemplate(courseId, data)
  },

  async detach(courseId: number, template: TrainingCourseImageTemplateModel) {
    if (!template.id) throw new Error('环境模板缺少有效标识。')
    await api.trainingCourseAdmin.trainingCourseAdminDetachImageTemplate(courseId, template.id)
  },
}
