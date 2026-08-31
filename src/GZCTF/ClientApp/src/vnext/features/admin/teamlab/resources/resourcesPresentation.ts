import type {
  TeamLabConnectorHealth,
  TeamLabConnectorKind,
  TeamLabDeviceArtifactKind,
  TeamLabLinkPolicyKind,
} from '../api/teamlabResourcesContracts'

type Tone = 'success' | 'info' | 'warning' | 'danger' | 'neutral'

export const deviceArtifactKindLabels: Record<TeamLabDeviceArtifactKind, string> = {
  'oci-image': 'OCI 镜像',
  'vm-image': 'VM 镜像',
}

export const connectorKindLabels: Record<TeamLabConnectorKind, string> = {
  'managed-nic': '受管网卡',
  vlan: 'VLAN',
  segment: '网段',
  serial: '串口',
  'usb-gateway': 'USB 设备网关',
  'dedicated-network': '专用外部网络',
}

export const connectorHealthLabels: Record<TeamLabConnectorHealth, { label: string; tone: Tone }> = {
  unknown: { label: '未知', tone: 'neutral' },
  healthy: { label: '健康', tone: 'success' },
  degraded: { label: '降级', tone: 'warning' },
  unreachable: { label: '不可达', tone: 'danger' },
}

export const linkPolicyKindLabels: Record<TeamLabLinkPolicyKind, string> = {
  'access-rule': '访问控制',
  nat: '地址转换',
  'bandwidth-limit': '带宽限制',
  latency: '时延',
  jitter: '抖动',
  'packet-loss': '丢包',
  duplication: '重复包',
  'link-break': '断链',
}

/** One-line parameter summary for table cells; detailed JSON lives in the drawer. */
export function summarizeLinkPolicyParameters(kind: TeamLabLinkPolicyKind, parameters: unknown): string {
  if (kind === 'link-break') return '全链路中断'
  if (!parameters || typeof parameters !== 'object') return '—'
  const entries = Object.entries(parameters as Record<string, unknown>)
  if (entries.length === 0) return '—'
  return entries
    .map(([key, value]) => `${key}: ${formatParameterValue(value)}`)
    .join('，')
}

function formatParameterValue(value: unknown): string {
  if (typeof value === 'string') return value
  if (typeof value === 'number' || typeof value === 'boolean') return String(value)
  return JSON.stringify(value)
}

/** Backend projections carry ISO strings; the admin formatter takes epoch millis. */
export function toAdminDate(iso: string | null | undefined): number | null {
  if (!iso) return null
  const millis = Date.parse(iso)
  return Number.isFinite(millis) ? millis : null
}

export function formatBytes(sizeBytes: number): string {
  if (!Number.isFinite(sizeBytes) || sizeBytes <= 0) return '—'
  const units = ['B', 'KiB', 'MiB', 'GiB', 'TiB']
  let value = sizeBytes
  let unit = 0
  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024
    unit += 1
  }
  const text = new Intl.NumberFormat('zh-CN', { maximumFractionDigits: 1 }).format(value)
  return `${text} ${units[unit]}`
}
