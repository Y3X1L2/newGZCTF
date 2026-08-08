import type {
  TeamLabCaptureStatus,
  TeamLabEventLevel,
  TeamLabPathConfidence,
  TeamLabRuntimeStatus,
} from '../api'

export const terminalRuntimeStatuses = new Set<TeamLabRuntimeStatus>([
  'failed',
  'destroyed',
])

export function isRuntimeTerminal(status: TeamLabRuntimeStatus | undefined) {
  return status ? terminalRuntimeStatuses.has(status) : false
}

export function runtimeRefreshInterval(status: TeamLabRuntimeStatus | undefined) {
  if (!status) return 0
  if (isRuntimeTerminal(status)) return 0
  return status === 'running' ? 8_000 : 2_500
}

export const runtimeStageOrder: readonly TeamLabRuntimeStatus[] = [
  'pending',
  'planning',
  'scheduled',
  'deploying',
  'probing',
  'running',
]

export const runtimeStatusLabels: Record<TeamLabRuntimeStatus, string> = {
  pending: '等待调度',
  planning: '规划分片',
  scheduled: '资源已预留',
  deploying: '部署资产',
  probing: '连通性探测',
  running: '运行就绪',
  failed: '执行失败',
  'cleanup-pending': '等待清理',
  paused: '已暂停',
  destroying: '正在销毁',
  destroyed: '已销毁',
}

export const eventLevelLabels: Record<TeamLabEventLevel, string> = {
  info: '信息',
  success: '成功',
  warning: '警告',
  error: '错误',
}

export const pathConfidenceLabels: Record<TeamLabPathConfidence, string> = {
  'packet-exact': '报文精确',
  'process-correlated': '进程关联',
  'temporally-related': '时间关联',
}

export const captureStatusLabels: Record<TeamLabCaptureStatus, string> = {
  pending: '等待启动',
  running: '抓包中',
  stopping: '正在停止',
  completed: '已完成',
  failed: '失败',
  expired: '已过期',
  'cleanup-pending': '等待清理',
}

export function endpoint(ip: string, port: number | null) {
  return port === null ? ip : `${ip}:${port}`
}

export function formatBytes(value: number) {
  if (!Number.isFinite(value) || value <= 0) return '0 B'
  const units = ['B', 'KiB', 'MiB', 'GiB', 'TiB']
  const unit = Math.min(Math.floor(Math.log(value) / Math.log(1024)), units.length - 1)
  return `${(value / 1024 ** unit).toFixed(unit === 0 ? 0 : 1)} ${units[unit]}`
}
