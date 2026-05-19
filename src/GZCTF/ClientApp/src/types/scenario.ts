export interface ScenarioStageInfo {
  id: number; orderIndex: number;
  title: string; skillDescription: string;
  status: 'locked' | 'unlocked' | 'completed';
}

export interface ScenarioInstanceStatus {
  instanceId: string; scenarioId: number;
  currentStageId: number; stages: ScenarioStageInfo[];
  timeRemaining: string; totalScore: number;
}

export interface StageSubmitResult {
  isCorrect: boolean; stageId: number;
  instanceStatus: string; currentStageId: number;
}
