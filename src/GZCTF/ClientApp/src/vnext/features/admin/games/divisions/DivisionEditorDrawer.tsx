import { Dices, Plus, Trash2 } from 'lucide-react'
import { useEffect, useMemo, useState } from 'react'
import { ChallengeInfoModel, Division, GameInfoModel, GameType } from '@Api'
import { SelectField, TextField } from '../../../../shared/FormControls'
import { ActionButton, InlineFeedback, VNextDrawer, type DrawerRequestClose } from '../../../../shared/Interaction'
import { errorMessage } from '../../../../shared/errors'
import { gameOperationsAdminApi } from '../../api'
import styles from '../GameOperations.module.css'
import {
  divisionCreatePayload,
  divisionEditorDraft,
  divisionUpdatePayload,
  generateDivisionInviteCode,
  hasGamePermission,
  permissionOptions,
  toggleGamePermission,
  validateDivisionDraft,
} from './divisionModel'

function PermissionGrid({
  mask,
  challengeScoped,
  disabled,
  onChange,
}: {
  mask: number
  challengeScoped: boolean
  disabled: boolean
  onChange: (mask: number) => void
}) {
  const options = permissionOptions.filter((option) => option.challengeScoped === challengeScoped)
  return (
    <div className={styles.permissionGrid}>
      {options.map((option) => (
        <label className={styles.permissionOption} key={option.value}>
          <input
            checked={hasGamePermission(mask, option.value)}
            disabled={disabled}
            onChange={(event) => onChange(toggleGamePermission(mask, option.value, event.currentTarget.checked))}
            type="checkbox"
          />
          <span><strong>{option.label}</strong><small>{option.description}</small></span>
        </label>
      ))}
    </div>
  )
}

