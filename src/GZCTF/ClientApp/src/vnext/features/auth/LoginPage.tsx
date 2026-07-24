import { ExternalLink, KeyRound } from 'lucide-react'
import { useEffect, useRef, useState } from 'react'
import { useNavigate, useSearchParams } from 'react-router'
import { useConfig } from '@Hooks/useConfig'
import { PasswordField, TextField } from '../../shared/FormControls'
import { useVNextPageTitle } from '../../shared/useVNextPageTitle'
import { useCurrentAccount } from '../account/useCurrentAccount'
import { AuthDivider, AuthForm, AuthSecondaryButton, AuthSubmitButton, AuthTextLink } from './AuthForm'
import { AuthMessage, AuthPanel } from './AuthShell'
import { authApi } from './api/authApi'
import { CaptchaField, CaptchaHandle } from './CaptchaField'
import { loginValidation, safeReturnUrl } from './authDomain'
import { useAuthAction, useAuthCapabilities } from './useAuthController'

const legacyCapabilities = {
  allowPasswordLogin: true,
  allowRegister: true,
  passwordRecoveryAvailable: true,
  emailConfirmationRequired: false,
  portalSso: { enabled: false, entryUrl: null },
}

export function LoginPage() {
  useVNextPageTitle('登录')
  const [searchParams] = useSearchParams()
  const navigate = useNavigate()
  const { config } = useConfig()
  const account = useCurrentAccount()
  const capabilityQuery = useAuthCapabilities()
  const capabilities = capabilityQuery.capabilities ?? legacyCapabilities
  const action = useAuthAction()
  const captchaRef = useRef<CaptchaHandle>(null)
  const [userName, setUserName] = useState(searchParams.get('userName') ?? '')
  const [password, setPassword] = useState('')
  const returnUrl = safeReturnUrl(searchParams.get('returnUrl'))

  useEffect(() => {
    if (account.user) navigate(returnUrl, { replace: true })
  }, [account.user, navigate, returnUrl])

  const submit = async () => {
    const validation = loginValidation(userName, password)
    if (validation) {
      action.setError(validation)
      return
    }

    const captcha = await captchaRef.current?.getToken()
    if (!captcha?.valid) {
      action.setError('安全验证尚未完成，请稍后再试。')
      return
    }

    const result = await action.run(async () => {
      await authApi.login(userName, password, captcha.token, config.apiPublicKey)
      const current = await account.mutate()
      if (!current) throw new Error('登录成功但会话尚未建立，请重试。')
      return current
    })

    if (result) navigate(returnUrl, { replace: true })
    else {
      setPassword('')
      await captchaRef.current?.reset()
    }
  }

  const portalEntry = capabilities.portalSso.enabled ? capabilities.portalSso.entryUrl : null

  return (
    <AuthPanel
      description="使用平台账户登录，或从统一身份认证门户进入。"
      eyebrow="ACCOUNT ACCESS"
      footer={
        capabilities.allowRegister ? (
          <span>
            还没有平台账户？ <AuthTextLink to={`/account/register?returnUrl=${encodeURIComponent(returnUrl)}`}>创建账户</AuthTextLink>
          </span>
        ) : null
      }
      title="登录平台"
    >
      {searchParams.get('reset') === 'success' ? <AuthMessage tone="success">密码已更新，请使用新密码登录。</AuthMessage> : null}
      {action.error ? <AuthMessage>{action.error}</AuthMessage> : null}
      {capabilityQuery.error ? <AuthMessage tone="info">账户能力接口尚未同步，当前使用兼容登录模式。</AuthMessage> : null}

      {capabilities.allowPasswordLogin ? (
        <AuthForm onSubmit={submit}>
          <TextField
            autoComplete="username"
            autoFocus
            disabled={action.pending}
            label="用户名或邮箱"
            onValueChange={setUserName}
            placeholder="ctfer"
            required
            value={userName}
          />
          <PasswordField
            autoComplete="current-password"
            disabled={action.pending}
            label="密码"
            onValueChange={setPassword}
            placeholder="输入账户密码"
            required
            value={password}
          />
          <CaptchaField action="login" disabled={action.pending} ref={captchaRef} />
          {capabilities.passwordRecoveryAvailable ? (
            <AuthTextLink to="/account/recovery">忘记密码</AuthTextLink>
          ) : null}
          <AuthSubmitButton pending={action.pending}>
            <KeyRound aria-hidden="true" size={17} />
            登录
          </AuthSubmitButton>
        </AuthForm>
      ) : null}

      {portalEntry ? (
        <>
          {capabilities.allowPasswordLogin ? <AuthDivider>统一身份</AuthDivider> : null}
          <AuthSecondaryButton onClick={() => window.location.assign(portalEntry)}>
            <ExternalLink aria-hidden="true" size={17} />
            统一身份认证
          </AuthSecondaryButton>
        </>
      ) : null}
    </AuthPanel>
  )
}
