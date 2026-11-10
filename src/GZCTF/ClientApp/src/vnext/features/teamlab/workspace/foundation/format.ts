export function reading(value: number | undefined, scale = 1) {
  if (value === undefined) return '—'
  const scaled = value / scale
  return scaled.toLocaleString(
    'zh-CN',
    Math.abs(scaled) > 0 && Math.abs(scaled) < 1 ? { maximumSignificantDigits: 3 } : { maximumFractionDigits: 2 }
  )
}

export function byteUnit(value: number, rate = false) {
  const index = value >= 2 ** 30 ? 3 : value >= 2 ** 20 ? 2 : value >= 2 ** 10 ? 1 : 0
  return {
    scale: 2 ** (index * 10),
    unit: ['B', 'KiB', 'MiB', 'GiB'][index] + (rate ? '/s' : ''),
  }
}

export function memory(mib: number) {
  return `${new Intl.NumberFormat('zh-CN', { maximumFractionDigits: 1 }).format(mib / 1024)} GiB`
}
export function dateTime(value: string) {
  return new Intl.DateTimeFormat('zh-CN', {
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    hour12: false,
  }).format(new Date(value))
}
export function clockTime(value: string | number) {
  return new Date(value).toLocaleTimeString('zh-CN', { hour12: false })
}
export const statusLabels: Record<string, string> = {
  draft: '待运行',
  deploying: '部署中',
  running: '运行中',
  restarting: '重启中',
  stopped: '已停止',
  suspended: '已暂停',
  changing: '应用变更',
  resizing: '扩容中',
  destroying: '销毁中',
  destroyed: '已销毁',
  failed: '失败',
  unknown: '状态未知',
  ready: '可用',
  draining: '迁出中',
  importing: '导入中',
  deleting: '删除中',
  capturing: '捕获中',
  creating: '创建中',
  connecting: '连接中',
  preparing: '准备中',
  online: '在线',
  offline: '离线',
  queued: '等待执行',
  succeeded: '已完成',
  partially_applied: '部分完成',
  created: '已创建',
  prepared: '已准备',
  reserved: '已分配',
}
export const actionLabels: Record<string, string> = {
  start: '启动',
  stop: '停止',
  'force-stop': '强制停止',
  reboot: '重启',
  suspend: '暂停',
  resume: '继续运行',
  rebuild: '重建',
  migrate: '迁移设备',
  'move-network': '迁移网络入口',
  'refresh-network': '更新网络配置',
  'retire-node': '退出节点',
  'trim-template-cache': '释放镜像缓存',
  destroy: '销毁',
  changes: '应用变更',
  change: '应用变更',
  'prepare-template': '准备模板',
  'capture-template': '固化模板',
  'delete-template': '删除模板',
  'capture-recovery': '创建恢复点',
  'delete-recovery': '删除恢复点',
  'restore-recovery': '恢复环境',
  'clone-recovery': '克隆环境',
  'connect-backup-repository': '连接备份仓库',
  'create-backup': '保存备份',
  'delete-backup': '删除备份',
  create: '创建环境',
  'vpn-create': '创建 VPN',
  'vpn-revoke': '撤销 VPN',
}
export function stateLabel(value: string) {
  return statusLabels[value] ?? value
}
