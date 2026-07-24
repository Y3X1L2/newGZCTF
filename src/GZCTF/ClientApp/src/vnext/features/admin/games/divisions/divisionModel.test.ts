import { describe, expect, it } from 'vitest'
import { GamePermission, GameType } from '@Api'
import {
  divisionPermissionSummary,
  hasGamePermission,
  permissionsForGameType,
  toggleGamePermission,
  validateDivisionDraft,
} from './divisionModel'

describe('divisionModel', () => {
  it('expands the All mask before toggling a permission', () => {
    const mask = toggleGamePermission(GamePermission.All, GamePermission.RequireReview, false)
    expect(hasGamePermission(mask, GamePermission.RequireReview)).toBe(false)
    expect(hasGamePermission(mask, GamePermission.JoinGame)).toBe(true)
  })

  it('hides challenge-scoped permissions for theory games', () => {
    expect(permissionsForGameType(GameType.Theory).every((option) => !option.challengeScoped)).toBe(true)
    expect(permissionsForGameType(GameType.Jeopardy).some((option) => option.challengeScoped)).toBe(true)
  })

  it('validates duplicate challenge overrides', () => {
    expect(
      validateDivisionDraft({
        name: '公开组',
        inviteCode: '',
        defaultPermissions: GamePermission.All,
        challengeConfigs: [
          { challengeId: 1, permissions: GamePermission.All },
          { challengeId: 1, permissions: GamePermission.JoinGame },
        ],
      })
    ).toContain('同一道题目不能配置多次权限覆盖。')
  })

  it('summarizes a restricted permission set', () => {
    expect(divisionPermissionSummary(GamePermission.JoinGame | GamePermission.RankOverall)).toBe('允许报名、计入总榜')
  })
})
