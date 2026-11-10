import api, { ApiTokenCreateModel, ProfileUpdateModel } from '@Api'

export function useApiTokens() {
  return api.apiTokens.useApiTokensList({ revalidateOnFocus: false })
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
}