export function DivisionEditorDrawer({
  game,
  division,
  challenges,
  open,
  onClose,
  onSaved,
}: {
  game: GameInfoModel
  division: Division | null
  challenges: ChallengeInfoModel[]
  open: boolean
  onClose: () => void
  onSaved: () => Promise<unknown>
}) {
  const [draft, setDraft] = useState(() => divisionEditorDraft(division))
  const [selectedChallengeId, setSelectedChallengeId] = useState('')
  const [saving, setSaving] = useState(false)
  const [feedback, setFeedback] = useState<string | null>(null)
  const ctf = game.gameType === GameType.Jeopardy || game.gameType === GameType.Mixed

  useEffect(() => {
    if (!open) return
    setDraft(divisionEditorDraft(division))
    setSelectedChallengeId('')
    setSaving(false)
    setFeedback(null)
  }, [division, open])

  const challengeMap = useMemo(
    () => new Map(challenges.filter((challenge) => challenge.id).map((challenge) => [challenge.id as number, challenge])),
    [challenges]
  )
  const availableChallenges = useMemo(() => {
    const selected = new Set(draft.challengeConfigs.map((config) => config.challengeId))
    return challenges.filter((challenge) => challenge.id && !selected.has(challenge.id))
  }, [challenges, draft.challengeConfigs])

  const addOverride = () => {
    const challengeId = Number(selectedChallengeId)
    if (!Number.isInteger(challengeId) || challengeId <= 0) return
    setDraft((current) => ({
      ...current,
      challengeConfigs: [...current.challengeConfigs, { challengeId, permissions: current.defaultPermissions }],
    }))
    setSelectedChallengeId('')
  }

  const save = async (requestClose: DrawerRequestClose) => {
    const issues = validateDivisionDraft(draft)
    if (issues.length) {
      setFeedback(issues.join(' '))
      return
    }
    setSaving(true)
    setFeedback(null)
    try {
      if (division) await gameOperationsAdminApi.updateDivision(game.id as number, division.id, divisionUpdatePayload(draft))
      else await gameOperationsAdminApi.createDivision(game.id as number, divisionCreatePayload(draft))
      await onSaved()
      requestClose()
    } catch (requestError) {
      setFeedback(errorMessage(requestError, `赛区${division ? '保存' : '创建'}失败。`))
    } finally {
      setSaving(false)
    }
  }

  return (
    <VNextDrawer
      description="配置报名策略、总榜权限和 CTF 题目级权限覆盖。"
      eyebrow="GAME DIVISION"
      footer={(requestClose) => <><ActionButton disabled={saving} onClick={() => requestClose()} type="button">取消</ActionButton><ActionButton disabled={saving} onClick={() => void save(requestClose)} tone="primary" type="button">{saving ? '正在保存' : division ? '保存赛区' : '创建赛区'}</ActionButton></>}
      onClose={onClose}
      open={open}
      size="wide"
      title={division ? `编辑 ${division.name}` : '新建赛区'}
    >
      <div className={styles.drawerStack}>
        {feedback ? <InlineFeedback tone="danger">{feedback}</InlineFeedback> : null}
        <section className={styles.drawerSection}>
          <h3>赛区身份</h3>
          <p>邀请码为空时不要求赛区邀请码；比赛全局邀请码仍在比赛信息页维护。</p>
          <div className={styles.fieldGrid}>
            <TextField label="赛区名称" maxLength={31} onValueChange={(value) => setDraft((current) => ({ ...current, name: value }))} required value={draft.name} />
            <div className={styles.overrideToolbar}>
              <TextField label="赛区邀请码" maxLength={32} onValueChange={(value) => setDraft((current) => ({ ...current, inviteCode: value }))} value={draft.inviteCode} />
              <ActionButton icon={<Dices size={16} />} onClick={() => setDraft((current) => ({ ...current, inviteCode: generateDivisionInviteCode() }))} type="button">生成</ActionButton>
            </div>
          </div>
        </section>

        <section className={styles.drawerSection}>
          <h3>报名与排名权限</h3>
          <p>权限关闭后由后端在报名、排名和比赛访问流程中执行限制。</p>
          <PermissionGrid challengeScoped={false} disabled={saving} mask={draft.defaultPermissions} onChange={(defaultPermissions) => setDraft((current) => ({ ...current, defaultPermissions }))} />
        </section>

        {ctf ? (
          <section className={styles.drawerSection}>
            <h3>CTF 默认权限</h3>
            <p>这些权限适用于没有单独覆盖的所有 CTF 题目。</p>
            <PermissionGrid challengeScoped disabled={saving} mask={draft.defaultPermissions} onChange={(defaultPermissions) => setDraft((current) => ({ ...current, defaultPermissions }))} />
          </section>
        ) : null}

        {ctf ? (
          <section className={styles.drawerSection}>
            <h3>题目权限覆盖</h3>
            <p>只添加与默认策略不同的题目，避免产生难以维护的大量重复配置。</p>
            <div className={styles.overrideToolbar}>
              <SelectField label="选择题目" onValueChange={setSelectedChallengeId} value={selectedChallengeId}>
                <option value="">请选择题目</option>
                {availableChallenges.map((challenge) => <option key={challenge.id} value={challenge.id}>#{challenge.id} {challenge.title}</option>)}
              </SelectField>
              <ActionButton disabled={!selectedChallengeId} icon={<Plus size={16} />} onClick={addOverride} type="button">添加覆盖</ActionButton>
            </div>
            {draft.challengeConfigs.length ? (
              <div className={styles.overrideList}>
                {draft.challengeConfigs.map((config) => (
                  <div className={styles.overrideItem} key={config.challengeId}>
                    <div className={styles.overrideHeader}>
                      <strong>{challengeMap.get(config.challengeId)?.title ?? `未知题目 #${config.challengeId}`}</strong>
                      <button aria-label={`移除题目 #${config.challengeId} 权限覆盖`} className={styles.iconButton} data-danger onClick={() => setDraft((current) => ({ ...current, challengeConfigs: current.challengeConfigs.filter((item) => item.challengeId !== config.challengeId) }))} type="button"><Trash2 size={16} /></button>
                    </div>
                    <PermissionGrid challengeScoped disabled={saving} mask={config.permissions} onChange={(permissions) => setDraft((current) => ({ ...current, challengeConfigs: current.challengeConfigs.map((item) => item.challengeId === config.challengeId ? { ...item, permissions } : item) }))} />
                  </div>
                ))}
              </div>
            ) : <InlineFeedback>当前赛区全部题目使用默认权限。</InlineFeedback>}
          </section>
        ) : null}

        {!ctf ? <InlineFeedback>当前赛制不使用 CTF 题目权限覆盖，只保存报名和总体排名权限。</InlineFeedback> : null}
      </div>
    </VNextDrawer>
  )
}
