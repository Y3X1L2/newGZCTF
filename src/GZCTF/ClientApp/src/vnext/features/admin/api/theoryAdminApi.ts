import api, {
  TheoryPaperEditModel,
  TheoryQuestionEditModel,
  TheoryResultsModel,
} from '@Api'

export interface TheoryQuestionQuery {
  keyword?: string
  count?: number
  skip?: number
}

export const theoryAdminKeys = {
  questions: (query: TheoryQuestionQuery = {}) => [
    'vnext:admin:theory-questions',
    query.keyword ?? '',
    query.count ?? 5000,
    query.skip ?? 0,
  ] as const,
  paper: (gameId: number) => ['vnext:admin:theory-paper', gameId] as const,
  results: (gameId: number) => ['vnext:admin:theory-results', gameId] as const,
}

function normalizedResults(results: TheoryResultsModel): TheoryResultsModel {
  return {
    submissions: results.submissions ?? [],
    scoreboard: results.scoreboard ?? [],
  }
}

export const theoryAdminApi = {
  async listQuestions(query: TheoryQuestionQuery = {}) {
    const response = await api.theoryAdmin.theoryAdminGetQuestions({
      keyword: query.keyword?.trim() || undefined,
      count: Math.min(5000, Math.max(1, query.count ?? 5000)),
      skip: Math.max(0, query.skip ?? 0),
    })
    return Array.isArray(response.data) ? response.data : []
  },

  async createQuestion(payload: TheoryQuestionEditModel) {
    return (await api.theoryAdmin.theoryAdminCreateQuestion(payload)).data
  },

  async updateQuestion(questionId: number, payload: TheoryQuestionEditModel) {
    return (await api.theoryAdmin.theoryAdminUpdateQuestion(questionId, payload)).data
  },

  async removeQuestion(questionId: number) {
    await api.theoryAdmin.theoryAdminDeleteQuestion(questionId)
  },

  async getPaper(gameId: number) {
    return (await api.theoryAdmin.theoryAdminGetPaper(gameId)).data
  },

  async savePaper(gameId: number, payload: TheoryPaperEditModel) {
    return (await api.theoryAdmin.theoryAdminSavePaper(gameId, payload)).data
  },

  async publishPaper(gameId: number) {
    return (await api.theoryAdmin.theoryAdminPublishPaper(gameId)).data
  },

  async getResults(gameId: number) {
    return normalizedResults((await api.theoryAdmin.theoryAdminGetResults(gameId)).data)
  },

  async recalculateResults(gameId: number) {
    return normalizedResults((await api.theoryAdmin.theoryAdminRecalculateResults(gameId)).data)
  },
}
