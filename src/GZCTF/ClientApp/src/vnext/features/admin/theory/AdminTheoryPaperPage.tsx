import { ArrowDown, ArrowUp, Dice5, Plus, Save, Send, Trash2 } from 'lucide-react'
import { useEffect, useMemo, useState } from 'react'
import { useOutletContext } from 'react-router'
import { GameType, TheoryPaperEditModel, TheoryPaperQuestionEditModel, TheoryQuestionBankItemModel, TheoryQuestionType } from '@Api'
import { TextAreaField, TextField } from '../../../shared/FormControls'
import { ActionButton, InlineFeedback, VNextConfirmDialog } from '../../../shared/Interaction'
import { DataState } from '../../../shared/Primitives'
import { errorMessage } from '../../../shared/errors'
import { useVNextPageTitle } from '../../../shared/useVNextPageTitle'
import {
  normalizedTheoryPaper,
  reorderTheoryPaperQuestions,
  selectRandomTheoryQuestions,
  theoryPaperDraft,
  theoryPaperQuestionFromBank,
  theoryPaperTotalScore,
  validateTheoryPaper,
} from '../../theory/paperModel'
import { DEFAULT_THEORY_BANK, theoryQuestionTypeLabel } from '../../theory/questionModel'
import {
  AdminEditorActionBar,
  AdminEditorSection,
  AdminPageHeader,
  MetricItem,
  MetricStrip,
  PaginationBar,
  StatusBadge,
} from '../shared/AdminWorkbench'
import { formatAdminDate } from '../shared/adminFormat'
import type { GameAdminOutletContext } from '../games/GameAdminShell'
import { theoryAdminApi } from '../api'
import { useTheoryPaper, useTheoryQuestions } from './useTheoryAdmin'
import styles from './AdminTheoryPaperPage.module.css'

const BANK_PAGE_SIZE = 40

function questionSearchText(question: TheoryQuestionBankItemModel) {
  return [question.id, question.title, question.content, question.bankName, ...(question.tags ?? [])]
    .filter(Boolean)
    .join(' ')
    .toLocaleLowerCase('zh-CN')
}

