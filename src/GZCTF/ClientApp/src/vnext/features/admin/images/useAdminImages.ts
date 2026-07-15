import useSWR from 'swr'
import { ImageStatus, ImageType, OSType } from '@Api'
import { imageTemplateAdminApi } from '../api'

export interface AdminImageFilters {
  search?: string
  imageType?: ImageType
  osType?: OSType
}

const API_PAGE_SIZE = 100

async function fetchAllImages(filters: AdminImageFilters) {
  const first = await imageTemplateAdminApi.list({ ...filters, page: 1, pageSize: API_PAGE_SIZE })
  const items = [...first.items]
  const pages = Math.ceil(first.total / API_PAGE_SIZE)

  for (let page = 2; page <= pages; page += 1) {
    const response = await imageTemplateAdminApi.list({ ...filters, page, pageSize: API_PAGE_SIZE })
    items.push(...response.items)
  }
  return items
}

export function useAdminImages(filters: AdminImageFilters) {
  const result = useSWR(
    ['vnext:admin:images', filters.search ?? '', filters.imageType ?? 'all', filters.osType ?? 'all'],
    () => fetchAllImages(filters),
    {
      revalidateOnFocus: false,
      refreshInterval: (data) =>
        data?.some((template) => template.status === ImageStatus.Importing || template.status === ImageStatus.Deleting)
          ? 5_000
          : 0,
    }
  )

  return {
    images: result.data,
    error: result.error,
    isLoading: !result.data && !result.error,
    isRefreshing: result.isValidating,
    mutate: result.mutate,
  }
}

export function useDockerRegistry() {
  const result = useSWR('vnext:admin:docker-registry', () => imageTemplateAdminApi.registry(), {
    revalidateOnFocus: false,
    refreshInterval: 60_000,
  })
  return {
    registry: result.data,
    error: result.error,
    isLoading: !result.data && !result.error,
    mutate: result.mutate,
  }
}

export function imageTypeLabel(type: ImageType) {
  if (type === ImageType.Docker) return 'Docker'
  if (type === ImageType.Qcow2) return 'QCOW2'
  if (type === ImageType.Ova) return 'OVA'
  return 'VMDK'
}

export function imageOsLabel(os: OSType) {
  return os === OSType.Windows ? 'Windows' : 'Linux'
}

export function imageStatusMeta(status: ImageStatus) {
  if (status === ImageStatus.Ready) return { label: '可用', tone: 'success' as const, active: false }
  if (status === ImageStatus.Importing) return { label: '处理中', tone: 'info' as const, active: true }
  if (status === ImageStatus.Error) return { label: '异常', tone: 'danger' as const, active: false }
  return { label: '删除中', tone: 'warning' as const, active: true }
}

export function formatBytes(value: number) {
  if (!value) return '0 B'
  const units = ['B', 'KB', 'MB', 'GB', 'TB']
  const index = Math.min(Math.floor(Math.log(value) / Math.log(1024)), units.length - 1)
  return `${(value / 1024 ** index).toFixed(index > 1 ? 1 : 0)} ${units[index]}`
}

export function formatAdminTime(value: number | null | undefined) {
  if (!value) return '—'
  return new Intl.DateTimeFormat('zh-CN', {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    hour12: false,
  }).format(value)
}
