import { ImageStatus, ImageType, OSType } from '@Api'
import { imageTemplateAdminApi } from '../../api'
import type { VmDeviceType } from '../model/topologyMapper'

export async function resolveTeamLabVmDeviceTypes(templateIds: readonly number[]) {
  const uniqueIds = [...new Set(templateIds)].filter((id) => id > 0).sort((left, right) => left - right)
  const results = await Promise.allSettled(uniqueIds.map((id) => imageTemplateAdminApi.detail(id)))
  const templates = results.flatMap((result) => (result.status === 'fulfilled' ? [result.value] : []))
  return new Map<number, VmDeviceType>(
    templates.map((template) => [template.id, template.osType === OSType.Windows ? 'windows-vm' : 'linux-vm'])
  )
}

export interface TeamLabImageOption {
  id: number
  name: string
  deviceType: 'docker' | VmDeviceType
  remoteAccessProtocol?: 'ssh' | 'rdp' | null
}

const imagePageSize = 100

export async function listTeamLabImageOptions(): Promise<readonly TeamLabImageOption[]> {
  const first = await imageTemplateAdminApi.list({ page: 1, pageSize: imagePageSize })
  const pageCount = Math.ceil(first.total / imagePageSize)
  const remaining = await Promise.all(
    Array.from({ length: Math.max(0, pageCount - 1) }, (_, index) =>
      imageTemplateAdminApi.list({ page: index + 2, pageSize: imagePageSize })
    )
  )
  return [first, ...remaining]
    .flatMap((page) => page.items)
    .filter((template) => template.status === ImageStatus.Ready)
    .map<TeamLabImageOption>((template) => ({
      id: template.id,
      name: template.name,
      deviceType:
        template.imageType === ImageType.Docker
          ? 'docker'
          : template.osType === OSType.Windows
            ? 'windows-vm'
            : 'linux-vm',
      remoteAccessProtocol:
        template.remoteAccessProtocol === 'ssh' || template.remoteAccessProtocol === 'rdp'
          ? template.remoteAccessProtocol
          : null,
    }))
    .sort((left, right) => left.name.localeCompare(right.name, 'zh-CN') || left.id - right.id)
}
