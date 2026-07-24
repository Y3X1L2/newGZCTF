import { Mail } from 'lucide-react'
import { useRef, useState } from 'react'
import { TextField } from '../../shared/FormControls'
import { useVNextPageTitle } from '../../shared/useVNextPageTitle'
import { AuthForm, AuthSubmitButton, AuthTextLink } from './AuthForm'
import { AuthMessage, AuthPanel } from './AuthShell'
import { authApi } from './api/authApi'
import { CaptchaField, CaptchaHandle } from './CaptchaField'
import { useAuthAction, useAuthCapabilities } from './useAuthController'

export function RecoveryPage() {
  useVNextPageTitle('找回密码')
  const capabilityQuery = useAuthCapabilities()
  const action = useAuthAction()
  const captchaRef = useRef<CaptchaHandle>(null)
  const [email, setEmail] = useState('')
  const [submitted, setSubmitted] = useState(false)

  const submit = async () => {
    if (!/^\S+@\S+\.\S+$/.test(email.trim())) {
      action.setError('请输入有效的邮箱地址。')
      return
    }
    const captcha = await captchaRef.current?.getToken()
    if (!captcha?.valid) {
      action.setError('安全验证尚未完成，请稍后再试。')
      return
    }
    const result = await action.run(async () => {
      await authApi.recovery(email, captcha.token)
      return true
    })
    if (result) setSubmitted(true)
    else await captchaRef.current?.reset()
  }

  if (!capabilityQuery.loading && capabilityQuery.capabilities && !capabilityQuery.capabilities.passwordRecoveryAvailable) {
    return (
      <AuthPanel
        description="当前平台未启用邮件找回，请联系管理员重置账户密码。"
        eyebrow="RECOVERY UNAVAILABLE"
        footer={<AuthTextLink to="/account/login">返回登录</AuthTextLink>}
        title="无法自助找回"
      >
        <AuthMessage tone="info">管理员重置后会提供一次性新密码。</AuthMessage>
      </AuthPanel>
    )
  }

  return (
    <AuthPanel
      description="提交邮箱后，系统会在账户可用时发送密码重置邮件。"
      eyebrow="PASSWORD RECOVERY"
      footer={<AuthTextLink to="/account/login">返回登录</AuthTextLink>}
      title="找回密码"
    >
      {submitted ? (
        <AuthMessage tone="success">若该账户存在且可用，密码重置邮件将发送至对应邮箱。</AuthMessage>
      ) : (
        <>
          {action.error ? <AuthMessage>{action.error}</AuthMessage> : null}
          <AuthForm onSubmit={submit}>
            <TextField
              autoComplete="email"
              autoFocus
              disabled={action.pending}
              label="账户邮箱"
              onValueChange={setEmail}
              placeholder="ctf@example.com"
              required
              type="email"
              value={email}
            />
            <CaptchaField action="recovery" disabled={action.pending} ref={captchaRef} />
            <AuthSubmitButton pending={action.pending}>
              <Mail aria-hidden="true" size={17} />
              发送重置邮件
            </AuthSubmitButton>
          </AuthForm>
        </>
      )}
    </AuthPanel>
  )
}
