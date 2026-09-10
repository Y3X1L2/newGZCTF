import useSWR from 'swr'
import { deviceHealthApi } from '../api/teamlabDeviceHealthApi'

export function useDeviceHealth(runtimeId: string, generation: number) {
  return useSWR(['vnext:teamlab:device-health', runtimeId, generation], () => deviceHealthApi.read(runtimeId), { refreshInterval: 5000 })
}
