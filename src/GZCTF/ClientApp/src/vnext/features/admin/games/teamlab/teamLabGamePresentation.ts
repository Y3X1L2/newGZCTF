import type { AdminStatusTone } from '../../shared/AdminWorkbench'
import type { TeamLabGameRolloutStatus, TeamLabGameTargetStatus } from '../../api/teamlabGameAdminApi'

export const rolloutStatusMeta: Record<TeamLabGameRolloutStatus, { label: string; tone: AdminStatusTone; active: boolean }> = {
  pending: { label: '待准备', tone: 'neutral', active: false },
  preparing: { label: '准备中', tone: 'info', active: true },
  ready: { label: '准备完成', tone: 'success', active: false },
  draining: { label: '结束清理中', tone: 'warning', active: true },
  completed: { label: '已完成', tone: 'neutral', active: false },
  paused: { label: '已暂停', tone: 'neutral', active: false },
  failed: { label: '失败', tone: 'danger', active: false },
}

export const targetStatusMeta: Record<TeamLabGameTargetStatus, { label: string; tone: AdminStatusTone; active: boolean }> = {
  draft: { label: '待部署', tone: 'neutral', active: false },
  deploying: { label: '部署中', tone: 'info', active: true },
  running: { label: '运行中', tone: 'success', active: false },
  stopped: { label: '已停止', tone: 'neutral', active: false },
  suspended: { label: '已暂停', tone: 'neutral', active: false },
  changing: { label: '调整中', tone: 'info', active: true },
  failed: { label: '失败', tone: 'danger', active: false },
  destroying: { label: '清理中', tone: 'warning', active: true },
  destroyed: { label: '已销毁', tone: 'neutral', active: false },
  unknown: { label: '等待节点响应', tone: 'warning', active: true },
}

export function rolloutPollInterval(status: TeamLabGameRolloutStatus | undefined) {
  if (!status) return 0
  return rolloutStatusMeta[status].active ? 3_000 : status === 'ready' ? 10_000 : 0
}
