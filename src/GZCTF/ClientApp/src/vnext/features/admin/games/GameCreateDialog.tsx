import { ChevronLeft, ChevronRight, Plus } from 'lucide-react'
import { useEffect, useState } from 'react'
import { GameType } from '@Api'
import { SelectField, TextAreaField, TextField, ToggleField } from '../../../shared/FormControls'
import { ActionButton, InlineFeedback, VNextDialog } from '../../../shared/Interaction'
import { errorMessage } from '../../../shared/errors'
import { gameAdminApi } from '../api'
import { emptyGameCreateDraft, gameCreatePayload, gameTypeLabel, validateGameCreateDraft } from './gamePresentation'
import styles from './GameDialogs.module.css'

const steps = ['基本信息', '赛制', '报名与分组', '时间与确认']

export function GameCreateDialog({
  open,
  onClose,
  onCreated,
}: {
  open: boolean
  onClose: () => void
  onCreated: (gameId: number) => void
}) {
  const [step, setStep] = useState(0)
  const [draft, setDraft] = useState(emptyGameCreateDraft)
  const [saving, setSaving] = useState(false)
  const [failure, setFailure] = useState<string | null>(null)

  useEffect(() => {
    if (!open) return
    setStep(0)
    setDraft(emptyGameCreateDraft())
    setFailure(null)
  }, [open])

  const update = <Key extends keyof typeof draft>(field: Key, value: (typeof draft)[Key]) => {
    setDraft((current) => ({ ...current, [field]: value }))
  }

  const next = () => {
    if (step === 0 && !draft.title.trim()) {
      setFailure('请输入比赛名称。')
      return
    }
    setFailure(null)
    setStep((current) => Math.min(steps.length - 1, current + 1))
  }

  const create = async () => {
    const issues = validateGameCreateDraft(draft)
    if (issues.length) {
      setFailure(issues[0])
      return false
    }
    setSaving(true)
    setFailure(null)
    try {
      const game = await gameAdminApi.create(gameCreatePayload(draft))
      if (!game.id) throw new Error('比赛已创建，但服务器没有返回比赛编号。')
      onCreated(game.id)
      return true
    } catch (requestError) {
      setFailure(errorMessage(requestError, '比赛创建失败。'))
      return false
    } finally {
      setSaving(false)
    }
  }

  return (
    <VNextDialog
      description="新比赛以隐藏状态创建，完成题目、赛制和报名配置后再公开。"
      eyebrow="GAME CREATION"
      footer={
        <>
          <ActionButton disabled={saving} onClick={onClose} type="button">
            取消
          </ActionButton>
          {step > 0 ? (
            <ActionButton icon={<ChevronLeft size={16} />} onClick={() => setStep((current) => current - 1)} type="button">
              上一步
            </ActionButton>
          ) : null}
          {step < steps.length - 1 ? (
            <ActionButton icon={<ChevronRight size={16} />} onClick={next} tone="primary" type="button">
              下一步
            </ActionButton>
          ) : (
            <ActionButton disabled={saving} icon={<Plus size={16} />} onClick={() => void create()} tone="primary" type="button">
              {saving ? '正在创建' : '创建比赛'}
            </ActionButton>
          )}
        </>
      }
      onClose={onClose}
      open={open}
      title="创建比赛"
      wide
    >
      <ol className={styles.stepper}>
        {steps.map((label, index) => (
          <li data-active={index === step || undefined} data-complete={index < step || undefined} key={label}>
            <span>{index + 1}</span>
            {label}
          </li>
        ))}
      </ol>
      {failure ? <InlineFeedback tone="danger">{failure}</InlineFeedback> : null}
      <div className={styles.dialogBody}>
        {step === 0 ? (
          <div className={styles.stack}>
            <TextField
              autoFocus
              label="比赛名称"
              maxLength={128}
              onValueChange={(value) => update('title', value)}
              required
              value={draft.title}
            />
            <TextAreaField
              label="比赛摘要"
              maxLength={512}
              onValueChange={(value) => update('summary', value)}
              rows={5}
              value={draft.summary}
            />
          </div>
        ) : null}
        {step === 1 ? (
          <div className={styles.stack}>
            <SelectField
              hint="赛制决定后续管理入口；已产生业务数据后修改赛制可能被服务端拒绝。"
              label="比赛赛制"
              onValueChange={(value) => update('gameType', value as GameType)}
              value={draft.gameType}
            >
              {Object.values(GameType).map((type) => (
                <option key={type} value={type}>
                  {gameTypeLabel(type)}
                </option>
              ))}
            </SelectField>
            <div className={styles.toggleGrid}>
              <ToggleField
                checked={draft.hidden}
                description="隐藏比赛不会出现在选手赛事目录中。"
                label="创建后保持隐藏"
                onChange={(checked) => update('hidden', checked)}
              />
              <ToggleField
                checked={draft.practiceMode}
                description="比赛结束后仍允许已报名选手访问。"
                label="练习模式"
                onChange={(checked) => update('practiceMode', checked)}
              />
              <ToggleField
                checked={draft.isTest}
                description="用于内部测试和演示数据区分。"
                label="测试比赛"
                onChange={(checked) => update('isTest', checked)}
              />
            </div>
          </div>
        ) : null}
        {step === 2 ? (
          <div className={styles.stack}>
            <div className={styles.fieldGrid}>
              <TextField
                hint="0 表示不限制。"
                label="队伍人数上限"
                min={0}
                onValueChange={(value) => update('teamMemberCountLimit', Number(value))}
                type="number"
                value={draft.teamMemberCountLimit}
              />
              <TextField
                hint="每支队伍同时运行的题目实例数量。"
                label="实例数量上限"
                min={0}
                onValueChange={(value) => update('containerCountLimit', Number(value))}
                type="number"
                value={draft.containerCountLimit}
              />
              <TextField
                hint="留空表示不要求比赛邀请码，最长 32 个字符。"
                label="比赛邀请码"
                maxLength={32}
                onValueChange={(value) => update('inviteCode', value)}
                value={draft.inviteCode}
              />
            </div>
            <ToggleField
              checked={draft.acceptWithoutReview}
              description="关闭时，管理员需要在报名审核页处理申请。"
              label="报名自动通过"
              onChange={(checked) => update('acceptWithoutReview', checked)}
            />
          </div>
        ) : null}
        {step === 3 ? (
          <div className={styles.stack}>
            <div className={styles.fieldGrid}>
              <TextField
                label="开始时间"
                onValueChange={(value) => update('start', value)}
                required
                type="datetime-local"
                value={draft.start}
              />
              <TextField
                label="结束时间"
                min={draft.start}
                onValueChange={(value) => update('end', value)}
                required
                type="datetime-local"
                value={draft.end}
              />
            </div>
            <dl className={styles.summaryGrid}>
              <div><dt>比赛</dt><dd>{draft.title}</dd></div>
              <div><dt>赛制</dt><dd>{gameTypeLabel(draft.gameType)}</dd></div>
              <div><dt>可见性</dt><dd>{draft.hidden ? '隐藏' : '公开'}</dd></div>
              <div><dt>报名</dt><dd>{draft.acceptWithoutReview ? '自动通过' : '管理员审核'}</dd></div>
            </dl>
          </div>
        ) : null}
      </div>
    </VNextDialog>
  )
}
