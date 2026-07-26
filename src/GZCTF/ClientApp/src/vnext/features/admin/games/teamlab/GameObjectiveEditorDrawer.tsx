import { Save } from 'lucide-react'
import { useEffect, useState } from 'react'
import { PasswordField, SelectField, TextAreaField, TextField, ToggleField } from '../../../../shared/FormControls'
import { ActionButton, VNextDrawer } from '../../../../shared/Interaction'
import type { TeamLabTopologyAsset } from '../../teamlab/api/teamlabContracts'
import type { GameObjectiveDraft } from './gameObjectiveModel'
import styles from './TeamLabGame.module.css'

export function GameObjectiveEditorDrawer({
  objective,
  objectives,
  assets,
  open,
  onClose,
  onApply,
}: {
  objective: GameObjectiveDraft | null
  objectives: readonly GameObjectiveDraft[]
  assets: readonly TeamLabTopologyAsset[]
  open: boolean
  onClose: () => void
  onApply: (objective: GameObjectiveDraft) => void
}) {
  const [draft, setDraft] = useState<GameObjectiveDraft | null>(objective)

  useEffect(() => setDraft(objective), [objective])

  const ready = Boolean(draft?.key.trim() && draft.title.trim() && draft.assetKey)
  const update = (patch: Partial<GameObjectiveDraft>) => {
    setDraft((current) => current ? { ...current, ...patch } : current)
  }

  return (
    <VNextDrawer
      description={draft?.id ? `目标 #${draft.id}` : '新增目标'}
      eyebrow="SCORING OBJECTIVE"
      footer={
        <>
          <ActionButton onClick={onClose} type="button">取消</ActionButton>
          <ActionButton
            disabled={!draft || !ready}
            icon={<Save size={16} />}
            onClick={() => draft && onApply(draft)}
            tone="primary"
            type="button"
          >应用配置</ActionButton>
        </>
      }
      onClose={onClose}
      open={open}
      size="medium"
      title={draft?.title || '配置得分目标'}
    >
      {draft ? (
        <div className={styles.objectiveForm}>
          <div className={styles.objectiveFormGrid}>
            <TextField
              disabled={draft.id !== undefined}
              hint={draft.id ? '已创建目标的标识不可修改。' : undefined}
              label="目标标识"
              maxLength={63}
              onValueChange={(key) => update({ key })}
              required
              value={draft.key}
            />
            <SelectField label="绑定资产" onValueChange={(assetKey) => update({ assetKey })} required value={draft.assetKey}>
              <option value="">请选择资产</option>
              {assets.map((asset) => <option key={asset.key} value={asset.key}>{asset.name} ({asset.key})</option>)}
            </SelectField>
          </div>
          <TextField label="标题" maxLength={128} onValueChange={(title) => update({ title })} required value={draft.title} />
          <TextAreaField label="说明" maxLength={1024} onValueChange={(description) => update({ description: description || null })} value={draft.description ?? ''} />
          <div className={styles.objectiveFormGrid}>
            <TextField label="分类" maxLength={64} onValueChange={(category) => update({ category })} required value={draft.category} />
            <TextField label="分数" min={0} onValueChange={(value) => update({ score: Number(value) })} required type="number" value={draft.score} />
            <TextField label="最大提交次数" min={0} onValueChange={(value) => update({ maxAttempts: Number(value) })} required type="number" value={draft.maxAttempts} />
          </div>

          <div className={styles.objectiveToggleGroup}>
            <ToggleField checked={draft.dynamic} label="动态 Flag" onChange={(dynamic) => update({ dynamic })} />
            <ToggleField checked={draft.visible} label="选手可见" onChange={(visible) => update({ visible })} />
            <ToggleField checked={draft.checkpoint} label="关键检查点" onChange={(checkpoint) => update({ checkpoint })} />
          </div>

          {draft.dynamic ? (
            <TextField
              hint="留空使用 flag{[TEAM_HASH]}；支持 [TEAM_HASH] 或 [TOKEN]。"
              label="Flag 模板"
              onValueChange={(flagTemplate) => update({ flagTemplate })}
              placeholder={draft.persistedDynamic === true ? '留空保留当前模板' : 'flag{[TEAM_HASH]}'}
              value={draft.flagTemplate ?? ''}
            />
          ) : (
            <PasswordField
              hint={draft.persistedDynamic === false ? '留空保留当前静态 Flag。' : undefined}
              label="静态 Flag"
              maxLength={256}
              onValueChange={(staticFlag) => update({ staticFlag })}
              placeholder={draft.persistedDynamic === false ? '已配置' : 'flag{...}'}
              required={draft.persistedDynamic !== false}
              value={draft.staticFlag ?? ''}
            />
          )}

          <fieldset className={styles.prerequisiteFieldset}>
            <legend>前置目标</legend>
            {objectives.filter((item) => item.clientId !== draft.clientId).length ? (
              <div className={styles.prerequisiteList}>
                {objectives.filter((item) => item.clientId !== draft.clientId).map((item) => (
                  <label key={item.clientId}>
                    <input
                      checked={draft.prerequisiteKeys.includes(item.key)}
                      onChange={(event) => update({
                        prerequisiteKeys: event.currentTarget.checked
                          ? [...draft.prerequisiteKeys, item.key]
                          : draft.prerequisiteKeys.filter((key) => key !== item.key),
                      })}
                      type="checkbox"
                    />
                    <span>{item.title}</span>
                    <code>{item.key}</code>
                  </label>
                ))}
              </div>
            ) : <p className={styles.objectiveMuted}>暂无可选前置目标</p>}
          </fieldset>
        </div>
      ) : null}
    </VNextDrawer>
  )
}
