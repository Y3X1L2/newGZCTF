export interface IRChallengeSummary {
  id: number; title: string; category: string;
  isEnabled: boolean; originalScore: number;
  checkpointCount: number; osType: 'Linux' | 'Windows';
}

export interface CheckpointData {
  orderIndex: number; description: string;
  verificationType: 'AutoCommand' | 'AutoScript' | 'ManualAnswer' | 'ManualReview';
  verificationConfig: string; score: number; isRequired: boolean;
}

export interface CheckpointInfo extends CheckpointData {
  id: number; completed: boolean; verifiedAt?: string;
}

export interface IRInstanceStatus {
  instanceId: string; challengeId: number; status: string;
  remainingTime: string; totalScore: number;
  accessDetails?: {
    guacamoleConnectionUrl?: string;
    sshHost?: string; sshPort?: number;
    sshUsername?: string; sshCredential?: string;
  };
  checkpoints: CheckpointInfo[];
}
