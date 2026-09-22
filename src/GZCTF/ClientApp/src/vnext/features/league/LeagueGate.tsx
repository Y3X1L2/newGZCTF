import type { ReactNode } from 'react'
import { Link } from 'react-router'
import { ActionButton } from '../../shared/Interaction'
import { DataState } from '../../shared/Primitives'
import { leagueEnabled } from './leagueFeature'
import { leagueError } from './leaguePresentation'
import type { useLeagueIdentity } from './useLeagueController'

export function LeagueGate({
  account,
  admin = false,
  children,
}: {
  account: ReturnType<typeof useLeagueIdentity>
  admin?: boolean
  children: ReactNode
}) {
  if (!leagueEnabled) return <DataState title="联赛尚未开放" description="此模块正在准备中，请稍后再来。" />
  if (!account.isAuthenticated) {
    if (!account.error) return <DataState loading title="正在确认登录状态" />
    const status = (account.error as { response?: { status?: number } }).response?.status
    if (status === 401)
      return (
        <>
          <DataState title="请先登录" description="登录后可查看联赛、报名及审核结果。" />
          <Link to="/account/login">登录平台</Link>
        </>
      )
    return (
      <>
        <DataState title="账户状态读取失败" description={leagueError(account.error)} />
        <ActionButton onClick={() => void account.mutate()}>重试</ActionButton>
      </>
    )
  }
  if (admin && !account.isAdmin)
    return <DataState title="需要管理员权限" description="请返回联赛列表查看可参与的场次。" />
  return <>{children}</>
}
