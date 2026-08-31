import { Clipboard, Download, Save, Trash2, Upload } from 'lucide-react'
import { FormEvent, useEffect, useMemo, useState } from 'react'
import { useNavigate, useOutletContext } from 'react-router'
import { GameInfoModel, GameType } from '@Api'
import { FileField, SelectField, TextAreaField, TextField, ToggleField } from '../../../shared/FormControls'
import { ActionButton, InlineFeedback, VNextConfirmDialog } from '../../../shared/Interaction'
import { MarkdownContent } from '../../../shared/MarkdownContent'
import { GeometricPoster } from '../../../shared/Primitives'
import { errorMessage } from '../../../shared/errors'
import { useVNextPageTitle } from '../../../shared/useVNextPageTitle'
import { gameAdminApi } from '../api'
import {
  AdminEditorActionBar,
  AdminEditorSection,
  AdminPageHeader,
} from '../shared/AdminWorkbench'
import type { GameAdminOutletContext } from './GameAdminShell'
import { fromLocalDateTimeInput, gameTypeLabel, toLocalDateTimeInput } from './gamePresentation'
import styles from './AdminGameInfoPage.module.css'

interface GameTimeInputs {
  start: string
  end: string
  writeupDeadline: string
}

function gameTimeInputs(game: GameInfoModel): GameTimeInputs {
  return {
    start: toLocalDateTimeInput(game.start),
    end: toLocalDateTimeInput(game.end),
    writeupDeadline: toLocalDateTimeInput(game.writeupDeadline || game.end),
  }
}

function applyGameTimeInputs(game: GameInfoModel, inputs: GameTimeInputs): GameInfoModel {
  return {
    ...game,
    start: fromLocalDateTimeInput(inputs.start),
    end: fromLocalDateTimeInput(inputs.end),
    writeupDeadline: game.writeupRequired
      ? fromLocalDateTimeInput(inputs.writeupDeadline)
      : game.writeupDeadline,
  }
}

function normalizedGame(game: GameInfoModel): GameInfoModel {
  return {
    ...game,
    title: game.title.trim(),
    summary: game.summary?.trim() ?? '',
    content: game.content ?? '',
    inviteCode: game.inviteCode?.trim() || null,
    teamMemberCountLimit: Math.max(0, Number(game.teamMemberCountLimit) || 0),
    containerCountLimit: Math.max(0, Number(game.containerCountLimit) || 0),
    writeupNote: game.writeupNote ?? '',
    bloodBonus: Math.max(0, Number(game.bloodBonus) || 0),
  }
}

function triggerDownload(blob: Blob, fileName: string) {
  const url = URL.createObjectURL(blob)
  const anchor = document.createElement('a')
  anchor.href = url
  anchor.download = fileName
  anchor.click()
  window.setTimeout(() => URL.revokeObjectURL(url), 0)
}

