import { BadgeCheck } from 'lucide-react'
import { useEffect } from 'react'
import { useNavigate, useSearchParams } from 'react-router'
import { useVNextPageTitle } from '../../shared/useVNextPageTitle'
import { useCurrentAccount } from '../account/useCurrentAccount'
import { AuthForm, AuthSubmitButton, AuthTextLink } from './AuthForm'
import { AuthMessage, AuthPanel } from './AuthShell'
import { authApi } from './api/authApi'
import { decodeEmailParameter, maskEmail, normalizeEncodedParameter, safeReturnUrl } from './authDomain'
import { useAuthAction } from './useAuthController'

export function VerifyPage({ mode = 'account' }: { mode?: 'account' | 'email-change' }) {
  const title = mode === 'account' ? '验证邮箱' : '确认新邮箱'
  useVNextPageTitle(title)
  const [searchParams] = useSearchParams()
  const navigate = useNavigate()
  const account = useCurrentAccount()
  const action = useAuthAction()
  const token = normalizeEncodedParameter(searchParams.get('token'))
  const emailToken = normalizeEncodedParameter(searchParams.get('email'))
  const email = decodeEmailParameter(emailToken)
  const returnUrl = safeReturnUrl(searchParams.get('returnUrl'), mode === 'account' ? '/' : '/settings/profile')

  useEffect(() => {
    if (mode !== 'email-change' || account.user || !account.error) return
    const currentUrl = `${window.location.pathname}${window.location.search}`
    navigate(`/account/login?returnUrl=${encodeURIComponent(currentUrl)}`, { replace: true })
  }, [account.error, account.user, mode, navigate])

  const submit = async () => {
    if (!token || !emailToken || !email) {
      action.setError('验证链接缺少必要参数或已经损坏。')
      return
    }

    const result = await action.run(async () => {
      if (mode === 'account') await authApi.verify(emailToken, token)
      else await authApi.confirmEmailChange(emailToken, token)
      await account.mutate()
      return true
    })
    if (result) navigate(returnUrl, { replace: true })
  }

  if (!token || !emailToken || !email) {
    return (
      <AuthPanel
        description="验证链接缺少必要参数、格式不正确或已被邮件客户端截断。"
        eyebrow="INVALID VERIFICATION LINK"
        footer={<AuthTextLink to="/account/login">返回登录</AuthTextLink>}
        title="链接不可用"
      >
        <AuthMessage>请使用最新一封邮件中的完整链接。</AuthMessage>
      </AuthPanel>
    )
  }

  return (
    <AuthPanel
      description={`${mode === 'account' ? '完成账户邮箱验证' : '确认将账户邮箱修改为'} ${maskEmail(email) ?? '目标邮箱'}。`}
      eyebrow={mode === 'account' ? 'VERIFY ACCOUNT' : 'CONFIRM EMAIL'}
      footer={<AuthTextLink to="/account/login">返回登录</AuthTextLink>}
      title={title}
    >
      {action.error ? <AuthMessage>{action.error}</AuthMessage> : null}
      <AuthForm onSubmit={submit}>
        <AuthSubmitButton pending={action.pending}>
          <BadgeCheck aria-hidden="true" size={17} />
          {mode === 'account' ? '验证并登录' : '确认邮箱变更'}
        </AuthSubmitButton>
      </AuthForm>
    </AuthPanel>
  )
}
