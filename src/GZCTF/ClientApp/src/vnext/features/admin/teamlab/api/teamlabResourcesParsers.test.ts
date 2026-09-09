import { describe, expect, it } from 'vitest'
import { parseTeamLabDevicePackage, parseTeamLabLinkPolicy } from './teamlabResourcesParsers'

describe('TeamLab resource wire contracts', () => {
  it('reads server timestamps, memoryMib and the numeric topology binding', () => {
    const result = parseTeamLabDevicePackage({ id: 'package-public-id', bindingId: 7, name: 'modbus', displayName: 'MODBUS', version: '1.0',
      artifactKind: 'oci-image', artifactReference: 'registry/modbus', digest: null, description: null, supportedAssetKinds: ['docker'],
      cpuMillis: 1000, memoryMib: 256, storageGib: 1, enabled: true, archived: false, createdAt: 0, updatedAt: 1000 })
    expect(result.id).toBe('package-public-id')
    expect(result.bindingId).toBe(7)
    expect(result.memoryMiB).toBe(256)
    expect(result.updatedAt).toBe('1970-01-01T00:00:01.000Z')
  })
  it('keeps failed recovery visible without fabricating a recovery time', () => {
    const result = parseTeamLabLinkPolicy({ id: 'policy', runtimeId: 'runtime', networkKey: 'net', assetKey: 'web', kind: 'latency',
      parameters: { delayMillis: 10 }, status: 'failed', recoverAt: 1000, appliedAt: 0, recoveredAt: null, recoverOrigin: 'none', lastError: '节点不可达' })
    expect(result.status).toBe('failed')
    expect(result.recoveredAt).toBeNull()
    expect(result.lastError).toBe('节点不可达')
  })
})
