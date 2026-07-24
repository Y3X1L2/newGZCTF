import {
  Division,
  DivisionCreateModel,
  DivisionEditModel,
  GamePermission,
  GameType,
} from '@Api'

export interface PermissionOption {
  value: GamePermission
  label: string
  description: string
  challengeScoped: boolean
}

export const permissionOptions: PermissionOption[] = [
  { value: GamePermission.JoinGame, label: '允许报名', description: '允许队伍申请加入该赛区。', challengeScoped: false },
  { value: GamePermission.RankOverall, label: '计入总榜', description: '该赛区队伍参与比赛总榜排名。', challengeScoped: false },
  { value: GamePermission.RequireReview, label: '需要审核', description: '报名后由管理员审核通过。', challengeScoped: false },
  { value: GamePermission.ViewChallenge, label: '查看题目', description: '允许查看题面和附件。', challengeScoped: true },
  { value: GamePermission.SubmitFlags, label: '提交答案', description: '允许提交 Flag 或题目答案。', challengeScoped: true },
  { value: GamePermission.GetScore, label: '获得分数', description: '正确提交会获得题目分数。', challengeScoped: true },
  { value: GamePermission.GetBlood, label: '获得血分', description: '允许获得一血、二血和三血奖励。', challengeScoped: true },
  { value: GamePermission.AffectDynamicScore, label: '影响动态分值', description: '解题会参与动态分值衰减计算。', challengeScoped: true },
]

const knownPermissionMask = permissionOptions.reduce((mask, option) => mask | option.value, 0)

export interface DivisionEditorDraft {
  name: string
  inviteCode: string
  defaultPermissions: number
  challengeConfigs: Array<{ challengeId: number; permissions: number }>
}

export function permissionMask(mask?: number | null) {
  return mask === undefined || mask === null || mask === GamePermission.All ? knownPermissionMask : mask
}

export function hasGamePermission(mask: number | null | undefined, permission: GamePermission) {
  return (permissionMask(mask) & permission) === permission
}

export function toggleGamePermission(mask: number, permission: GamePermission, enabled: boolean) {
  const explicit = permissionMask(mask)
  return enabled ? explicit | permission : explicit & ~permission
}

export function permissionsForGameType(gameType?: GameType) {
  const ctf = gameType === GameType.Jeopardy || gameType === GameType.Mixed
  return permissionOptions.filter((option) => ctf || !option.challengeScoped)
}

export function divisionEditorDraft(division?: Division | null): DivisionEditorDraft {
  return {
    name: division?.name ?? '',
    inviteCode: division?.inviteCode ?? '',
    defaultPermissions: permissionMask(division?.defaultPermissions),
    challengeConfigs: (division?.challengeConfigs ?? []).map((config) => ({
      challengeId: config.challengeId,
      permissions: permissionMask(config.permissions),
    })),
  }
}

function divisionPayload(draft: DivisionEditorDraft): DivisionCreateModel {
  return {
    name: draft.name.trim(),
    inviteCode: draft.inviteCode.trim() || null,
    defaultPermissions: draft.defaultPermissions,
    challengeConfigs: draft.challengeConfigs.map((config) => ({ ...config })),
  }
}

export function divisionCreatePayload(draft: DivisionEditorDraft) {
  return divisionPayload(draft)
}

export function divisionUpdatePayload(draft: DivisionEditorDraft): DivisionEditModel {
  return divisionPayload(draft)
}

export function validateDivisionDraft(draft: DivisionEditorDraft) {
  const issues: string[] = []
  const name = draft.name.trim()
  if (!name) issues.push('请输入赛区名称。')
  if (name.length > 31) issues.push('赛区名称不能超过 31 个字符。')
  if (draft.inviteCode.trim().length > 32) issues.push('赛区邀请码不能超过 32 个字符。')
  const ids = draft.challengeConfigs.map((config) => config.challengeId)
  if (ids.some((id) => !Number.isInteger(id) || id <= 0)) issues.push('题目权限覆盖包含无效题目。')
  if (new Set(ids).size !== ids.length) issues.push('同一道题目不能配置多次权限覆盖。')
  return issues
}

export function divisionPermissionSummary(mask?: number | null) {
  const enabled = permissionOptions.filter((option) => hasGamePermission(mask, option.value))
  if (enabled.length === permissionOptions.length) return '全部权限'
  if (!enabled.length) return '未授予权限'
  return enabled.map((option) => option.label).join('、')
}

export function generateDivisionInviteCode() {
  const alphabet = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789'
  const values = crypto.getRandomValues(new Uint8Array(10))
  return Array.from(values, (value) => alphabet[value % alphabet.length]).join('')
}
