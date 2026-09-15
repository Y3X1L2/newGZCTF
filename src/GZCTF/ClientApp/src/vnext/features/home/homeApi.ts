import api from '@Api'
import { homePreviewEnabled, previewCourses, previewOverview, previewPosts } from './homePreview'

const swrOptions = { revalidateOnFocus: false } as const

export function useHomePosts() {
  const result = api.info.useInfoGetLatestPosts({ ...swrOptions, refreshInterval: 5 * 60 * 1000 })
  return {
    ...result,
    data: result.data ?? (homePreviewEnabled ? previewPosts : undefined),
    error: result.data || !homePreviewEnabled ? result.error : undefined,
    isLoading: homePreviewEnabled ? false : result.isLoading,
  }
}

export function useHomeCourses() {
  const result = api.trainingCourse.useTrainingCourseCourses({
    ...swrOptions,
    refreshInterval: 5 * 60 * 1000,
    shouldRetryOnError: false,
  })
  return {
    ...result,
    data: result.data ?? (homePreviewEnabled ? previewCourses : undefined),
    error: result.data || !homePreviewEnabled ? result.error : undefined,
    isLoading: homePreviewEnabled ? false : result.isLoading,
  }
}

export function useHomeTrainingOverview(enabled: boolean) {
  const result = api.trainingCourse.useTrainingCourseOverview(
    { ...swrOptions, refreshInterval: 5 * 60 * 1000, shouldRetryOnError: false },
    enabled
  )
  return {
    ...result,
    data: result.data ?? (homePreviewEnabled && enabled ? previewOverview : undefined),
    error: result.data || !homePreviewEnabled || !enabled ? result.error : undefined,
    isLoading: homePreviewEnabled && enabled ? false : result.isLoading,
  }
}
