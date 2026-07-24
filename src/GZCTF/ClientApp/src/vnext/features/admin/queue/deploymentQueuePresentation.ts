import type { DeploymentTask } from '../api'
import { formatAdminDate } from '../shared/adminFormat'
import type { AdminStatusTone } from '../shared/AdminWorkbench'

export { formatAdminDate }

export const activeDeploymentStatuses = new Set(['pending', 'scheduling', 'scheduled', 'running'])

const stageLabels: Record<number, string> = {
  0: '等待入队',
  1: '准入检查',
  2: '等待容量',
  3: '准备镜像',
  4: '拉取镜像',
  5: '校验镜像',
  6: '等待节点执行',
  7: '创建容器',
  8: '创建虚拟机',
  9: '应用运行网络',
  10: '创建运行资产',
  11: '启动探测',
  12: '开放入口',
  13: '延长运行时间',
  14: '停止资源',
  15: '销毁资源',
  16: '回滚',
  17: '已就绪',
  18: '失败',
  19: '已取消',
}

export function deploymentStatusMeta(status: string) {
  const normalized = status.toLowerCase()
  if (normalized === 'completed') return { label: '已完成', tone: 'success' as AdminStatusTone, active: false }
  if (normalized === 'failed') return { label: '失败', tone: 'danger' as AdminStatusTone, active: false }
  if (normalized === 'cancelled') return { label: '已取消', tone: 'neutral' as AdminStatusTone, active: false }
  if (normalized === 'running') return { label: '执行中', tone: 'info' as AdminStatusTone, active: true }
  if (normalized === 'scheduled') return { label: '已分配', tone: 'info' as AdminStatusTone, active: true }
  if (normalized === 'scheduling') return { label: '调度中', tone: 'warning' as AdminStatusTone, active: true }
  return { label: normalized === 'pending' ? '等待中' : status || '未知', tone: 'warning' as AdminStatusTone, active: true }
}

export function deploymentStageLabel(stage: number | null | undefined, fallback?: string | null) {
  if (stage === null || stage === undefined) return fallback || '尚未上报'
  return stageLabels[stage] ?? fallback ?? `阶段 ${stage}`
}

export function deploymentSlotsLabel(task: DeploymentTask) {
  const slots = []
  if (task.dockerSlots > 0) slots.push(`Docker ${task.dockerSlots}`)
  if (task.vmSlots > 0) slots.push(`VM ${task.vmSlots}`)
  return slots.length ? slots.join(' / ') : '无槽位占用'
}

export function formatDeploymentDuration(task: DeploymentTask, now = Date.now()) {
  const end = task.completedAt ?? now
  const start = task.startedAt ?? task.createdAt
  const seconds = Math.max(0, Math.round((end - start) / 1000))
  if (seconds < 60) return `${seconds} 秒`
  const minutes = Math.floor(seconds / 60)
  if (minutes < 60) return `${minutes} 分 ${seconds % 60} 秒`
  const hours = Math.floor(minutes / 60)
  return `${hours} 小时 ${minutes % 60} 分`
}
