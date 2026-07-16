import api, {
  AttachmentCreateModel,
  ChallengeEditDetailModel,
  ChallengeInfoModel,
  ChallengeUpdateModel,
  FlagCreateModel,
  GameInfoModel,
  TaskStatus,
} from '@Api'

export interface AdminGameListQuery {
  page?: number
  pageSize?: number
}

export interface AdminGamePage {
  items: GameInfoModel[]
  total: number
  page: number
  pageSize: number
}

export interface AdminUploadProgress {
  loaded: number
  total: number | null
}

function uploadProgress(onProgress?: (progress: AdminUploadProgress) => void) {
  if (!onProgress) return undefined
  return (event: { loaded: number; total?: number }) =>
    onProgress({ loaded: event.loaded, total: event.total ?? null })
}

function exportFileName(header: unknown, fallback: string) {
  if (typeof header !== 'string') return fallback
  const encoded = header.match(/filename\*=UTF-8''([^;]+)/i)?.[1]
  if (encoded) {
    try {
      return decodeURIComponent(encoded)
    } catch {
      return fallback
    }
  }
  return header.match(/filename="?([^";]+)"?/i)?.[1] ?? fallback
}

export const adminGameKeys = {
  list: (query: AdminGameListQuery = {}) => [
    'vnext:admin:games',
    query.page ?? 1,
    query.pageSize ?? 30,
  ] as const,
  detail: (gameId: number) => ['vnext:admin:game', gameId] as const,
  challenges: (gameId: number) => ['vnext:admin:game-challenges', gameId] as const,
  challenge: (gameId: number, challengeId: number) => [
    'vnext:admin:game-challenge',
    gameId,
    challengeId,
  ] as const,
}

export const gameAdminApi = {
  async list(query: AdminGameListQuery = {}): Promise<AdminGamePage> {
    const page = Math.max(1, query.page ?? 1)
    const pageSize = Math.min(100, Math.max(1, query.pageSize ?? 30))
    const response = await api.edit.editGetGames({ count: pageSize, skip: (page - 1) * pageSize })
    return {
      items: response.data.data,
      total: response.data.total ?? response.data.length,
      page,
      pageSize,
    }
  },

  async detail(gameId: number) {
    return (await api.edit.editGetGame(gameId)).data
  },

  async create(payload: GameInfoModel) {
    return (await api.edit.editAddGame(payload)).data
  },

  async update(gameId: number, payload: GameInfoModel) {
    return (await api.edit.editUpdateGame(gameId, payload)).data
  },

  async remove(gameId: number) {
    await api.edit.editDeleteGame(gameId)
  },

  async uploadPoster(gameId: number, file: File, onProgress?: (progress: AdminUploadProgress) => void) {
    return (
      await api.edit.editUpdateGamePoster(
        gameId,
        { file },
        { onUploadProgress: uploadProgress(onProgress) }
      )
    ).data
  },

  async importGame(file: File, onProgress?: (progress: AdminUploadProgress) => void) {
    return (
      await api.edit.editImportGame(
        { file },
        { onUploadProgress: uploadProgress(onProgress) }
      )
    ).data
  },

  async exportGame(gameId: number, title: string) {
    const response = await api.edit.editExportGame(gameId, { format: 'blob' })
    return {
      blob: response.data as unknown as Blob,
      fileName: exportFileName(response.headers?.['content-disposition'], `${title}-export.zip`),
    }
  },

  async listChallenges(gameId: number) {
    return (await api.edit.editGetGameChallenges(gameId)).data
  },

  async challenge(gameId: number, challengeId: number) {
    return (await api.edit.editGetGameChallenge(gameId, challengeId)).data
  },

  async createChallenge(gameId: number, payload: ChallengeInfoModel) {
    return (await api.edit.editAddGameChallenge(gameId, payload)).data
  },

  async updateChallenge(gameId: number, challengeId: number, payload: ChallengeUpdateModel) {
    return (await api.edit.editUpdateGameChallenge(gameId, challengeId, payload)).data
  },

  async removeChallenge(gameId: number, challengeId: number) {
    await api.edit.editRemoveGameChallenge(gameId, challengeId)
  },

  async flushScoreboard(gameId: number) {
    await api.edit.editFlushScoreboardCache(gameId)
  },

  async uploadAsset(file: File) {
    const files = (await api.assets.assetsUpload({ files: [file] }, { filename: file.name })).data
    const uploaded = files[0]
    if (!uploaded?.hash) throw new Error('附件已上传，但服务器没有返回文件哈希。')
    return uploaded
  },

  async updateAttachment(gameId: number, challengeId: number, payload: AttachmentCreateModel) {
    await api.edit.editUpdateAttachment(gameId, challengeId, payload)
  },

  async addFlags(gameId: number, challengeId: number, payload: FlagCreateModel[]) {
    await api.edit.editAddFlags(gameId, challengeId, payload)
  },

  async updateFlag(gameId: number, challengeId: number, flagId: number, payload: FlagCreateModel) {
    await api.edit.editUpdateFlag(gameId, challengeId, flagId, payload)
  },

  async removeFlag(gameId: number, challengeId: number, flagId: number) {
    return (await api.edit.editRemoveFlag(gameId, challengeId, flagId)).data as TaskStatus
  },

  async createTestInstance(gameId: number, challengeId: number) {
    return (await api.edit.editCreateTestContainer(gameId, challengeId)).data
  },

  async destroyTestInstance(gameId: number, challengeId: number) {
    await api.edit.editDestroyTestContainer(gameId, challengeId)
  },
}

export type AdminGame = GameInfoModel
export type AdminGameChallenge = ChallengeInfoModel
export type AdminGameChallengeDetail = ChallengeEditDetailModel
