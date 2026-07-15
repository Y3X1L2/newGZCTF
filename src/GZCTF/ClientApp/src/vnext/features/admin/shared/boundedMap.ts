export async function boundedMap<T, R>(items: readonly T[], limit: number, worker: (item: T, index: number) => Promise<R>) {
  if (!Number.isInteger(limit) || limit < 1) throw new Error('Concurrency limit must be a positive integer.')
  if (items.length === 0) return []

  const results = new Array<R>(items.length)
  let nextIndex = 0

  async function run() {
    while (nextIndex < items.length) {
      const index = nextIndex
      nextIndex += 1
      results[index] = await worker(items[index], index)
    }
  }

  await Promise.all(Array.from({ length: Math.min(limit, items.length) }, () => run()))
  return results
}
