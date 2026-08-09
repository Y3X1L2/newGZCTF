import type { TeamLabPlayerAccessGrant } from './api/teamlabPlayerContracts'
import { parseTeamLabPlayerAccessGrant } from './api/teamlabPlayerParsers'

const storageVersion = 1
const storagePrefix = 'gzctf:teamlab:player-access'

function storageKey(gameId: number, runtimeId: string) {
  return `${storagePrefix}:v${storageVersion}:${gameId}:${runtimeId}`
}

function sessionStorageOrNull() {
  try {
    return typeof window === 'undefined' ? null : window.sessionStorage
  } catch {
    return null
  }
}

export function loadPlayerAccessGrant(gameId: number, runtimeId: string): TeamLabPlayerAccessGrant | null {
  const storage = sessionStorageOrNull()
  if (!storage) return null
  const key = storageKey(gameId, runtimeId)
  try {
    const raw = storage.getItem(key)
    if (!raw) return null
    const grant = parseTeamLabPlayerAccessGrant(JSON.parse(raw))
    if (grant.expiresAt !== null && grant.expiresAt <= Date.now()) {
      storage.removeItem(key)
      return null
    }
    return grant
  } catch {
    storage.removeItem(key)
    return null
  }
}

export function savePlayerAccessGrant(gameId: number, runtimeId: string, grant: TeamLabPlayerAccessGrant) {
  try {
    sessionStorageOrNull()?.setItem(storageKey(gameId, runtimeId), JSON.stringify(grant))
  } catch {
    // The active grant remains usable in memory when browser storage is unavailable.
  }
}

export function clearPlayerAccessGrant(gameId: number, runtimeId: string) {
  try {
    sessionStorageOrNull()?.removeItem(storageKey(gameId, runtimeId))
  } catch {
    // Storage availability must not block runtime access controls.
  }
}
