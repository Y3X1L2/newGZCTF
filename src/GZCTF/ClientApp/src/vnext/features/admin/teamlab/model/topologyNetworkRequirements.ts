import type { TeamLabGuestNetworkRequirements } from '../api/teamlabContracts'

/** Keep omission, null and explicit empty lists distinct during editor round trips. */
export function copyNetworkRequirements(source: TeamLabGuestNetworkRequirements): TeamLabGuestNetworkRequirements {
  return {
    ...(source.guestInterfaceName !== undefined ? { guestInterfaceName: source.guestInterfaceName } : {}),
    ...(source.useDefaultGateway !== undefined ? { useDefaultGateway: source.useDefaultGateway } : {}),
    ...(source.dnsServers !== undefined
      ? { dnsServers: source.dnsServers === null ? null : [...source.dnsServers] }
      : {}),
    ...(source.staticRoutes !== undefined
      ? { staticRoutes: source.staticRoutes === null ? null : source.staticRoutes.map((route) => ({ ...route })) }
      : {}),
  }
}
