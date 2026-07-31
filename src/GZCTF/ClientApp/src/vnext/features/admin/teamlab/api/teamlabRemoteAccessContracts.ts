export type TeamLabRemoteProtocol = 'containerTerminal' | 'ssh' | 'rdp'
export type TeamLabRemoteSessionStatus = 'creating' | 'ready' | 'connected' | 'ending' | 'ended' | 'failed'

export interface TeamLabRemoteAccessAvailability {
  assetId: number
  assetName: string
  protocol: TeamLabRemoteProtocol | null
  available: boolean
  unavailableReason: string | null
}

export interface TeamLabRemoteSession {
  id: string
  runtimeId: string
  assetId: number
  assetName: string
  protocol: TeamLabRemoteProtocol
  status: TeamLabRemoteSessionStatus
  reason: string
  createdAt: number
  expiresAt: number
  connectedAt: number | null
  endedAt: number | null
  endReason: string | null
}

export interface TeamLabRemoteConnect {
  url: string
  expiresAt: number
}
