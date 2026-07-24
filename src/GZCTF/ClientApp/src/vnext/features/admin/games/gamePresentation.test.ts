import { describe, expect, it } from 'vitest'
import { ChallengeCategory, ChallengeType, EnvironmentType, GameType } from '@Api'
import {
  challengeConfigurationIssues,
  emptyGameCreateDraft,
  fromLocalDateTimeInput,
  gameCreatePayload,
  gameLifecycle,
  toLocalDateTimeInput,
  validateGameCreateDraft,
  validateGameImportFile,
} from './gamePresentation'

describe('game admin presentation', () => {
  it('derives the lifecycle from the real start and end timestamps', () => {
    const game = { start: 2_000, end: 4_000 }
    expect(gameLifecycle(game, 1_000)).toBe('scheduled')
    expect(gameLifecycle(game, 3_000)).toBe('running')
    expect(gameLifecycle(game, 4_000)).toBe('ended')
  })

  it('creates hidden games and preserves the selected business settings', () => {
    const draft = {
      ...emptyGameCreateDraft(1_000),
      title: '  Phase 5B Test  ',
      summary: '  summary  ',
      gameType: GameType.AWDP,
      inviteCode: ' invite ',
      teamMemberCountLimit: 4,
    }
    expect(validateGameCreateDraft(draft)).toEqual([])
    expect(gameCreatePayload(draft)).toMatchObject({
      title: 'Phase 5B Test',
      summary: 'summary',
      gameType: GameType.AWDP,
      hidden: true,
      inviteCode: 'invite',
      teamMemberCountLimit: 4,
    })
    expect(gameCreatePayload(draft)).not.toHaveProperty('bloodBonus')
  })

  it('rejects invalid times and non-zip import files before a request is sent', () => {
    const draft = { ...emptyGameCreateDraft(), title: 'Test', end: emptyGameCreateDraft().start }
    expect(validateGameCreateDraft(draft)).toContain('比赛结束时间必须晚于开始时间。')
    expect(validateGameImportFile({ name: 'game.json', size: 10, type: 'application/json' })).toHaveLength(2)
    expect(validateGameImportFile({ name: 'game.zip', size: 10, type: 'application/zip' })).toEqual([])
  })

  it('keeps incomplete local date-time input outside the timestamp model', () => {
    expect(toLocalDateTimeInput(Number.NaN)).toBe('')
    expect(toLocalDateTimeInput(Number.POSITIVE_INFINITY)).toBe('')
    expect(Number.isNaN(fromLocalDateTimeInput(''))).toBe(true)
    expect(Number.isNaN(fromLocalDateTimeInput('2026-07-16T'))).toBe(true)

    const complete = '2026-07-16T19:30'
    expect(toLocalDateTimeInput(fromLocalDateTimeInput(complete))).toBe(complete)
  })

  it('reports only configuration facts available in challenge detail', () => {
    expect(
      challengeConfigurationIssues({
        title: 'Docker',
        category: ChallengeCategory.Web,
        type: ChallengeType.DynamicContainer,
        environment: EnvironmentType.Docker,
        containerImage: '',
        exposePort: null,
      })
    ).toEqual(['缺少 Docker 镜像', '缺少暴露端口'])
  })
})
