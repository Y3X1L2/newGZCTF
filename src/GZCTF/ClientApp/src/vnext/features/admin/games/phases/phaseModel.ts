import { GameInfoModel } from '@Api'
import type { AdminGamePhase, AdminGamePhaseWrite } from '../../api'
import { fromLocalDateTimeInput, toLocalDateTimeInput } from '../gamePresentation'

export interface PhaseEditorDraft {
  name: string
  start: string
  end: string
  ctfEnabled: boolean
}

export function phaseEditorDraft(phase: AdminGamePhase): PhaseEditorDraft {
  return {
    name: phase.name,
    start: toLocalDateTimeInput(phase.startTime),
    end: toLocalDateTimeInput(phase.endTime),
    ctfEnabled: phase.ctfEnabled,
  }
}

export function emptyPhaseEditorDraft(game: Pick<GameInfoModel, 'start' | 'end'>, phases: AdminGamePhase[]) {
  const latestEnd = phases.reduce((latest, phase) => Math.max(latest, phase.endTime), game.start)
  const start = latestEnd < game.end ? latestEnd : game.start
  const end = Math.min(game.end, start + 60 * 60_000)
  return {
    name: '',
    start: toLocalDateTimeInput(start),
    end: toLocalDateTimeInput(end > start ? end : game.end),
    ctfEnabled: true,
  }
}

export function phaseWritePayload(draft: PhaseEditorDraft): AdminGamePhaseWrite {
  return {
    name: draft.name.trim(),
    startTime: fromLocalDateTimeInput(draft.start),
    endTime: fromLocalDateTimeInput(draft.end),
    ctfEnabled: draft.ctfEnabled,
  }
}

export function validatePhaseDraft(
  draft: PhaseEditorDraft,
  game: Pick<GameInfoModel, 'start' | 'end'>,
  phases: AdminGamePhase[],
  editingId?: number
) {
  const issues: string[] = []
  const payload = phaseWritePayload(draft)
  if (!payload.name) issues.push('请输入阶段名称。')
  if (payload.name.length > 256) issues.push('阶段名称不能超过 256 个字符。')
  if (!Number.isFinite(payload.startTime) || !Number.isFinite(payload.endTime)) issues.push('请输入有效的阶段时间。')
  else if (payload.endTime <= payload.startTime) issues.push('阶段结束时间必须晚于开始时间。')
  else {
    if (payload.startTime < game.start || payload.endTime > game.end) issues.push('阶段时间必须位于比赛开始和结束时间之间。')
    const overlapping = phases.find(
      (phase) => phase.id !== editingId && payload.startTime < phase.endTime && payload.endTime > phase.startTime
    )
    if (overlapping) issues.push(`阶段时间与“${overlapping.name}”重叠。`)
  }
  return issues
}

export type PhaseLifecycle = 'scheduled' | 'active' | 'ended'

export function phaseLifecycle(phase: Pick<AdminGamePhase, 'startTime' | 'endTime'>, now = Date.now()): PhaseLifecycle {
  if (phase.startTime > now) return 'scheduled'
  if (phase.endTime <= now) return 'ended'
  return 'active'
}

export function phaseDurationLabel(phase: Pick<AdminGamePhase, 'startTime' | 'endTime'>) {
  const totalMinutes = Math.max(0, Math.round((phase.endTime - phase.startTime) / 60_000))
  const days = Math.floor(totalMinutes / 1440)
  const hours = Math.floor((totalMinutes % 1440) / 60)
  const minutes = totalMinutes % 60
  return [days ? `${days}天` : '', hours ? `${hours}小时` : '', minutes || (!days && !hours) ? `${minutes}分钟` : '']
    .filter(Boolean)
    .join(' ')
}
