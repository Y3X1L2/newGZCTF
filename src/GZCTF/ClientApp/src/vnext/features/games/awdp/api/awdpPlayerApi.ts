import api from '@Api'
import {
  AwdpPlayerSnapshot,
  normalizeAwdpAttackLog,
  normalizeAwdpInstance,
  normalizeAwdpPatchState,
  normalizeAwdpScore,
  normalizeAwdpStatus,
} from '../../../awdp/awdpDomain'

export const awdpPlayerApi = {
  async snapshot(gameId: number): Promise<AwdpPlayerSnapshot> {
    const [status, instances, scoreboard, attackLogs, patchStatus] = await Promise.all([
      api.awdpPlayer.awdpPlayerGetStatus(gameId),
      api.awdpPlayer.awdpPlayerGetInstances(gameId),
      api.awdpPlayer.awdpPlayerGetScoreboard(gameId),
      api.awdpPlayer.awdpPlayerGetAttackLogs(gameId, { count: 50, skip: 0 }),
      api.awdpPlayer.awdpPlayerGetPatchStatus(gameId),
    ])
    return {
      status: normalizeAwdpStatus(status.data),
      instances: instances.data.map(normalizeAwdpInstance),
      scoreboard: scoreboard.data.map(normalizeAwdpScore),
      attackLogs: attackLogs.data.data.map(normalizeAwdpAttackLog),
      patchStatus: patchStatus.data.map(normalizeAwdpPatchState),
    }
  },
  async submitFlag(gameId: number, flag: string) {
    const response = await api.awdpPlayer.awdpPlayerSubmitFlag(gameId, { flag })
    return response.data
  },
  async submitPatch(gameId: number, serviceId: number, file: File) {
    const response = await api.awdpPlayer.awdpPlayerSubmitPatch(gameId, { ServiceId: serviceId, File: file })
    return response.data
  },
  async resetInstance(instanceId: number) {
    const response = await api.awdpPlayer.awdpPlayerResetInstance(instanceId)
    return response.data
  },
  async recoverInstance(instanceId: number) {
    const response = await api.awdpPlayer.awdpPlayerRecoverInstance(instanceId)
    return response.data
  },
}
