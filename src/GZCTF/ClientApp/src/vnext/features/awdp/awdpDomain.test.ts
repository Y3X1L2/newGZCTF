import { describe, expect, it } from 'vitest'
import { AwdpPatchStatus, AwdpRoundStatus, CheckerStatus } from '@Api'
import {
  checkerMeta,
  normalizeAwdpInstance,
  normalizeAwdpStatus,
  patchMeta,
  phaseEndsAt,
  remainingPhaseLabel,
} from './awdpDomain'

describe('AWDP domain', () => {
  it('calculates attack and patch deadlines from the same round start', () => {
    const attack = normalizeAwdpStatus({
      roundStartTime: 1_000_000,
      attackPhaseMinutes: 10,
      patchPhaseMinutes: 5,
      status: AwdpRoundStatus.AttackPhase,
    })
    expect(phaseEndsAt(attack)).toBe(1_600_000)
    expect(remainingPhaseLabel(attack, 1_540_000)).toBe('01:00')

    const patch = { ...attack, status: AwdpRoundStatus.PatchPhase }
    expect(phaseEndsAt(patch)).toBe(1_900_000)
  })

  it('keeps service health separate from exploit verification', () => {
    expect(checkerMeta(CheckerStatus.Mumble)).toMatchObject({ label: '部分异常', tone: 'warning' })
    expect(patchMeta(AwdpPatchStatus.ExpFailed)).toMatchObject({ label: '漏洞已阻断', tone: 'success' })
    expect(patchMeta(AwdpPatchStatus.CheckerFailed)).toMatchObject({ tone: 'danger' })
  })

  it('normalizes an attack endpoint without allowing executable schemes', () => {
    expect(normalizeAwdpInstance({ ipAddress: '10.24.0.30', port: 32768 }).endpoint).toBe('http://10.24.0.30:32768')
    expect(normalizeAwdpInstance({ ipAddress: 'javascript://alert(1)', port: 80 }).endpoint).toBeNull()
  })
})