export function AdminTheoryPaperPage() {
  const { game } = useOutletContext<GameAdminOutletContext>()
  const gameId = game.id as number
  const paperRequest = useTheoryPaper(gameId)
  const questionsRequest = useTheoryQuestions()
  const [draft, setDraft] = useState<TheoryPaperEditModel | null>(null)
  const [keyword, setKeyword] = useState('')
  const [typeFilter, setTypeFilter] = useState<TheoryQuestionType | ''>('')
  const [selectedBanks, setSelectedBanks] = useState<Set<string>>(() => new Set())
  const [selectedIds, setSelectedIds] = useState<Set<number>>(() => new Set())
  const [uniformScore, setUniformScore] = useState(5)
  const [randomCount, setRandomCount] = useState(5)
  const [bankPage, setBankPage] = useState(1)
  const [saving, setSaving] = useState(false)
  const [publishOpen, setPublishOpen] = useState(false)
  const [feedback, setFeedback] = useState<{ tone: 'danger' | 'neutral' | 'success'; message: string } | null>(null)

  useVNextPageTitle(`${game.title} · 理论试卷`)

  useEffect(() => {
    if (!paperRequest.paper) return
    setDraft(theoryPaperDraft(paperRequest.paper))
  }, [paperRequest.paper])

  const bankQuestions = questionsRequest.questions ?? []
  const banks = useMemo(
    () => [...new Set(bankQuestions.map((question) => question.bankName || DEFAULT_THEORY_BANK))]
      .sort((left, right) => left.localeCompare(right, 'zh-CN')),
    [bankQuestions]
  )
  const banksKey = banks.join('\u0000')

  useEffect(() => {
    setSelectedBanks((current) => {
      const valid = new Set([...current].filter((bank) => banks.includes(bank)))
      return valid.size ? valid : new Set(banks)
    })
  }, [banksKey])

  useEffect(() => setBankPage(1), [keyword, typeFilter, selectedBanks])

  const questions = draft?.questions ?? []
  const selectedSourceIds = useMemo(
    () => new Set(questions.map((question) => question.sourceQuestionId).filter((id): id is number => Boolean(id))),
    [questions]
  )
  const sourceQuestionsById = useMemo(
    () => new Map(bankQuestions.filter((question) => question.id).map((question) => [question.id as number, question])),
    [bankQuestions]
  )
  const filteredQuestions = useMemo(() => {
    const search = keyword.trim().toLocaleLowerCase('zh-CN')
    return bankQuestions.filter((question) => {
      if (!selectedBanks.has(question.bankName || DEFAULT_THEORY_BANK)) return false
      if (typeFilter && question.type !== typeFilter) return false
      return !search || questionSearchText(question).includes(search)
    })
  }, [bankQuestions, keyword, selectedBanks, typeFilter])
  const bankPageCount = Math.max(1, Math.ceil(filteredQuestions.length / BANK_PAGE_SIZE))
  const currentBankPage = Math.min(bankPage, bankPageCount)
  const visibleQuestions = filteredQuestions.slice(
    (currentBankPage - 1) * BANK_PAGE_SIZE,
    currentBankPage * BANK_PAGE_SIZE
  )
  const baseline = useMemo(
    () => paperRequest.paper ? theoryPaperDraft(paperRequest.paper) : null,
    [paperRequest.paper]
  )
  const dirty = Boolean(draft && baseline && JSON.stringify(normalizedTheoryPaper(draft)) !== JSON.stringify(baseline))
  const totalScore = draft ? theoryPaperTotalScore(draft) : 0

  useEffect(() => {
    if (!dirty) return undefined
    const warn = (event: BeforeUnloadEvent) => event.preventDefault()
    window.addEventListener('beforeunload', warn)
    return () => window.removeEventListener('beforeunload', warn)
  }, [dirty])

  const setQuestions = (next: TheoryPaperQuestionEditModel[]) => {
    setDraft((current) => current ? { ...current, questions: normalizedTheoryPaper({ ...current, questions: next }).questions } : current)
  }

  const addQuestions = (source: TheoryQuestionBankItemModel[]) => {
    const additions = source
      .filter((question) => question.id && !selectedSourceIds.has(question.id))
      .map((question, index) => theoryPaperQuestionFromBank(question, uniformScore, questions.length + index + 1))
    if (!additions.length) {
      setFeedback({ tone: 'neutral', message: '当前选择没有可加入的新题目。' })
      return
    }
    setQuestions([...questions, ...additions])
    setSelectedIds(new Set())
    setFeedback({ tone: 'success', message: `已加入 ${additions.length} 道题目，保存后才会写入服务器。` })
  }

  const addRandom = () => {
    const pool = filteredQuestions.filter((question) => question.id && !selectedSourceIds.has(question.id))
    const additions = selectRandomTheoryQuestions(pool, randomCount)
    if (!additions.length) {
      setFeedback({ tone: 'neutral', message: '当前筛选池没有可随机加入的新题目。' })
      return
    }
    addQuestions(additions)
  }

  const validate = () => {
    if (!draft) return null
    const payload = normalizedTheoryPaper(draft)
    const issues = validateTheoryPaper(payload)
    if (issues.length) {
      setFeedback({ tone: 'danger', message: issues.join(' ') })
      return null
    }
    return payload
  }

  const save = async () => {
    const payload = validate()
    if (!payload || saving) return false
    setSaving(true)
    setFeedback(null)
    try {
      const saved = await theoryAdminApi.savePaper(gameId, payload)
      await paperRequest.mutate(saved, { revalidate: false })
      setDraft(theoryPaperDraft(saved))
      setFeedback({ tone: 'success', message: '理论试卷草稿已保存，并从服务器回读。' })
      return true
    } catch (requestError) {
      setFeedback({ tone: 'danger', message: errorMessage(requestError, '理论试卷保存失败。') })
      return false
    } finally {
      setSaving(false)
    }
  }

  const publish = async () => {
    const payload = validate()
    if (!payload || saving) return false
    setSaving(true)
    setFeedback(null)
    try {
      await theoryAdminApi.savePaper(gameId, payload)
      const published = await theoryAdminApi.publishPaper(gameId)
      await paperRequest.mutate(published, { revalidate: false })
      setDraft(theoryPaperDraft(published))
      setFeedback({ tone: 'success', message: '理论试卷已保存并发布，选手端可按比赛时间与权限访问。' })
      return true
    } catch (requestError) {
      setFeedback({ tone: 'danger', message: errorMessage(requestError, '理论试卷发布失败。') })
      return false
    } finally {
      setSaving(false)
    }
  }

  if (game.gameType !== GameType.Theory && game.gameType !== GameType.Mixed) {
    return <DataState description="当前赛制不使用理论考试试卷。" title="理论试卷不可用" />
  }
  if (!paperRequest.paper || !draft || questionsRequest.questions === undefined) {
    const failed = paperRequest.error || questionsRequest.error
    return failed ? (
      <DataState description="理论试卷或共享题库读取失败。" title="无法打开理论试卷" />
    ) : (
      <DataState description="正在读取比赛试卷和共享题库。" loading title="理论试卷加载中" />
    )
  }

  return (
    <div className={styles.page}>
      <AdminPageHeader
        description="一场比赛只发布一套试卷；从共享题库加入快照、配置分值并完成发布。"
        eyebrow="THEORY PAPER"
        title="理论试卷"
      />
      <MetricStrip density="comfortable">
        <MetricItem detail="当前试卷" label="题目数量" value={questions.length} />
        <MetricItem detail="不含部分分" label="试卷总分" value={totalScore} />
        <MetricItem detail={`题库 ${banks.length} 个`} label="可用题目" value={bankQuestions.length} />
        <MetricItem detail={paperRequest.paper.publishedAt ? formatAdminDate(paperRequest.paper.publishedAt, false) : '尚未发布'} label="发布状态" value={paperRequest.paper.isPublished ? '已发布' : '草稿'} />
      </MetricStrip>
      {paperRequest.paper.isPublished ? (
        <InlineFeedback>当前试卷已发布。再次保存会先回到草稿状态；已有已提交答卷时，后端会拒绝编辑。</InlineFeedback>
      ) : null}
      {feedback ? <InlineFeedback tone={feedback.tone === 'neutral' ? undefined : feedback.tone}>{feedback.message}</InlineFeedback> : null}
      <div className={styles.workspace}>
        <main className={styles.paperColumn}>
          <AdminEditorSection description="保存草稿不会发布；保存并发布会先写入当前配置，再调用发布接口。" title="试卷配置">
            <div className={styles.formGrid}>
              <TextField label="试卷名称" onValueChange={(value) => setDraft((current) => current ? { ...current, title: value } : current)} required value={draft.title} />
              <TextAreaField label="试卷说明" onValueChange={(value) => setDraft((current) => current ? { ...current, description: value } : current)} rows={5} value={draft.description ?? ''} />
            </div>
          </AdminEditorSection>
          <section className={styles.selectedSection}>
            <header>
              <div><span>PAPER STRUCTURE</span><h2>已选题目</h2></div>
              <div className={styles.bulkScore}>
                <TextField label="统一分值" min={1} onValueChange={(value) => setUniformScore(Math.max(1, Number(value) || 1))} type="number" value={uniformScore} />
                <ActionButton disabled={!questions.length} onClick={() => setQuestions(questions.map((question) => ({ ...question, score: uniformScore })))} type="button">应用全部</ActionButton>
              </div>
            </header>
            {questions.length ? (
              <div className={styles.selectedList}>
                {questions.map((question, index) => (
                  <article key={`${question.sourceQuestionId ?? question.id ?? question.title}-${index}`}>
                    <span>{String(index + 1).padStart(2, '0')}</span>
                    <div>
                      <strong>{question.title}</strong>
                      <small>
                        {theoryQuestionTypeLabel(question.type)} · {question.bankName || (question.sourceQuestionId ? sourceQuestionsById.get(question.sourceQuestionId)?.bankName : null) || DEFAULT_THEORY_BANK}
                      </small>
                    </div>
                    <TextField aria-label={`第 ${index + 1} 题分值`} label="分值" min={1} onValueChange={(value) => setQuestions(questions.map((item, itemIndex) => itemIndex === index ? { ...item, score: Math.max(1, Number(value) || 1) } : item))} type="number" value={question.score ?? uniformScore} />
                    <div className={styles.itemActions}>
                      <button aria-label={`上移第 ${index + 1} 题`} disabled={index === 0} onClick={() => setQuestions(reorderTheoryPaperQuestions(questions, index, -1))} type="button"><ArrowUp size={15} /></button>
                      <button aria-label={`下移第 ${index + 1} 题`} disabled={index === questions.length - 1} onClick={() => setQuestions(reorderTheoryPaperQuestions(questions, index, 1))} type="button"><ArrowDown size={15} /></button>
                      <button aria-label={`移除第 ${index + 1} 题`} data-danger onClick={() => setQuestions(questions.filter((_, itemIndex) => itemIndex !== index))} type="button"><Trash2 size={15} /></button>
                    </div>
                  </article>
                ))}
              </div>
            ) : <DataState description="从左侧共享题库指定题目，或按当前筛选条件随机抽取。" title="试卷尚无题目" />}
          </section>
        </main>
        <aside className={styles.bankColumn}>
          <header><div><span>QUESTION SOURCE</span><h2>共享题库</h2></div><StatusBadge tone="info">最多 5000 条</StatusBadge></header>
          <TextField label="搜索题目" onValueChange={setKeyword} value={keyword} />
          <div className={styles.filterRow}>
            <label><span>题型</span><select aria-label="筛选组卷题型" onChange={(event) => setTypeFilter(event.currentTarget.value as TheoryQuestionType | '')} value={typeFilter}><option value="">全部题型</option>{Object.values(TheoryQuestionType).map((type) => <option key={type} value={type}>{theoryQuestionTypeLabel(type)}</option>)}</select></label>
            <TextField label="随机数量" min={1} onValueChange={(value) => setRandomCount(Math.max(1, Number(value) || 1))} type="number" value={randomCount} />
            <ActionButton icon={<Dice5 size={16} />} onClick={addRandom} type="button">随机抽取</ActionButton>
          </div>
          <fieldset className={styles.bankSelector}>
            <legend>题库（可多选）</legend>
            <div>
              {banks.map((bank) => <label key={bank}><input checked={selectedBanks.has(bank)} onChange={(event) => setSelectedBanks((current) => { const next = new Set(current); if (event.currentTarget.checked) next.add(bank); else next.delete(bank); return next })} type="checkbox" /><span>{bank}</span></label>)}
            </div>
            <button onClick={() => setSelectedBanks(new Set(banks))} type="button">全选题库</button>
          </fieldset>
          <div className={styles.bankList}>
            {visibleQuestions.map((question) => {
              const id = question.id ?? 0
              const added = selectedSourceIds.has(id)
              return (
                <label data-added={added || undefined} key={id || question.title}>
                  <input checked={selectedIds.has(id)} disabled={!id || added} onChange={(event) => setSelectedIds((current) => { const next = new Set(current); if (event.currentTarget.checked) next.add(id); else next.delete(id); return next })} type="checkbox" />
                  <span><strong>{question.title}</strong><small>{theoryQuestionTypeLabel(question.type)} · {question.bankName || DEFAULT_THEORY_BANK}</small></span>
                  <StatusBadge tone={added ? 'success' : 'neutral'}>{added ? '已加入' : `#${id}`}</StatusBadge>
                </label>
              )
            })}
            {!visibleQuestions.length ? <DataState description="调整题库、题型或搜索条件后重试。" title="没有匹配题目" /> : null}
          </div>
          <PaginationBar onPageChange={setBankPage} page={currentBankPage} pageCount={bankPageCount} total={filteredQuestions.length} />
          <ActionButton disabled={!selectedIds.size} icon={<Plus size={16} />} onClick={() => addQuestions(bankQuestions.filter((question) => question.id && selectedIds.has(question.id)))} tone="primary" type="button">加入选中题目</ActionButton>
        </aside>
      </div>
      <AdminEditorActionBar status={dirty ? `有未保存更改 · ${questions.length} 题 / ${totalScore} 分` : `已与服务器同步 · ${questions.length} 题 / ${totalScore} 分`}>
        <ActionButton disabled={saving || !dirty} icon={<Save size={16} />} onClick={() => void save()} type="button">{saving ? '正在保存' : '保存草稿'}</ActionButton>
        <ActionButton disabled={saving || !questions.length} icon={<Send size={16} />} onClick={() => setPublishOpen(true)} tone="primary" type="button">保存并发布</ActionButton>
      </AdminEditorActionBar>
      <VNextConfirmDialog description="发布后选手可按比赛时间与权限访问；已有答卷后将无法再编辑试卷。" message={`将保存并发布“${draft.title || game.title}”，共 ${questions.length} 题、${totalScore} 分。`} onClose={() => setPublishOpen(false)} onConfirm={publish} open={publishOpen} title="发布理论试卷？" />
    </div>
  )
}
