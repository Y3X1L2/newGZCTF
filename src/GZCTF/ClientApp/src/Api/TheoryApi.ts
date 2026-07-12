import api, { ContentType } from '@Api'

export enum TheoryQuestionType {
  SingleChoice = 'SingleChoice',
  MultipleChoice = 'MultipleChoice',
  TrueFalse = 'TrueFalse',
}

export enum TheoryAnswerSheetStatus {
  Draft = 'Draft',
  Submitted = 'Submitted',
}

export interface TheoryQuestionEditModel {
  type: TheoryQuestionType
  bankName?: string
  title: string
  content: string
  options: string[]
  answerIndexes: number[]
  tags?: string[]
}

export interface TheoryQuestionBankItemModel extends TheoryQuestionEditModel {
  id: number
  createdAt: number
  updatedAt: number
}

export interface TheoryPaperQuestionEditModel extends TheoryQuestionEditModel {
  id?: number
  sourceQuestionId?: number | null
  score: number
  order: number
}

export interface TheoryPaperEditModel {
  title: string
  description: string
  questions: TheoryPaperQuestionEditModel[]
}

export interface TheoryPaperDetailModel extends TheoryPaperEditModel {
  id: number
  gameId: number
  isPublished: boolean
  publishedAt?: number | null
  updatedAt?: number
  totalScore: number
}

export interface TheoryAnswerModel {
  paperQuestionId: number
  selectedIndexes: number[]
}

export interface TheoryAnswerSheetEditModel {
  answers: TheoryAnswerModel[]
}

export interface TheoryPlayerQuestionModel {
  id: number
  type: TheoryQuestionType
  title: string
  content: string
  options: string[]
  score: number
  order: number
}

export interface TheoryPlayerPaperModel {
  paperId: number
  gameId: number
  title: string
  description: string
  totalScore: number
  status?: TheoryAnswerSheetStatus | null
  score?: number | null
  submittedAt?: number | null
  updatedAt?: number | null
  questions: TheoryPlayerQuestionModel[]
  answers: TheoryAnswerModel[]
}

export interface TheoryAnswerSheetSummaryModel {
  id: number
  participationId: number
  teamId: number
  teamName: string
  userId: string
  userName: string
  status: TheoryAnswerSheetStatus
  score: number
  maxScore: number
  updatedAt: number
  submittedAt?: number | null
}

export interface TheoryScoreboardItemModel {
  rank: number
  teamId: number
  teamName: string
  divisionId?: number | null
  score: number
  maxScore: number
  userName?: string | null
  submittedAt?: number | null
}

export interface TheoryResultsModel {
  submissions: TheoryAnswerSheetSummaryModel[]
  scoreboard: TheoryScoreboardItemModel[]
}

const request = api.request

export const theoryAdminApi = {
  getQuestions: (keyword?: string, count = 1000, tag?: string[]) =>
    request<TheoryQuestionBankItemModel[], unknown>({
      path: '/api/admin/theory/questions',
      method: 'GET',
      query: { keyword, count, tag },
    }),

  createQuestion: (data: TheoryQuestionEditModel) =>
    request<TheoryQuestionBankItemModel, unknown>({
      path: '/api/admin/theory/questions',
      method: 'POST',
      body: data,
      type: ContentType.Json,
    }),

  updateQuestion: (id: number, data: TheoryQuestionEditModel) =>
    request<TheoryQuestionBankItemModel, unknown>({
      path: `/api/admin/theory/questions/${id}`,
      method: 'PUT',
      body: data,
      type: ContentType.Json,
    }),

  deleteQuestion: (id: number) =>
    request<void, unknown>({
      path: `/api/admin/theory/questions/${id}`,
      method: 'DELETE',
    }),

  getPaper: (gameId: number) =>
    request<TheoryPaperDetailModel, unknown>({
      path: `/api/admin/theory/games/${gameId}/paper`,
      method: 'GET',
    }),

  savePaper: (gameId: number, data: TheoryPaperEditModel) =>
    request<TheoryPaperDetailModel, unknown>({
      path: `/api/admin/theory/games/${gameId}/paper`,
      method: 'PUT',
      body: data,
      type: ContentType.Json,
    }),

  publishPaper: (gameId: number) =>
    request<TheoryPaperDetailModel, unknown>({
      path: `/api/admin/theory/games/${gameId}/paper/publish`,
      method: 'POST',
    }),

  getResults: (gameId: number) =>
    request<TheoryResultsModel, unknown>({
      path: `/api/admin/theory/games/${gameId}/results`,
      method: 'GET',
    }),

  recalculateResults: (gameId: number) =>
    request<TheoryResultsModel, unknown>({
      path: `/api/admin/theory/games/${gameId}/results/recalculate`,
      method: 'POST',
    }),
}

export const theoryPlayerApi = {
  getPaper: (gameId: number) =>
    request<TheoryPlayerPaperModel, unknown>({
      path: `/api/theory/games/${gameId}/paper`,
      method: 'GET',
    }),

  saveDraft: (gameId: number, data: TheoryAnswerSheetEditModel) =>
    request<TheoryPlayerPaperModel, unknown>({
      path: `/api/theory/games/${gameId}/draft`,
      method: 'PUT',
      body: data,
      type: ContentType.Json,
    }),

  submit: (gameId: number, data: TheoryAnswerSheetEditModel) =>
    request<TheoryPlayerPaperModel, unknown>({
      path: `/api/theory/games/${gameId}/submit`,
      method: 'POST',
      body: data,
      type: ContentType.Json,
    }),

  getScoreboard: (gameId: number) =>
    request<TheoryScoreboardItemModel[], unknown>({
      path: `/api/theory/games/${gameId}/scoreboard`,
      method: 'GET',
    }),
}
