import api, { ApiTokenCreateModel, ProfileUpdateModel } from '@Api'
import { runtimeJsonClient } from '../admin/api/runtimeJsonClient'

export function useApiTokens() {
  return api.apiTokens.useApiTokensList({ revalidateOnFocus: false })
}

export interface ControlScopeOption {
  id: string
  key: string
  displayName: string
}

function parseControlScopeOption(value: unknown, label: string): ControlScopeOption {
  if (!value || typeof value !== 'object') throw new Error(`${label}: not an object`)
  const record = value as Record<string, unknown>
  if (typeof record.id !== 'string' || !record.id) throw new Error(`${label}: missing id`)
  return {
    id: record.id,
    key: typeof record.key === 'string' ? record.key : '',
    displayName: typeof record.displayName === 'string' ? record.displayName : '',
  }
}

export const settingsApi = {
  async updateProfile(data: ProfileUpdateModel) {
    await api.account.accountUpdate(data)
  },
  async uploadAvatar(file: File) {
    await api.account.accountAvatar({ file })
  },
  async changeEmail(newMail: string) {
    const response = await api.account.accountChangeEmail({ newMail })
    return Boolean(response.data.data)
  },
  async changePassword(oldPassword: string, newPassword: string) {
    await api.account.accountChangePassword({ old: oldPassword, new: newPassword })
  },
  async issueToken(data: ApiTokenCreateModel) {
    const response = await api.apiTokens.apiTokensIssue(data)
    return response.data.plainTextToken ?? ''
  },
  async revokeToken(tokenId: string) {
    await api.apiTokens.apiTokensRevoke(tokenId)
  },
  async listControlScopes(): Promise<ControlScopeOption[]> {
    const payload = await runtimeJsonClient.get('/api/admin/teamlab/scopes')
    if (!Array.isArray(payload)) throw new Error('Control scope list: not an array')
    return payload.map((item, index) => parseControlScopeOption(item, `Control scope ${index}`))
  },
}
