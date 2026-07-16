import { describe, expect, it } from 'vitest'
import type { AdminGamePhase } from '../../api'
import { toLocalDateTimeInput } from '../gamePresentation'
import { phaseDurationLabel, phaseLifecycle, validatePhaseDraft } from './phaseModel'

const minute = 60_000
const game = { start: 0, end: 10 * minute }
const phases: AdminGamePhase[] = [
  { id: 1, gameId: 7, name: '第一阶段', startTime: 2 * minute, endTime: 4 * minute, ctfEnabled: true },
]

describe('phaseModel', () => {
  it('rejects phases outside the game or overlapping an existing phase', () => {
    expect(validatePhaseDraft({ name: '越界', start: toLocalDateTimeInput(-minute), end: toLocalDateTimeInput(minute), ctfEnabled: true }, game, phases)).toContain('阶段时间必须位于比赛开始和结束时间之间。')
    const start = toLocalDateTimeInput(2 * minute)
    const end = toLocalDateTimeInput(4 * minute)
    expect(validatePhaseDraft({ name: '重叠', start, end, ctfEnabled: true }, game, phases)).toContain('阶段时间与“第一阶段”重叠。')
  })

  it('classifies lifecycle boundaries', () => {
    expect(phaseLifecycle(phases[0], 2 * minute - 1)).toBe('scheduled')
    expect(phaseLifecycle(phases[0], 2 * minute)).toBe('active')
    expect(phaseLifecycle(phases[0], 4 * minute)).toBe('ended')
  })

  it('formats a compact duration', () => {
    expect(phaseDurationLabel({ startTime: 0, endTime: 90 * 60_000 })).toBe('1小时 30分钟')
  })
})
