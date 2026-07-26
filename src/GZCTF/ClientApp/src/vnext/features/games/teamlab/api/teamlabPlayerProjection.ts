import type {
  TeamLabPlayerObjectiveProjection,
  TeamLabPlayerTargetProjection,
  TeamLabPlayerWorkspace,
  TeamLabPlayerWorkspaceProjection,
} from './teamlabPlayerContracts'

export function projectTeamLabPlayerWorkspace(
  workspace: TeamLabPlayerWorkspace
): TeamLabPlayerWorkspaceProjection {
  const solvedKeys = new Set(workspace.objectives.filter((item) => item.solved).map((item) => item.key))
  const objectives: TeamLabPlayerObjectiveProjection[] = workspace.objectives.map((item) => ({
    ...item,
    available: item.solved || item.prerequisiteKeys.every((key) => solvedKeys.has(key)),
    remainingAttempts: item.maxAttempts === 0 ? null : Math.max(0, item.maxAttempts - item.attempts),
  }))
  const targetsByAsset = new Map<string, TeamLabPlayerObjectiveProjection[]>()

  for (const item of objectives) {
    const targetObjectives = targetsByAsset.get(item.assetKey)
    if (targetObjectives) targetObjectives.push(item)
    else targetsByAsset.set(item.assetKey, [item])
  }

  const targets: TeamLabPlayerTargetProjection[] = Array.from(targetsByAsset, ([assetKey, targetObjectives]) => ({
    assetKey,
    solvedCount: targetObjectives.filter((item) => item.solved).length,
    objectiveCount: targetObjectives.length,
    totalScore: targetObjectives.reduce((sum, item) => sum + item.score, 0),
    objectives: targetObjectives,
  }))

  return {
    gameId: workspace.gameId,
    teamId: workspace.teamId,
    teamName: workspace.teamName,
    runtimeId: workspace.runtimeId,
    status: workspace.status,
    stage: workspace.stage,
    resetAllowance: {
      used: workspace.resetCount,
      limit: workspace.maxResetCount,
      remaining: Math.max(0, workspace.maxResetCount - workspace.resetCount),
    },
    solvedCount: objectives.filter((item) => item.solved).length,
    objectiveCount: objectives.length,
    totalScore: objectives.reduce((sum, item) => sum + item.score, 0),
    objectives,
    targets,
  }
}
