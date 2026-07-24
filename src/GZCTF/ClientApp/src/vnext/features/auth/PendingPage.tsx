import { Clock3, LogOut } from 'lucide-react'
import { useLocation, useNavigate, useSearchParams } from 'react-router'
import { useVNextPageTitle } from '../../shared/useVNextPageTitle'
import { useAccountLogout, useCurrentAccount } from '../account/useCurrentAccount'
import { AuthSecondaryButton, AuthTextLink } from './AuthForm'
import { AuthMessage, AuthPanel } from './AuthShell'
import { pendingReason, safeReturnUrl } from './authDomain'

const pendingCopy = {
  approval: {
    eyebrow: 'APPROVAL PENDING',
    title: '等待管理员审核',
    description: '账户申请已经提交。审核通过后，可使用注册时的用户名和密码登录。',
  },
  'email-verification': {
    eyebrow: 'EMAIL VERIFICATION',
    title: '检查验证邮件',
    description: '验证邮件已经发出。完成邮箱验证后即可进入平台。',
  },
  unknown: {
    eyebrow: 'ACCOUNT PENDING',
    title: '账户状态待确认',
    description: '当前账户尚未完成认证，请返回登录页重新检查状态。',
  },
}

export function PendingPage() {
  useVNextPageTitle('账户待确认')
  const [searchParams] = useSearchParams()
  const location = useLocation()
  const navigate = useNavigate()
  const account = useCurrentAccount()
  const logout = useAccountLogout()
  const reason = pendingReason(searchParams.get('reason'))
  const copy = pendingCopy[reason]
  const returnUrl = safeReturnUrl(searchParams.get('returnUrl'))
  const state = location.state as { userName?: string } | null
  const loginUrl = `/account/login?returnUrl=${encodeURIComponent(returnUrl)}${
    state?.userName ? `&userName=${encodeURIComponent(state.userName)}` : ''
  }`

  return (
    <AuthPanel
      description={copy.description}
      eyebrow={copy.eyebrow}
      footer={<AuthTextLink to="/">返回平台首页</AuthTextLink>}
      title={copy.title}
    >
      <AuthMessage tone="info">
        <Clock3 aria-hidden="true" size={16} /> 当前页面不会持续轮询账户或保存注册密码。
      </AuthMessage>
      <AuthSecondaryButton onClick={() => navigate(loginUrl, { replace: true })}>重新登录检查</AuthSecondaryButton>
      {account.isAuthenticated ? (
        <AuthSecondaryButton onClick={() => void logout({ redirectTo: '/account/login' })}>
          <LogOut aria-hidden="true" size={17} />
          退出当前账户
        </AuthSecondaryButton>
      ) : null}
    </AuthPanel>
  )
}
