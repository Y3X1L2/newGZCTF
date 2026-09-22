import { LeagueEndReason, LeagueMatchState } from '@Api'
import type { LeagueMatchSummary } from '@Api'

export function matchStateLabel(match: LeagueMatchSummary) {
  if (match.state === LeagueMatchState.Ended)
    return match.result?.reason === LeagueEndReason.Aborted ? '已中止' : '已结束'
  return ['报名与配置', '准备中', '待开赛', '开赛处理中', '进行中'][match.state ?? -1] ?? '状态待确认'
}

export function leagueError(error: unknown) {
  const response = (
    error as {
      response?: { status?: number; data?: { code?: string; detail?: string; errors?: Record<string, string[]> } }
    }
  )?.response
  if (response?.data?.code === 'league_disabled') return '联赛尚未开放，请联系管理员。'
  if (response?.data?.code === 'league_revision_conflict')
    return '配置已被其他操作更新。你的输入已保留，请核对最新配置后再保存。'
  if (response?.status === 401) return '登录已失效，请重新登录。'
  if (response?.status === 403) return response.data?.detail || '当前账号无权执行此操作。'
  if (response?.status === 404) return '场次或所选资源不存在。'
  if (response?.data?.detail) return response.data.detail
  if (response?.data?.errors) return Object.values(response.data.errors).flat().join('；')
  return '请求未能完成。写操作请先刷新核对结果，再决定是否重试。'
}

export const registrationLabels = ['待审核', '已通过', '已拒绝']
