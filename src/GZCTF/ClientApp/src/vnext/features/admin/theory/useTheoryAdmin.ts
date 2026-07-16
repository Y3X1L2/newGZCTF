import useSWR from 'swr'
import { theoryAdminApi, theoryAdminKeys } from '../api'

function validGameId(gameId: number) {
  return Number.isInteger(gameId) && gameId > 0
}

export function useTheoryQuestions() {
  const result = useSWR(
    theoryAdminKeys.questions({ count: 5000 }),
    () => theoryAdminApi.listQuestions({ count: 5000 }),
    { revalidateOnFocus: false }
  )
  return {
    questions: result.data,
    error: result.error,
    isLoading: result.isLoading,
    isRefreshing: result.isValidating,
    mutate: result.mutate,
  }
}

export function useTheoryPaper(gameId: number) {
  const result = useSWR(
    validGameId(gameId) ? theoryAdminKeys.paper(gameId) : null,
    () => theoryAdminApi.getPaper(gameId),
    { revalidateOnFocus: false }
  )
  return {
    paper: result.data,
    error: result.error,
    isLoading: result.isLoading,
    isRefreshing: result.isValidating,
    mutate: result.mutate,
  }
}

export function useTheoryResults(gameId: number) {
  const result = useSWR(
    validGameId(gameId) ? theoryAdminKeys.results(gameId) : null,
    () => theoryAdminApi.getResults(gameId),
    { revalidateOnFocus: false }
  )
  return {
    results: result.data,
    error: result.error,
    isLoading: result.isLoading,
    isRefreshing: result.isValidating,
    mutate: result.mutate,
  }
}