export function AdminGameInfoPage() {
  const navigate = useNavigate()
  const { game, mutateGame } = useOutletContext<GameAdminOutletContext>()
  const teamLabOnly = game.gameType === GameType.Penetration
  const [draft, setDraft] = useState<GameInfoModel>(() => ({ ...game }))
  const [timeInputs, setTimeInputs] = useState<GameTimeInputs>(() => gameTimeInputs(game))
  const [posterFile, setPosterFile] = useState<File | null>(null)
  const [saving, setSaving] = useState(false)
  const [auxiliaryPending, setAuxiliaryPending] = useState(false)
  const [deleteOpen, setDeleteOpen] = useState(false)
  const [feedback, setFeedback] = useState<{ tone: 'success' | 'danger' | 'neutral'; message: string } | null>(null)

  useVNextPageTitle(`管理 ${game.title}`)

  useEffect(() => {
    setDraft({ ...game })
    setTimeInputs(gameTimeInputs(game))
    setPosterFile(null)
  }, [game])

  const dirty = useMemo(
    () => JSON.stringify(normalizedGame(applyGameTimeInputs(draft, timeInputs))) !== JSON.stringify(normalizedGame(game)),
    [draft, game, timeInputs]
  )

  useEffect(() => {
    if (!dirty) return undefined
    const warn = (event: BeforeUnloadEvent) => event.preventDefault()
    window.addEventListener('beforeunload', warn)
    return () => window.removeEventListener('beforeunload', warn)
  }, [dirty])

  const update = <Key extends keyof GameInfoModel>(field: Key, value: GameInfoModel[Key]) => {
    setDraft((current) => ({ ...current, [field]: value }))
  }

  const save = async (event?: FormEvent) => {
    event?.preventDefault()
    const payload = normalizedGame(applyGameTimeInputs(draft, timeInputs))
    if (!payload.title) {
      setFeedback({ tone: 'danger', message: '请输入比赛名称。' })
      return
    }
    if (!Number.isFinite(payload.start) || !Number.isFinite(payload.end)) {
      setFeedback({ tone: 'danger', message: '请输入有效的比赛开始和结束时间。' })
      return
    }
    if (payload.end <= payload.start) {
      setFeedback({ tone: 'danger', message: '比赛结束时间必须晚于开始时间。' })
      return
    }
    if (payload.writeupRequired && !Number.isFinite(payload.writeupDeadline)) {
      setFeedback({ tone: 'danger', message: '请输入有效的 Writeup 截止时间。' })
      return
    }
    setSaving(true)
    setFeedback(null)
    try {
      await gameAdminApi.update(game.id as number, payload)
      await mutateGame()
      setFeedback({ tone: 'success', message: '比赛信息已保存。' })
    } catch (requestError) {
      setFeedback({ tone: 'danger', message: errorMessage(requestError, '比赛信息保存失败。') })
    } finally {
      setSaving(false)
    }
  }

  const uploadPoster = async () => {
    if (!posterFile || !game.id) return
    if (posterFile.size > 3 * 1024 * 1024) {
      setFeedback({ tone: 'danger', message: '比赛海报不能超过 3 MB。' })
      return
    }
    setAuxiliaryPending(true)
    setFeedback(null)
    try {
      await gameAdminApi.uploadPoster(game.id, posterFile)
      await mutateGame()
      setPosterFile(null)
      setFeedback({ tone: 'success', message: '比赛海报已更新。' })
    } catch (requestError) {
      setFeedback({ tone: 'danger', message: errorMessage(requestError, '比赛海报上传失败。') })
    } finally {
      setAuxiliaryPending(false)
    }
  }

  const exportGame = async () => {
    if (!game.id) return
    setAuxiliaryPending(true)
    setFeedback(null)
    try {
      const result = await gameAdminApi.exportGame(game.id, game.title)
      triggerDownload(result.blob, result.fileName)
      setFeedback({ tone: 'success', message: '比赛导出包已生成。' })
    } catch (requestError) {
      setFeedback({ tone: 'danger', message: errorMessage(requestError, '比赛导出失败。') })
    } finally {
      setAuxiliaryPending(false)
    }
  }

  const copyPublicKey = async () => {
    if (!game.publicKey) return
    try {
      await navigator.clipboard.writeText(game.publicKey)
      setFeedback({ tone: 'success', message: '比赛公钥已复制。' })
    } catch {
      setFeedback({ tone: 'danger', message: '浏览器拒绝访问剪贴板。' })
    }
  }

  const remove = async () => {
    if (!game.id) return false
    setAuxiliaryPending(true)
    setFeedback(null)
    try {
      await gameAdminApi.remove(game.id)
      navigate('/admin/games', { replace: true })
      return true
    } catch (requestError) {
      setFeedback({ tone: 'danger', message: errorMessage(requestError, '比赛删除失败。') })
      return false
    } finally {
      setAuxiliaryPending(false)
    }
  }

  return (
    <div className={styles.page}>
      <AdminPageHeader
        actions={
          <>
            <ActionButton disabled={auxiliaryPending} icon={<Clipboard size={16} />} onClick={() => void copyPublicKey()} type="button">复制公钥</ActionButton>
            <ActionButton disabled={auxiliaryPending} icon={<Download size={16} />} onClick={() => void exportGame()} type="button">导出比赛</ActionButton>
          </>
        }
        description="维护比赛身份、时间、报名规则、运行限制、Writeup 和选手可见内容。"
        eyebrow="GAME CONFIGURATION"
        title="比赛信息"
      />
      {feedback ? <InlineFeedback tone={feedback.tone === 'neutral' ? undefined : feedback.tone}>{feedback.message}</InlineFeedback> : null}
      <form className={styles.form} onSubmit={(event) => void save(event)}>
        <AdminEditorSection description="名称、赛制和可见性决定比赛在选手端的入口与后续管理模块。" title="比赛身份">
          <div className={styles.stack}>
            <div className={styles.fieldGrid}>
              <TextField label="比赛名称" maxLength={128} onValueChange={(value) => update('title', value)} required value={draft.title} />
              <SelectField label="比赛赛制" onValueChange={(value) => update('gameType', value as GameType)} value={draft.gameType ?? GameType.Jeopardy}>
                {Object.values(GameType).map((type) => <option key={type} value={type}>{gameTypeLabel(type)}</option>)}
              </SelectField>
            </div>
            {draft.gameType !== game.gameType ? <InlineFeedback tone="danger">赛制已更改；已有提交、试卷、服务或渗透场景时，服务端可能拒绝保存。</InlineFeedback> : null}
            <div className={styles.toggleGrid}>
              <ToggleField checked={draft.hidden ?? false} description="隐藏后不会出现在选手赛事目录。" label="隐藏比赛" onChange={(checked) => update('hidden', checked)} />
              <ToggleField checked={draft.practiceMode ?? false} description="结束后仍允许已报名选手访问。" label="练习模式" onChange={(checked) => update('practiceMode', checked)} />
              <ToggleField checked={draft.isTest ?? false} description="标记内部测试或演示赛事。" label="测试比赛" onChange={(checked) => update('isTest', checked)} />
            </div>
          </div>
        </AdminEditorSection>

        <AdminEditorSection description="时间使用浏览器本地时区输入，提交时转换为绝对时间戳。" title="比赛时间">
          <div className={styles.fieldGrid}>
            <TextField label="开始时间" onValueChange={(value) => setTimeInputs((current) => ({ ...current, start: value }))} required type="datetime-local" value={timeInputs.start} />
            <TextField label="结束时间" min={timeInputs.start} onValueChange={(value) => setTimeInputs((current) => ({ ...current, end: value }))} required type="datetime-local" value={timeInputs.end} />
          </div>
        </AdminEditorSection>

        <AdminEditorSection description={teamLabOnly ? '队伍上限和邀请码仍适用于组网比赛；每支队伍的运行环境由 TeamLab 场景统一管理。' : '0 表示不限制；邀请码为空时不要求比赛邀请码。'} title="报名与运行限制">
          <div className={styles.stack}>
            <div className={styles.fieldGrid}>
              <TextField label="队伍人数上限" min={0} onValueChange={(value) => update('teamMemberCountLimit', Number(value))} type="number" value={draft.teamMemberCountLimit ?? 0} />
              {!teamLabOnly ? <TextField label="实例数量上限" min={0} onValueChange={(value) => update('containerCountLimit', Number(value))} type="number" value={draft.containerCountLimit ?? 0} /> : null}
              <TextField label="比赛邀请码" maxLength={32} onValueChange={(value) => update('inviteCode', value)} value={draft.inviteCode ?? ''} />
              {!teamLabOnly ? <TextField label="一二三血奖励" min={0} onValueChange={(value) => update('bloodBonus', Number(value))} type="number" value={draft.bloodBonus ?? 0} /> : null}
            </div>
            <ToggleField checked={draft.acceptWithoutReview ?? false} description="关闭时由管理员在报名审核页处理申请。" label="报名自动通过" onChange={(checked) => update('acceptWithoutReview', checked)} />
          </div>
        </AdminEditorSection>

        <AdminEditorSection description="摘要用于赛事卡片；完整说明与选手端使用同一 Markdown 渲染器。" title="选手可见内容">
          <div className={styles.stack}>
            <TextAreaField label="比赛摘要" maxLength={512} onValueChange={(value) => update('summary', value)} rows={4} value={draft.summary ?? ''} />
            <div className={styles.markdownGrid}>
              <TextAreaField label="比赛说明 Markdown" onValueChange={(value) => update('content', value)} rows={20} value={draft.content ?? ''} />
              <article className={styles.preview}><header>实时预览</header><MarkdownContent source={draft.content || '暂无比赛说明。'} /></article>
            </div>
          </div>
        </AdminEditorSection>

        <AdminEditorSection description="海报独立上传并立即生效，支持 PNG、JPEG 和 WebP，最大 3 MB。" title="比赛海报">
          <div className={styles.posterGrid}>
            <div className={styles.poster}><GeometricPoster alt={`${game.title} 海报`} src={game.poster} tone="green" /></div>
            <div className={styles.stack}>
              <FileField accept="image/png,image/jpeg,image/webp" hint={posterFile?.name} label="选择海报" onChange={setPosterFile} />
              <ActionButton disabled={!posterFile || auxiliaryPending} icon={<Upload size={16} />} onClick={() => void uploadPoster()} type="button">上传海报</ActionButton>
            </div>
          </div>
        </AdminEditorSection>

        <AdminEditorSection description="要求提交时，截止时间和说明会显示在选手 Writeup 工作流中。" title="Writeup">
          <div className={styles.stack}>
            <ToggleField checked={draft.writeupRequired ?? false} label="要求提交 Writeup" onChange={(checked) => update('writeupRequired', checked)} />
            {draft.writeupRequired ? (
              <div className={styles.stack}>
                <TextField label="Writeup 截止时间" min={timeInputs.end} onValueChange={(value) => setTimeInputs((current) => ({ ...current, writeupDeadline: value }))} type="datetime-local" value={timeInputs.writeupDeadline} />
                <TextAreaField label="Writeup 附加说明" onValueChange={(value) => update('writeupNote', value)} rows={5} value={draft.writeupNote ?? ''} />
              </div>
            ) : null}
          </div>
        </AdminEditorSection>

        <AdminEditorSection description="删除会移除比赛、题目和相关配置；已有业务事实时后端可能拒绝。" title="危险区">
          <div className={styles.dangerRow}>
            <div><strong>删除比赛</strong><p>此操作不可撤销，必须输入完整比赛名称确认。</p></div>
            <ActionButton disabled={auxiliaryPending} icon={<Trash2 size={16} />} onClick={() => setDeleteOpen(true)} tone="danger" type="button">删除比赛</ActionButton>
          </div>
        </AdminEditorSection>

        <AdminEditorActionBar status={dirty ? '有未保存更改。' : feedback?.message || '当前配置已与服务器同步。'}>
          <ActionButton disabled={saving || !dirty} icon={<Save size={17} />} tone="primary" type="submit">{saving ? '正在保存' : '保存比赛'}</ActionButton>
        </AdminEditorActionBar>
      </form>
      <VNextConfirmDialog confirmationText={game.title} description="删除后无法恢复。" message={`将永久删除比赛“${game.title}”及其配置。`} onClose={() => setDeleteOpen(false)} onConfirm={remove} open={deleteOpen} title="确认删除比赛" />
    </div>
  )
}
