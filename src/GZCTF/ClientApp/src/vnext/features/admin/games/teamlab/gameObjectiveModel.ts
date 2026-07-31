import type {
  ReplaceTeamLabGameObjectivesRequest,
  TeamLabGameBinding,
  TeamLabGameObjectiveWrite,
} from '../../api/teamlabGameAdminApi'
import type { TeamLabTopologyAsset } from '../../teamlab/api/teamlabContracts'

let draftSequence = 0

export interface GameObjectiveDraft extends TeamLabGameObjectiveWrite {
  clientId: string
  persistedDynamic: boolean | null
}

export function objectivesFromBinding(binding: TeamLabGameBinding): GameObjectiveDraft[] {
  return [...binding.objectives]
    .sort((left, right) => left.orderIndex - right.orderIndex || left.id - right.id)
    .map((objective) => ({
      ...objective,
      clientId: `objective-${objective.id}`,
      persistedDynamic: objective.dynamic,
      staticFlag: null,
      flagTemplate: null,
    }))
}

export function createObjectiveDraft(
  assets: readonly TeamLabTopologyAsset[],
  existing: readonly GameObjectiveDraft[]
): GameObjectiveDraft {
  let ordinal = existing.length + 1
  let key = `objective-${ordinal}`
  const keys = new Set(existing.map((item) => item.key))
  while (keys.has(key)) key = `objective-${++ordinal}`
  return {
    clientId: `draft-${++draftSequence}`,
    persistedDynamic: null,
    key,
    assetKey: assets[0]?.key ?? '',
    title: `得分目标 ${ordinal}`,
    description: null,
    category: 'General',
    score: 100,
    dynamic: true,
    staticFlag: null,
    flagTemplate: null,
    maxAttempts: 0,
    visible: true,
    checkpoint: false,
    prerequisiteKeys: [],
    orderIndex: existing.length,
  }
}

export function validateObjectiveDrafts(
  objectives: readonly GameObjectiveDraft[],
  assets: readonly TeamLabTopologyAsset[],
  maxResetCount: number
): string | null {
  if (!Number.isInteger(maxResetCount) || maxResetCount < 0 || maxResetCount > 100)
    return '最大重置次数必须是 0 到 100 之间的整数。'
  if (objectives.length > 256) return '单场比赛最多配置 256 个得分目标。'

  const assetKeys = new Set(assets.map((asset) => asset.key))
  const objectiveKeys = new Set<string>()
  for (const objective of objectives) {
    const key = objective.key.trim()
    if (!key || key.length > 63) return '目标标识不能为空，且不能超过 63 个字符。'
    if (objectiveKeys.has(key)) return `目标标识“${key}”重复。`
    objectiveKeys.add(key)
    if (!assetKeys.has(objective.assetKey)) return `目标“${key}”尚未绑定有效资产。`
    if (!objective.title.trim() || objective.title.trim().length > 128)
      return `目标“${key}”的标题不能为空，且不能超过 128 个字符。`
    if (objective.description && objective.description.trim().length > 1024)
      return `目标“${key}”的说明不能超过 1024 个字符。`
    if (!objective.category.trim() || objective.category.trim().length > 64)
      return `目标“${key}”的分类不能为空，且不能超过 64 个字符。`
    if (!Number.isInteger(objective.score) || objective.score < 0)
      return `目标“${key}”的分数必须是非负整数。`
    if (!Number.isInteger(objective.maxAttempts) || objective.maxAttempts < 0)
      return `目标“${key}”的提交次数必须是非负整数。`
    if (!objective.dynamic && objective.persistedDynamic !== false && !objective.staticFlag?.trim())
      return `目标“${key}”切换为静态 Flag 时必须填写 Flag。`
  }

  for (const objective of objectives) {
    if (objective.prerequisiteKeys.some((key) => key === objective.key || !objectiveKeys.has(key)))
      return `目标“${objective.key}”包含无效的前置目标。`
  }
  return null
}

export function toReplaceObjectivesRequest(
  objectives: readonly GameObjectiveDraft[],
  maxResetCount: number,
  revision: number
): ReplaceTeamLabGameObjectivesRequest {
  return {
    revision,
    maxResetCount,
    objectives: objectives.map(({ clientId: _clientId, persistedDynamic: _persistedDynamic, ...objective }, index) => ({
      ...objective,
      key: objective.key.trim(),
      assetKey: objective.assetKey.trim(),
      title: objective.title.trim(),
      description: objective.description?.trim() || null,
      category: objective.category.trim(),
      staticFlag: objective.staticFlag?.trim() || null,
      flagTemplate: objective.flagTemplate?.trim() || null,
      prerequisiteKeys: [...objective.prerequisiteKeys],
      orderIndex: index,
    })),
  }
}
