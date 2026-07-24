import { KeyRound } from 'lucide-react'
import { useState } from 'react'
import { useNavigate, useSearchParams } from 'react-router'
import { useConfig } from '@Hooks/useConfig'
import { PasswordField } from '../../shared/FormControls'
import { useVNextPageTitle } from '../../shared/useVNextPageTitle'
import { AuthForm, AuthSubmitButton, AuthTextLink } from './AuthForm'
import { AuthMessage, AuthPanel } from './AuthShell'
import { authApi } from './api/authApi'
import {
  decodeEmailParameter,
  maskEmail,
  normalizeEncodedParameter,
  passwordResetValidation,
} from './authDomain'
import { useAuthAction } from './useAuthController'

export function ResetPage() {
  useVNextPageTitle('重置密码')
  const [searchParams] = useSearchParams()
  const navigate = useNavigate()
  const { config } = useConfig()
  const action = useAuthAction()
  const [password, setPassword] = useState('')
  const [confirmation, setConfirmation] = useState('')
  const token = normalizeEncodedParameter(searchParams.get('token'))
  const emailToken = normalizeEncodedParameter(searchParams.get('email'))
  const email = decodeEmailParameter(emailToken)

  const submit = async () => {
    if (!token || !emailToken || !email) {
      action.setError('重置链接缺少必要参数或已经损坏。')
      return
    }
    const validation = passwordResetValidation(password, confirmation)
    if (validation) {
      action.setError(validation)
      return
    }

    const result = await action.run(async () => {
      await authApi.resetPassword(emailToken, token, password, config.apiPublicKey)
      return true
    })
    if (result) navigate('/account/login?reset=success', { replace: true })
  }

  if (!token || !emailToken || !email) {
    return (
      <AuthPanel
        description="该密码重置链接缺少必要参数、格式不正确或已被邮件客户端截断。"
        eyebrow="INVALID RESET LINK"
        footer={<AuthTextLink to="/account/recovery">重新申请找回</AuthTextLink>}
        title="链接不可用"
      >
        <AuthMessage>请使用最新一封重置邮件中的完整链接。</AuthMessage>
      </AuthPanel>
    )
  }

  return (
    <AuthPanel
      description={`正在为 ${maskEmail(email) ?? '当前账户'} 设置新密码。`}
      eyebrow="RESET PASSWORD"
      footer={<AuthTextLink to="/account/login">返回登录</AuthTextLink>}
      title="设置新密码"
    >
      {action.error ? <AuthMessage>{action.error}</AuthMessage> : null}
      <AuthForm onSubmit={submit}>
        <PasswordField
          autoComplete="new-password"
          autoFocus
          disabled={action.pending}
          hint="至少 6 个字符"
          label="新密码"
          onValueChange={setPassword}
          required
          value={password}
        />
        <PasswordField
          autoComplete="new-password"
          disabled={action.pending}
          label="确认新密码"
          onValueChange={setConfirmation}
          required
          value={confirmation}
        />
        <AuthSubmitButton pending={action.pending}>
          <KeyRound aria-hidden="true" size={17} />
          更新密码
        </AuthSubmitButton>
      </AuthForm>
    </AuthPanel>
  )
}
