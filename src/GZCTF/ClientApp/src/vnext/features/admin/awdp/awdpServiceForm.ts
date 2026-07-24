import { AwdpService } from '../../awdp/awdpDomain'
import { AwdpServiceWriteModel } from './api/awdpAdminApi'

export interface AwdpServiceDraft {
  name: string
  imageName: string
  exposePort: number
  checkerScript: string
  checkerEntrypoint: string
  expScript: string
  expEntrypoint: string
  originalScore: number
  attackPoints: number
  slaPoints: number
  patchPoints: number
  serviceAbnormalPenalty: number
  maxAttackPerRound: number
  attackPhaseMinutes: number
  patchPhaseMinutes: number
  totalRounds: number
  maxResetCount: number
  maxRecoveryCount: number
}

export const emptyAwdpServiceDraft = (): AwdpServiceDraft => ({
  name: '',
  imageName: '',
  exposePort: 80,
  checkerScript: '',
  checkerEntrypoint: 'python3 checker.py',
  expScript: '',
  expEntrypoint: 'python3 exp.py',
  originalScore: 1000,
  attackPoints: 50,
  slaPoints: 20,
  patchPoints: 100,
  serviceAbnormalPenalty: 200,
  maxAttackPerRound: 3,
  attackPhaseMinutes: 15,
  patchPhaseMinutes: 10,
  totalRounds: 20,
  maxResetCount: 10,
  maxRecoveryCount: 5,
})

export function awdpServiceDraft(service: AwdpService | null): AwdpServiceDraft {
  return service ? { ...service } : emptyAwdpServiceDraft()
}

export function validateAwdpService(draft: AwdpServiceDraft) {
  const errors: string[] = []
  if (!draft.name.trim()) errors.push('服务名称不能为空。')
  if (!draft.imageName.trim()) errors.push('容器镜像不能为空。')
  if (!Number.isInteger(draft.exposePort) || draft.exposePort < 1 || draft.exposePort > 65_535)
    errors.push('暴露端口必须为 1 到 65535 的整数。')
  const positiveFields: Array<[keyof AwdpServiceDraft, string]> = [
    ['originalScore', '原始分数'],
    ['attackPoints', '攻击得分'],
    ['slaPoints', 'SLA 得分'],
    ['patchPoints', '修补得分'],
    ['serviceAbnormalPenalty', '异常扣分'],
    ['maxAttackPerRound', '每轮攻击次数'],
    ['attackPhaseMinutes', '攻击阶段时长'],
    ['patchPhaseMinutes', '修补阶段时长'],
    ['totalRounds', '总轮数'],
  ]
  positiveFields.forEach(([key, label]) => {
    const value = draft[key]
    if (typeof value !== 'number' || !Number.isInteger(value) || value <= 0)
      errors.push(`${label}必须为大于 0 的整数。`)
  })
  if (
    !Number.isInteger(draft.maxResetCount) ||
    !Number.isInteger(draft.maxRecoveryCount) ||
    draft.maxResetCount < 0 ||
    draft.maxRecoveryCount < 0
  )
    errors.push('重置和恢复次数必须为不小于 0 的整数。')
  return errors
}

export function awdpServiceWarnings(draft: AwdpServiceDraft) {
  const warnings: string[] = []
  if (!draft.checkerScript.trim()) warnings.push('未配置 Checker 脚本')
  if (!draft.checkerEntrypoint.trim()) warnings.push('未配置 Checker 入口命令')
  if (!draft.expScript.trim()) warnings.push('未配置 Exp 脚本')
  if (!draft.expEntrypoint.trim()) warnings.push('未配置 Exp 入口命令')
  return warnings
}

export function toAwdpServiceWriteModel(draft: AwdpServiceDraft): AwdpServiceWriteModel {
  return {
    ...draft,
    name: draft.name.trim(),
    imageName: draft.imageName.trim(),
    checkerEntrypoint: draft.checkerEntrypoint.trim() || null,
    expEntrypoint: draft.expEntrypoint.trim() || null,
  }
}
