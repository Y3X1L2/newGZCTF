export const sessionCacheResetOptions = { revalidate: false, populateCache: true } as const
export const discardCachedValue = () => undefined

export type AccountCacheMutator = (
  data: typeof discardCachedValue,
  options: typeof sessionCacheResetOptions
) => Promise<unknown>

export type GlobalCacheMutator = (
  matcher: (key: unknown) => boolean,
  data: typeof discardCachedValue,
  options: typeof sessionCacheResetOptions
) => Promise<unknown>

export async function clearAccountSessionCache(accountMutate: AccountCacheMutator, globalMutate: GlobalCacheMutator) {
  await accountMutate(discardCachedValue, sessionCacheResetOptions)
  await globalMutate(() => true, discardCachedValue, sessionCacheResetOptions)
}
