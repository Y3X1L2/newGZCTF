import api, { TheoryAnswerSheetEditModel } from '@Api'

export function useGameTheoryPaper(gameId: number) {
  return api.theoryPlayer.useTheoryPlayerGetPaper(gameId, { revalidateOnFocus: false, shouldRetryOnError: false }, true)
}

export function useTheoryScoreboard(gameId: number, refreshInterval: number) {
  return api.theoryPlayer.useTheoryPlayerScoreboard(
    gameId,
    { revalidateOnFocus: false, refreshInterval, shouldRetryOnError: false },
    true
  )
}

export const theoryPlayerApi = {
  async saveDraft(gameId: number, data: TheoryAnswerSheetEditModel) {
    const response = await api.theoryPlayer.theoryPlayerSaveDraft(gameId, data)
    return response.data
  },
  async submit(gameId: number, data: TheoryAnswerSheetEditModel) {
    const response = await api.theoryPlayer.theoryPlayerSubmit(gameId, data)
    return response.data
  },
  async refreshScoreboard(gameId: number) {
    await api.theoryPlayer.mutateTheoryPlayerScoreboard(gameId)
  },
}
