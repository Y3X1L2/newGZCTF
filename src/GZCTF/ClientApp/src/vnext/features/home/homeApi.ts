import api from '@Api'

const swrOptions = { revalidateOnFocus: false } as const

export function useHomePosts() {
  return api.info.useInfoGetLatestPosts({ ...swrOptions, refreshInterval: 5 * 60 * 1000 })
}

export function useHomeCourses() {
  return api.trainingCourse.useTrainingCourseCourses({
    ...swrOptions,
    refreshInterval: 5 * 60 * 1000,
    shouldRetryOnError: false,
  })
}

export function useHomeTrainingOverview(enabled: boolean) {
  return api.trainingCourse.useTrainingCourseOverview(
    { ...swrOptions, refreshInterval: 5 * 60 * 1000, shouldRetryOnError: false },
    enabled
  )
}
