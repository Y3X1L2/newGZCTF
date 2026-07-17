import api, {
  CaptchaProvider,
  ClientCaptchaInfoModel,
  HashPowChallenge,
  RegisterStatus,
} from '@Api'
import { encryptApiData } from '@Utils/Crypto'

export interface AccountCapabilities {
  allowPasswordLogin: boolean
  allowRegister: boolean
  passwordRecoveryAvailable: boolean
  emailConfirmationRequired: boolean
  portalSso: {
    enabled: boolean
    entryUrl: string | null
  }
}

const defaultCapabilities: AccountCapabilities = {
  allowPasswordLogin: true,
  allowRegister: false,
  passwordRecoveryAvailable: false,
  emailConfirmationRequired: false,
  portalSso: { enabled: false, entryUrl: null },
}

function asRecord(value: unknown): Record<string, unknown> {
  return value && typeof value === 'object' ? (value as Record<string, unknown>) : {}
}

export function normalizeAccountCapabilities(value: unknown): AccountCapabilities {
  const source = asRecord(value)
  const portal = asRecord(source.portalSso)
  const entryUrl = typeof portal.entryUrl === 'string' && portal.entryUrl ? portal.entryUrl : null

  return {
    allowPasswordLogin:
      typeof source.allowPasswordLogin === 'boolean' ? source.allowPasswordLogin : defaultCapabilities.allowPasswordLogin,
    allowRegister: source.allowRegister === true,
    passwordRecoveryAvailable: source.passwordRecoveryAvailable === true,
    emailConfirmationRequired: source.emailConfirmationRequired === true,
    portalSso: {
      enabled: portal.enabled === true && Boolean(entryUrl),
      entryUrl,
    },
  }
}

async function encrypted(value: string, publicKey?: string | null) {
  return encryptApiData((key) => key, value, publicKey)
}

export const authApi = {
  async capabilities() {
    const response = await api.request<unknown>({
      path: '/api/account/capabilities',
      method: 'GET',
      format: 'json',
    })
    return normalizeAccountCapabilities(response.data)
  },
  async captchaInfo(): Promise<ClientCaptchaInfoModel> {
    const response = await api.info.infoGetClientCaptchaInfo()
    return response.data ?? { type: CaptchaProvider.None }
  },
  async powChallenge(): Promise<HashPowChallenge> {
    const response = await api.info.infoPowChallenge()
    return response.data
  },
  async login(userName: string, password: string, challenge: string | null, publicKey?: string | null) {
    await api.account.accountLogIn({
      userName: userName.trim(),
      password: await encrypted(password, publicKey),
      challenge,
    })
  },
  async register(
    userName: string,
    email: string,
    password: string,
    challenge: string | null,
    publicKey?: string | null
  ) {
    const response = await api.account.accountRegister({
      userName: userName.trim(),
      email: email.trim(),
      password: await encrypted(password, publicKey),
      challenge,
    })
    return response.data.data ?? RegisterStatus.AdminConfirmationRequired
  },
  async recovery(email: string, challenge: string | null) {
    await api.account.accountRecovery({ email: email.trim(), challenge })
  },
  async resetPassword(email: string, token: string, password: string, publicKey?: string | null) {
    await api.account.accountPasswordReset({
      email,
      rToken: token,
      password: await encrypted(password, publicKey),
    })
  },
  async verify(email: string, token: string) {
    await api.account.accountVerify({ email, token })
  },
  async confirmEmailChange(email: string, token: string) {
    await api.account.accountMailChangeConfirm({ email, token })
  },
}
