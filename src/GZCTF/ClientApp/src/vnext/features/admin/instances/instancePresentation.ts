import type { GlobalInstanceItem } from '../api'
import type { AdminStatusTone } from '../shared/AdminWorkbench'

export function instanceKindLabel(kind: string) {
  if (kind === 'container') return 'Docker'
  if (kind === 'vm') return 'VM'
  if (kind === 'pentest') return '渗透环境'
  if (kind === 'teamlab') return 'TeamLab'
  return kind || '未知资源'
}

export function instanceStatusMeta(instance: GlobalInstanceItem) {
  const status = instance.status.toLowerCase()
  if (status.includes('error') || status.includes('failed') || status.includes('orphaned')) {
    return { label: status.includes('orphaned') ? '孤儿资源' : '异常', tone: 'danger' as AdminStatusTone }
  }
  if (status.includes('cleanup') || status.includes('destroying')) {
    return { label: '清理中', tone: 'warning' as AdminStatusTone }
  }
  if (instance.isActive) return { label: '运行中', tone: 'success' as AdminStatusTone }
  if (status.includes('destroyed')) return { label: '已销毁', tone: 'neutral' as AdminStatusTone }
  if (status.includes('stopped')) return { label: '已停止', tone: 'neutral' as AdminStatusTone }
  return { label: instance.status || '历史记录', tone: 'neutral' as AdminStatusTone }
}

export function instanceOwnerLabel(instance: GlobalInstanceItem) {
  return instance.teamName || instance.userName || '平台调度'
}

export function instanceContextLabel(instance: GlobalInstanceItem) {
  return [instance.gameTitle, instance.challengeTitle].filter(Boolean).join(' / ') || '无业务上下文'
}

export function instanceEntryLabel(instance: GlobalInstanceItem) {
  if (instance.entry) return instance.entry
  if (instance.ip && instance.port) return `${instance.ip}:${instance.port}`
  return instance.ip || '—'
}

export function canDestroyInstance(instance: GlobalInstanceItem) {
  return instance.isActive && (instance.kind === 'container' || instance.kind === 'vm')
}
