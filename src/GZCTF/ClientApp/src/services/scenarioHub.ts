import { HubConnectionBuilder, HubConnection, LogLevel } from '@microsoft/signalr'

const HUB_URL = '/hubs/scenario'

export interface StageUnlockedPayload {
  instanceId: string
  stageId: number
  title: string
  accessInfo?: string
}

export interface TimeWarningPayload {
  instanceId: string
  remainingMinutes: number
}

export interface ScoreUpdatedPayload {
  instanceId: string
  totalScore: number
  detailScores: Record<string, number>
}

export interface EnvironmentReadyPayload {
  instanceId: string
  accessDetails: Record<string, unknown>
}

export interface CheckpointCompletedPayload {
  instanceId: string
  checkpointId: number
  score: number
}

export interface LeaderboardUpdatedPayload {
  challengeId: number
  entries: LeaderboardEntry[]
}

export interface LeaderboardEntry {
  rank: number
  userId: string
  userName: string
  totalScore: number
  detailScores: Record<string, number>
}

class ScenarioHubService {
  private connection: HubConnection | null = null

  async connect(): Promise<void> {
    if (this.connection?.state === 'Connected') return

    this.connection = new HubConnectionBuilder()
      .withUrl(HUB_URL)
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build()

    this.connection.onreconnecting(() => console.warn('[ScenarioHub] Reconnecting...'))
    this.connection.onreconnected(() => console.log('[ScenarioHub] Reconnected'))
    this.connection.onclose(() => console.log('[ScenarioHub] Disconnected'))

    await this.connection.start()
    console.log('[ScenarioHub] Connected')
  }

  async disconnect(): Promise<void> {
    if (this.connection) {
      await this.connection.stop()
      this.connection = null
    }
  }

  async joinScenario(scenarioId: string): Promise<void> {
    await this.ensureConnected()
    await this.connection!.invoke('JoinScenarioGroup', scenarioId)
  }

  async leaveScenario(scenarioId: string): Promise<void> {
    await this.ensureConnected()
    await this.connection!.invoke('LeaveScenarioGroup', scenarioId)
  }

  async joinIR(irId: string): Promise<void> {
    await this.ensureConnected()
    await this.connection!.invoke('JoinIRGroup', irId)
  }

  async leaveIR(irId: string): Promise<void> {
    await this.ensureConnected()
    await this.connection!.invoke('LeaveIRGroup', irId)
  }

  onStageUnlocked(callback: (payload: StageUnlockedPayload) => void): void {
    this.connection?.on('StageUnlocked', callback)
  }

  onTimeWarning(callback: (payload: TimeWarningPayload) => void): void {
    this.connection?.on('TimeWarning', callback)
  }

  onScoreUpdated(callback: (payload: ScoreUpdatedPayload) => void): void {
    this.connection?.on('ScoreUpdated', callback)
  }

  onEnvironmentReady(callback: (payload: EnvironmentReadyPayload) => void): void {
    this.connection?.on('EnvironmentReady', callback)
  }

  onCheckpointCompleted(callback: (payload: CheckpointCompletedPayload) => void): void {
    this.connection?.on('CheckpointCompleted', callback)
  }

  onEnvironmentResetComplete(callback: (instanceId: string) => void): void {
    this.connection?.on('EnvironmentResetComplete', callback)
  }

  onLeaderboardUpdated(callback: (payload: LeaderboardUpdatedPayload) => void): void {
    this.connection?.on('LeaderboardUpdated', callback)
  }

  offStageUnlocked(callback: (payload: StageUnlockedPayload) => void): void {
    this.connection?.off('StageUnlocked', callback)
  }

  offTimeWarning(callback: (payload: TimeWarningPayload) => void): void {
    this.connection?.off('TimeWarning', callback)
  }

  offScoreUpdated(callback: (payload: ScoreUpdatedPayload) => void): void {
    this.connection?.off('ScoreUpdated', callback)
  }

  offEnvironmentReady(callback: (payload: EnvironmentReadyPayload) => void): void {
    this.connection?.off('EnvironmentReady', callback)
  }

  offCheckpointCompleted(callback: (payload: CheckpointCompletedPayload) => void): void {
    this.connection?.off('CheckpointCompleted', callback)
  }

  offLeaderboardUpdated(callback: (payload: LeaderboardUpdatedPayload) => void): void {
    this.connection?.off('LeaderboardUpdated', callback)
  }

  private async ensureConnected(): Promise<void> {
    if (!this.connection || this.connection.state !== 'Connected') {
      await this.connect()
    }
  }
}

export const scenarioHub = new ScenarioHubService()
