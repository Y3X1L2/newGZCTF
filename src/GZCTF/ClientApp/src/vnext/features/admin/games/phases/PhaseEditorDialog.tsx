import { useEffect, useState } from 'react'
import { GameInfoModel } from '@Api'
import { TextField, ToggleField } from '../../../../shared/FormControls'
import { ActionButton, InlineFeedback, VNextDialog } from '../../../../shared/Interaction'
import { errorMessage } from '../../../../shared/errors'
import { AdminGamePhase, gameOperationsAdminApi } from '../../api'
import styles from '../GameOperations.module.css'
import { toLocalDateTimeInput } from '../gamePresentation'
import {
  emptyPhaseEditorDraft,
  phaseEditorDraft,
  phaseWritePayload,
  validatePhaseDraft,
} from './phaseModel'

export function PhaseEditorDialog({
  game,
  phases,
  phase,
  open,
  onClose,
  onSaved,
}: {
  game: GameInfoModel
  phases: AdminGamePhase[]
  phase: AdminGamePhase | null
  open: boolean
  onClose: () => void
  onSaved: () => Promise<unknown>
}) {
  const [draft, setDraft] = useState(() => phase ? phaseEditorDraft(phase) : emptyPhaseEditorDraft(game, phases))
  const [saving, setSaving] = useState(false)
  const [feedback, setFeedback] = useState<string | null>(null)

  useEffect(() => {
    if (!open) return
    setDraft(phase ? phaseEditorDraft(phase) : emptyPhaseEditorDraft(game, phases))
    setFeedback(null)
    setSaving(false)
  }, [game, open, phase, phases])

  const save = async () => {
    const issues = validatePhaseDraft(draft, game, phases, phase?.id)
    if (issues.length) {
      setFeedback(issues.join(' '))
      return
    }
    setSaving(true)
    setFeedback(null)
    try {
      if (phase) await gameOperationsAdminApi.updatePhase(game.id as number, phase.id, phaseWritePayload(draft))
      else await gameOperationsAdminApi.createPhase(game.id as number, phaseWritePayload(draft))
      await onSaved()
      onClose()
    } catch (requestError) {
      setFeedback(errorMessage(requestError, `阶段${phase ? '保存' : '创建'}失败。`))
    } finally {
      setSaving(false)
    }
  }

  return (
    <VNextDialog
      description="阶段时间必须位于比赛时间内，且不能与其他阶段重叠。"
      eyebrow="GAME PHASE"
      footer={
        <>
          <ActionButton disabled={saving} onClick={onClose} type="button">取消</ActionButton>
          <ActionButton disabled={saving} onClick={() => void save()} tone="primary" type="button">{saving ? '正在保存' : phase ? '保存阶段' : '创建阶段'}</ActionButton>
        </>
      }
      onClose={() => { if (!saving) onClose() }}
      open={open}
      title={phase ? `编辑 ${phase.name}` : '新建比赛阶段'}
    >
      <div className={styles.formStack}>
        {feedback ? <InlineFeedback tone="danger">{feedback}</InlineFeedback> : null}
        <TextField label="阶段名称" maxLength={256} onValueChange={(value) => setDraft((current) => ({ ...current, name: value }))} required value={draft.name} />
        <div className={styles.fieldGrid}>
          <TextField label="开始时间" min={toLocalDateTimeInput(game.start)} onValueChange={(value) => setDraft((current) => ({ ...current, start: value }))} required type="datetime-local" value={draft.start} />
          <TextField label="结束时间" max={toLocalDateTimeInput(game.end)} min={draft.start} onValueChange={(value) => setDraft((current) => ({ ...current, end: value }))} required type="datetime-local" value={draft.end} />
        </div>
        <ToggleField checked={draft.ctfEnabled} description="当前服务端阶段模型只支持控制 CTF 模块。" label="启用 CTF 操作" onChange={(checked) => setDraft((current) => ({ ...current, ctfEnabled: checked }))} />
      </div>
    </VNextDialog>
  )
}
