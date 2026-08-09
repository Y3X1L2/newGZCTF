import { ArrowDown, ArrowUp, Edit3, Flag, Plus, Save, Trash2 } from 'lucide-react'
import { useEffect, useRef, useState } from 'react'
import { TextField } from '../../../../shared/FormControls'
import { ActionButton, InlineFeedback } from '../../../../shared/Interaction'
import { errorMessage } from '../../../../shared/errors'
import type {
  ReplaceTeamLabGameObjectivesRequest,
  TeamLabGameBinding,
  TeamLabGameRollout,
} from '../../api/teamlabGameAdminApi'
import type { TeamLabTopologyAsset } from '../../teamlab/api/teamlabContracts'
import { GameObjectiveEditorDrawer } from './GameObjectiveEditorDrawer'
import {
  createObjectiveDraft,
  objectivesFromBinding,
  toReplaceObjectivesRequest,
  validateObjectiveDrafts,
  type GameObjectiveDraft,
} from './gameObjectiveModel'
import styles from './TeamLabGame.module.css'

export function GameObjectiveBindingEditor({
  binding,
  rollout,
  assets,
  loading,
  onSave,
  onDirtyChange,
}: {
  binding: TeamLabGameBinding | null
  rollout: TeamLabGameRollout | null
  assets: readonly TeamLabTopologyAsset[]
  loading: boolean
  onSave: (request: ReplaceTeamLabGameObjectivesRequest) => Promise<TeamLabGameBinding>
  onDirtyChange?: (dirty: boolean) => void
}) {
  const [objectives, setObjectives] = useState<GameObjectiveDraft[]>(() => binding ? objectivesFromBinding(binding) : [])
  const [maxResetCount, setMaxResetCount] = useState(binding?.maxResetCount ?? 0)
  const [selected, setSelected] = useState<GameObjectiveDraft | null>(null)
  const [dirty, setDirty] = useState(false)
  const [saving, setSaving] = useState(false)
  const [feedback, setFeedback] = useState<{ tone: 'success' | 'danger'; message: string } | null>(null)
  const bindingVersion = binding
    ? `${binding.gameId}:${binding.topologyId}:${binding.objectiveRevision}`
    : null
  const sourceBindingVersion = useRef(bindingVersion)

  useEffect(() => onDirtyChange?.(dirty), [dirty, onDirtyChange])

  useEffect(() => {
    if (!binding) return
    const bindingChanged = sourceBindingVersion.current !== bindingVersion
    if (!bindingChanged) return
    sourceBindingVersion.current = bindingVersion
    setObjectives(objectivesFromBinding(binding))
    setMaxResetCount(binding.maxResetCount)
    if (bindingChanged) {
      setDirty(false)
      setSelected(null)
      setFeedback(null)
    }
  }, [bindingVersion])

  if (!binding) return null

  const locked = Boolean(rollout && rollout.status !== 'completed')
  const interactionLocked = locked || saving
  const changed = (next: GameObjectiveDraft[]) => {
    setObjectives(next)
    setDirty(true)
    setFeedback(null)
  }
  const move = (index: number, offset: -1 | 1) => {
    const destination = index + offset
    if (destination < 0 || destination >= objectives.length) return
    const next = [...objectives]
    ;[next[index], next[destination]] = [next[destination], next[index]]
    changed(next)
  }
  const save = async () => {
    const validation = validateObjectiveDrafts(objectives, assets, maxResetCount)
    if (validation) {
      setFeedback({ tone: 'danger', message: validation })
      return
    }
    setSaving(true)
    setFeedback(null)
    try {
      const updated = await onSave(toReplaceObjectivesRequest(
        objectives, maxResetCount, binding.objectiveRevision))
      setObjectives(objectivesFromBinding(updated))
      setMaxResetCount(updated.maxResetCount)
      setDirty(false)
      setFeedback({ tone: 'success', message: '得分目标已保存。' })
    } catch (error) {
      setFeedback({ tone: 'danger', message: errorMessage(error, '得分目标保存失败。') })
    } finally {
      setSaving(false)
    }
  }

  return (
    <section className={styles.objectiveSection} aria-labelledby="teamlab-objectives-title">
      <header className={styles.sectionHeader}>
        <div><span>SCORING CONTRACT</span><h2 id="teamlab-objectives-title">得分目标</h2></div>
        <div className={styles.objectiveHeaderActions}>
          {dirty ? <span className={styles.unsavedMark}>有未保存修改</span> : null}
          <ActionButton
            disabled={interactionLocked || loading || assets.length === 0}
            icon={<Plus size={16} />}
            onClick={() => setSelected(createObjectiveDraft(assets, objectives))}
            type="button"
          >添加目标</ActionButton>
          <ActionButton disabled={locked || loading || saving || !dirty} icon={<Save size={16} />} onClick={() => void save()} tone="primary" type="button">
            {saving ? '保存中' : '保存配置'}
          </ActionButton>
        </div>
      </header>

      {locked ? <InlineFeedback>当前批次已开始，结束并清理后才能修改得分目标。</InlineFeedback> : null}
      {feedback ? <InlineFeedback tone={feedback.tone}>{feedback.message}</InlineFeedback> : null}

      <div className={styles.objectiveSettings}>
        <TextField
          disabled={interactionLocked}
          label="每队最大重置次数"
          max={100}
          min={0}
          onValueChange={(value) => { setMaxResetCount(Number(value)); setDirty(true); setFeedback(null) }}
          type="number"
          value={maxResetCount}
        />
        <div className={styles.objectiveTotal}><Flag size={17} /><strong>{objectives.length}</strong><span>个目标</span><strong>{objectives.reduce((sum, item) => sum + item.score, 0)}</strong><span>总分</span></div>
      </div>

      {objectives.length ? (
        <div className={styles.objectiveList}>
          {objectives.map((objective, index) => (
            <article className={styles.objectiveRow} key={objective.clientId}>
              <div className={styles.objectiveOrder}>{index + 1}</div>
              <div className={styles.objectiveIdentity}>
                <strong>{objective.title}</strong>
                <span><code>{objective.key}</code><span>{objective.category}</span><span>{objective.assetKey}</span></span>
              </div>
              <div className={styles.objectiveFacts}>
                <strong>{objective.score}</strong><span>分</span>
                <small>{objective.dynamic ? '动态 Flag' : '静态 Flag'}</small>
                {!objective.visible ? <small>已隐藏</small> : null}
              </div>
              <div className={styles.objectiveRowActions}>
                <button aria-label={`上移 ${objective.title}`} disabled={interactionLocked || index === 0} onClick={() => move(index, -1)} title="上移" type="button"><ArrowUp size={15} /></button>
                <button aria-label={`下移 ${objective.title}`} disabled={interactionLocked || index === objectives.length - 1} onClick={() => move(index, 1)} title="下移" type="button"><ArrowDown size={15} /></button>
                <button aria-label={`编辑 ${objective.title}`} disabled={interactionLocked} onClick={() => setSelected(objective)} title="编辑" type="button"><Edit3 size={15} /></button>
                <button aria-label={`删除 ${objective.title}`} disabled={interactionLocked} onClick={() => changed(objectives.filter((item) => item.clientId !== objective.clientId))} title="删除" type="button"><Trash2 size={15} /></button>
              </div>
            </article>
          ))}
        </div>
      ) : (
        <div className={styles.objectiveEmpty}><Flag size={20} /><span>尚未配置得分目标</span></div>
      )}

      <GameObjectiveEditorDrawer
        assets={assets}
        objective={selected}
        objectives={objectives}
        onApply={(objective) => {
          const exists = objectives.some((item) => item.clientId === objective.clientId)
          changed(exists
            ? objectives.map((item) => item.clientId === objective.clientId ? objective : item)
            : [...objectives, objective])
          setSelected(null)
        }}
        onClose={() => setSelected(null)}
        open={selected !== null}
      />
    </section>
  )
}
