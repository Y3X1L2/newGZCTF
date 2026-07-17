import { UserPlus } from 'lucide-react'
import { useRef, useState } from 'react'
import { useNavigate, useSearchParams } from 'react-router'
import { useConfig } from '@Hooks/useConfig'
import { RegisterStatus } from '@Api'
import { PasswordField, TextField } from '../../shared/FormControls'
import { useVNextPageTitle } from '../../shared/useVNextPageTitle'
import { useCurrentAccount } from '../account/useCurrentAccount'
import { AuthForm, AuthSubmitButton, AuthTextLink } from './AuthForm'
import { AuthMessage, AuthPanel } from './AuthShell'
import { authApi } from './api/authApi'
import { CaptchaField, CaptchaHandle } from './CaptchaField'
import { registrationValidation, safeReturnUrl } from './authDomain'
import { useAuthAction, useAuthCapabilities } from './useAuthController'

export function RegisterPage() {
  useVNextPageTitle('注册')
  const [searchParams] = useSearchParams()
  const navigate = useNavigate()
  const { config } = useConfig()
  const account = useCurrentAccount()
  const capabilityQuery = useAuthCapabilities()
  const action = useAuthAction()
  const captchaRef = useRef<CaptchaHandle>(null)
  const [email, setEmail] = useState('')
  const [userName, setUserName] = useState('')
  const [password, setPassword] = useState('')
  const [confirmation, setConfirmation] = useState('')
  const returnUrl = safeReturnUrl(searchParams.get('returnUrl'))

  const submit = async () => {
    const validation = registrationValidation(userName, email, password, confirmation)
    if (validation) {
      action.setError(validation)
      return
    }

    const captcha = await captchaRef.current?.getToken()
    if (!captcha?.valid) {
      action.setError('安全验证尚未完成，请稍后再试。')
      return
    }

    const status = await action.run(() => authApi.register(userName, email, password, captcha.token, config.apiPublicKey))
    if (!status) {
      await captchaRef.current?.reset()
      return
    }

    if (status === RegisterStatus.LoggedIn) {
      await account.mutate()
      navigate(returnUrl, { replace: true })
      return
    }

    const reason = status === RegisterStatus.EmailConfirmationRequired ? 'email-verification' : 'approval'
    navigate(`/account/pending?reason=${reason}&returnUrl=${encodeURIComponent(returnUrl)}`, {
      replace: true,
      state: { userName },
    })
  }

  if (!capabilityQuery.loading && capabilityQuery.capabilities && !capabilityQuery.capabilities.allowRegister) {
    return (
      <AuthPanel
        description="当前平台未开放自主注册，请通过统一身份门户进入或联系管理员创建账户。"
        eyebrow="REGISTRATION CLOSED"
        footer={<AuthTextLink to="/account/login">返回登录</AuthTextLink>}
        title="暂不开放注册"
      >
        <AuthMessage tone="info">已有账户仍可正常登录。</AuthMessage>
      </AuthPanel>
    )
  }

  return (
    <AuthPanel
      description="注册后将按照平台策略直接登录、等待审核或验证邮箱。"
      eyebrow="CREATE ACCOUNT"
      footer={
        <span>
          已有账户？ <AuthTextLink to={`/account/login?returnUrl=${encodeURIComponent(returnUrl)}`}>返回登录</AuthTextLink>
        </span>
      }
      title="创建平台账户"
    >
      {action.error ? <AuthMessage>{action.error}</AuthMessage> : null}
      <AuthForm onSubmit={submit}>
        <TextField
          autoComplete="email"
          autoFocus
          disabled={action.pending}
          label="邮箱"
          onValueChange={setEmail}
          placeholder="ctf@example.com"
          required
          type="email"
          value={email}
        />
        <TextField
          autoComplete="username"
          disabled={action.pending}
          label="用户名"
          maxLength={15}
          minLength={3}
          onValueChange={setUserName}
          placeholder="ctfer"
          required
          value={userName}
        />
        <PasswordField
          autoComplete="new-password"
          disabled={action.pending}
          hint="至少 6 个字符"
          label="密码"
          onValueChange={setPassword}
          required
          value={password}
        />
        <PasswordField
          autoComplete="new-password"
          disabled={action.pending}
          label="确认密码"
          onValueChange={setConfirmation}
          required
          value={confirmation}
        />
        <CaptchaField action="register" disabled={action.pending} ref={captchaRef} />
        <AuthSubmitButton pending={action.pending}>
          <UserPlus aria-hidden="true" size={17} />
          创建账户
        </AuthSubmitButton>
      </AuthForm>
    </AuthPanel>
  )
}
