import api, { AwdpServiceCreateModel, ImageStatus, ImageType } from '@Api'
import {
  AwdpAdminSnapshot,
  flattenAwdpInstances,
  normalizeAwdpAttackLog,
  normalizeAwdpPatchSubmission,
  normalizeAwdpScore,
  normalizeAwdpService,
  normalizeAwdpStatus,
} from '../../../awdp/awdpDomain'
import { imageTemplateAdminApi } from '../../api/imageTemplateAdminApi'

export type AwdpServiceWriteModel = AwdpServiceCreateModel

export const awdpAdminApi = {
  async readyDockerImages() {
    const response = await imageTemplateAdminApi.list({ imageType: ImageType.Docker, page: 1, pageSize: 500 })
    return response.items
      .filter((item) => item.status === ImageStatus.Ready && item.registryUrl)
      .map((item) => ({ id: item.id, name: item.name, registryUrl: item.registryUrl as string }))
  },
  async snapshot(gameId: number): Promise<AwdpAdminSnapshot> {
    const [services, status, instances, scoreboard, attackLogs, patches] = await Promise.all([
      api.awdpAdmin.awdpAdminGetServices(gameId),
      api.awdpAdmin.awdpAdminGetStatus(gameId),
      api.awdpAdmin.awdpAdminGetInstances(gameId),
      api.awdpAdmin.awdpAdminGetScoreboard(gameId),
      api.awdpAdmin.awdpAdminGetAttackLogs(gameId, { count: 100, skip: 0 }),
      api.awdpAdmin.awdpAdminGetPatches(gameId, { count: 100, skip: 0 }),
    ])
    return {
      services: services.data.map(normalizeAwdpService),
      status: normalizeAwdpStatus(status.data),
      instances: flattenAwdpInstances(instances.data),
      scoreboard: scoreboard.data.map(normalizeAwdpScore),
      attackLogs: attackLogs.data.data.map(normalizeAwdpAttackLog),
      patches: patches.data.data.map(normalizeAwdpPatchSubmission),
    }
  },
  async createService(gameId: number, model: AwdpServiceWriteModel) {
    const response = await api.awdpAdmin.awdpAdminCreateService(gameId, model)
    return normalizeAwdpService(response.data)
  },
  async updateService(serviceId: number, model: AwdpServiceWriteModel) {
    const response = await api.awdpAdmin.awdpAdminUpdateService(serviceId, model)
    return normalizeAwdpService(response.data)
  },
  async deleteService(serviceId: number) {
    await api.awdpAdmin.awdpAdminDeleteService(serviceId)
  },
  async start(gameId: number) {
    await api.awdpAdmin.awdpAdminStartGame(gameId)
  },
  async stop(gameId: number) {
    await api.awdpAdmin.awdpAdminStopGame(gameId)
  },
  async resetInstance(instanceId: number) {
    const response = await api.awdpAdmin.awdpAdminResetInstance(instanceId)
    return response.data
  },
  async recoverInstance(instanceId: number) {
    const response = await api.awdpAdmin.awdpAdminRecoverInstance(instanceId)
    return response.data
  },
}
