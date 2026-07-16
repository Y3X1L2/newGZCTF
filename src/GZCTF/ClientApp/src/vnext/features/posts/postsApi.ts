import api from '@Api'

const swrOptions = { revalidateOnFocus: false } as const

export function usePosts() {
  return api.info.useInfoGetPosts(swrOptions)
}

export function usePost(postId: string, enabled: boolean) {
  return api.info.useInfoGetPost(postId, swrOptions, enabled)
}
