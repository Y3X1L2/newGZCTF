import { FileUp, Pencil, Plus, RefreshCw, RotateCcw, Search, Trash2 } from 'lucide-react'
import { useEffect, useMemo, useState } from 'react'
import { TheoryQuestionBankItemModel, TheoryQuestionEditModel, TheoryQuestionType } from '@Api'
import { ActionButton, InlineFeedback, VNextConfirmDialog } from '../../../shared/Interaction'
import { DataState } from '../../../shared/Primitives'
import { errorMessage } from '../../../shared/errors'
import { useVNextPageTitle } from '../../../shared/useVNextPageTitle'
import { TheoryQuestionDialog } from '../../theory/admin/TheoryQuestionDialog'
import {
  DEFAULT_THEORY_BANK,
  normalizeTheoryQuestion,
  theoryAnswerLabel,
  theoryQuestionTypeLabel,
} from '../../theory/questionModel'
import { theoryAdminApi } from '../api'
import {
  AdminDataColumn,
  AdminPageHeader,
  DataTable,
  FilterToolbar,
  MetricItem,
  MetricStrip,
  PaginationBar,
  RefreshIndicator,
  StatusBadge,
  ToolbarGroup,
} from '../shared/AdminWorkbench'
import { formatAdminDate } from '../shared/adminFormat'
import { useAdminQueryState } from '../shared/useAdminQueryState'
import styles from './AdminTheoryBankPage.module.css'
import { TheoryQuestionImportDialog } from './TheoryQuestionImportDialog'
import { useTheoryQuestions } from './useTheoryAdmin'

const PAGE_SIZE = 20

function searchableText(question: TheoryQuestionBankItemModel) {
  return [question.id, question.title, question.content, question.bankName, ...(question.tags ?? [])]
    .filter((value) => value !== undefined && value !== null)
    .join(' ')
    .toLocaleLowerCase('zh-CN')
}

export function AdminTheoryBankPage() {
  const request = useTheoryQuestions()
  const queryState = useAdminQueryState(PAGE_SIZE)
  const [query, setQuery] = useState(queryState.params.get('q') ?? '')
  const [editorOpen, setEditorOpen] = useState(false)
  const [importOpen, setImportOpen] = useState(false)
  const [activeQuestion, setActiveQuestion] = useState<TheoryQuestionBankItemModel | null>(null)
  const [deleteTarget, setDeleteTarget] = useState<TheoryQuestionBankItemModel | null>(null)
  const [saving, setSaving] = useState(false)
  const [feedback, setFeedback] = useState<{ tone: 'danger' | 'success'; message: string } | null>(null)
  const type = queryState.params.get('type') as TheoryQuestionType | null
  const bank = queryState.params.get('bank')
  const tag = queryState.params.get('tag')
  const questions = request.questions ?? []

  useVNextPageTitle('理论题库')

  useEffect(() => setQuery(queryState.params.get('q') ?? ''), [queryState.params])
  useEffect(() => {
    const current = queryState.params.get('q') ?? ''
    if (query.trim() === current) return undefined
    const timer = window.setTimeout(() => queryState.update({ q: query.trim() || null }, { replace: true }), 250)
    return () => window.clearTimeout(timer)
  }, [query, queryState])

  const banks = useMemo(
    () => [...new Set(questions.map((question) => question.bankName || DEFAULT_THEORY_BANK))].sort((left, right) => left.localeCompare(right, 'zh-CN')),
    [questions]
  )
  const tags = useMemo(
    () => [...new Set(questions.flatMap((question) => question.tags ?? []))].sort((left, right) => left.localeCompare(right, 'zh-CN')),
    [questions]
  )
  const filtered = useMemo(() => {
    const keyword = (queryState.params.get('q') ?? '').toLocaleLowerCase('zh-CN')
    return questions.filter((question) => {
      if (keyword && !searchableText(question).includes(keyword)) return false
      if (type && question.type !== type) return false
      if (bank && (question.bankName || DEFAULT_THEORY_BANK) !== bank) return false
      if (tag && !(question.tags ?? []).includes(tag)) return false
      return true
    })
  }, [bank, queryState.params, questions, tag, type])
  const pageCount = Math.max(1, Math.ceil(filtered.length / PAGE_SIZE))
  const page = Math.min(queryState.page, pageCount)
  const rows = filtered.slice((page - 1) * PAGE_SIZE, page * PAGE_SIZE)
  const typeCounts = useMemo(() => ({
    single: questions.filter((question) => question.type === TheoryQuestionType.SingleChoice).length,
    multiple: questions.filter((question) => question.type === TheoryQuestionType.MultipleChoice).length,
    trueFalse: questions.filter((question) => question.type === TheoryQuestionType.TrueFalse).length,
  }), [questions])

  const openEditor = (question: TheoryQuestionBankItemModel | null) => {
    setActiveQuestion(question)
    setEditorOpen(true)
  }

  const saveQuestion = async (draft: TheoryQuestionEditModel) => {
    setSaving(true)
    setFeedback(null)
    try {
      const payload = normalizeTheoryQuestion(draft)
      if (activeQuestion?.id) await theoryAdminApi.updateQuestion(activeQuestion.id, payload)
      else await theoryAdminApi.createQuestion(payload)
      await request.mutate()
      setEditorOpen(false)
      setActiveQuestion(null)
      setFeedback({ tone: 'success', message: activeQuestion ? '理论题目已更新。' : '理论题目已创建。' })
    } catch (requestError) {
      setFeedback({ tone: 'danger', message: errorMessage(requestError, '理论题目保存失败。') })
    } finally {
      setSaving(false)
    }
  }

  const removeQuestion = async () => {
    if (!deleteTarget?.id) return false
    setSaving(true)
    setFeedback(null)
    try {
      await theoryAdminApi.removeQuestion(deleteTarget.id)
      await request.mutate()
      setFeedback({ tone: 'success', message: `题目“${deleteTarget.title}”已删除。` })
      return true
    } catch (requestError) {
      setFeedback({ tone: 'danger', message: errorMessage(requestError, '理论题目删除失败。') })
      return false
    } finally {
      setSaving(false)
    }
  }

  const columns: AdminDataColumn<TheoryQuestionBankItemModel>[] = [
    {
      id: 'question',
      header: '题目',
      width: 'wide',
      render: (question) => (
        <div className={styles.identity}>
          <strong>{question.title}</strong>
          <small>#{question.id ?? '—'} · {question.content || '暂无解析'}</small>
        </div>
      ),
    },
    { id: 'type', header: '题型', width: 'compact', render: (question) => <StatusBadge tone="info">{theoryQuestionTypeLabel(question.type)}</StatusBadge> },
    {
      id: 'bank',
      header: '题库与标签',
      width: 'medium',
      render: (question) => (
        <div className={styles.metaCell}>
          <strong>{question.bankName || DEFAULT_THEORY_BANK}</strong>
          <small>{(question.tags ?? []).join('、') || '无标签'}</small>
        </div>
      ),
    },
    { id: 'answer', header: '正确答案', width: 'medium', visibility: 'desktop', render: (question) => <span className={styles.answer}>{theoryAnswerLabel(question) || '未配置'}</span> },
    { id: 'updated', header: '更新时间', width: 'medium', visibility: 'wide', render: (question) => formatAdminDate(question.updatedAt ?? question.createdAt, false) },
    {
      id: 'action',
      header: '操作',
      width: 'compact',
      align: 'right',
      render: (question) => (
        <span className={styles.rowActions}>
          <button aria-label={`编辑 ${question.title}`} className={styles.iconButton} onClick={() => openEditor(question)} type="button"><Pencil size={16} /></button>
          <button aria-label={`删除 ${question.title}`} className={styles.iconButton} data-danger onClick={() => setDeleteTarget(question)} type="button"><Trash2 size={16} /></button>
        </span>
      ),
    },
  ]

  return (
    <div className={styles.page}>
      <AdminPageHeader
        actions={
          <>
            <ActionButton icon={<RefreshCw size={16} />} onClick={() => void request.mutate()} type="button">刷新</ActionButton>
            <ActionButton icon={<FileUp size={16} />} onClick={() => setImportOpen(true)} type="button">JSON 导入</ActionButton>
            <ActionButton icon={<Plus size={16} />} onClick={() => openEditor(null)} tone="primary" type="button">新建题目</ActionButton>
          </>
        }
        description="维护全平台共享的单选、多选和判断题；比赛试卷从这里引用并保存题目快照。"
        eyebrow="THEORY QUESTION BANK"
        title="理论题库"
      />
      <MetricStrip density="comfortable">
        <MetricItem detail="最多加载 5000 条" label="当前题目" value={questions.length} />
        <MetricItem detail="共享题库" label="题库数量" value={banks.length} />
        <MetricItem detail={`多选 ${typeCounts.multiple}`} label="单选题" value={typeCounts.single} />
        <MetricItem detail={`标签 ${tags.length}`} label="判断题" value={typeCounts.trueFalse} />
      </MetricStrip>
      <div className={styles.catalog}>
        <aside className={styles.filterPanel}>
          <header>
            <span>QUESTION FILTERS</span>
            <h2>筛选范围</h2>
            <p>先选择题型、题库或标签，再在右侧搜索题干与解析。</p>
          </header>
          <section>
            <strong>题型</strong>
            <div className={styles.filterOptions}>
              <button data-active={!type || undefined} onClick={() => queryState.update({ type: null })} type="button">全部题型 <small>{questions.length}</small></button>
              {Object.values(TheoryQuestionType).map((value) => (
                <button data-active={type === value || undefined} key={value} onClick={() => queryState.update({ type: value })} type="button">
                  {theoryQuestionTypeLabel(value)}
                  <small>{value === TheoryQuestionType.SingleChoice ? typeCounts.single : value === TheoryQuestionType.MultipleChoice ? typeCounts.multiple : typeCounts.trueFalse}</small>
                </button>
              ))}
            </div>
          </section>
          <section>
            <strong>题库</strong>
            <div className={styles.filterOptions}>
              <button data-active={!bank || undefined} onClick={() => queryState.update({ bank: null })} type="button">全部题库 <small>{banks.length}</small></button>
              {banks.map((value) => <button data-active={bank === value || undefined} key={value} onClick={() => queryState.update({ bank: value })} type="button">{value}</button>)}
            </div>
          </section>
          {tags.length ? (
            <section>
              <strong>标签</strong>
              <div className={styles.filterOptions}>
                <button data-active={!tag || undefined} onClick={() => queryState.update({ tag: null })} type="button">全部标签 <small>{tags.length}</small></button>
                {tags.map((value) => <button data-active={tag === value || undefined} key={value} onClick={() => queryState.update({ tag: value })} type="button">{value}</button>)}
              </div>
            </section>
          ) : null}
          <button className={styles.clearFilters} disabled={!type && !bank && !tag} onClick={() => queryState.update({ type: null, bank: null, tag: null })} type="button">
            <RotateCcw size={15} />
            重置筛选
          </button>
        </aside>
        <main className={styles.tableColumn}>
          <InlineFeedback>当前接口没有结构化筛选和总数；题型、题库与标签筛选作用于本次加载的最多 5000 条服务器记录。</InlineFeedback>
          <FilterToolbar>
            <ToolbarGroup grow>
              <label className={styles.searchBox}>
                <Search aria-hidden="true" size={17} />
                <input aria-label="搜索理论题目" onChange={(event) => setQuery(event.currentTarget.value)} placeholder="搜索题干、解析、题库、标签或编号" type="search" value={query} />
              </label>
            </ToolbarGroup>
            <RefreshIndicator active={request.isRefreshing} label="题库按需刷新" />
          </FilterToolbar>
          {feedback ? <InlineFeedback tone={feedback.tone}>{feedback.message}</InlineFeedback> : null}
          {request.error ? <InlineFeedback tone="danger">{errorMessage(request.error, '理论题库加载失败。')}</InlineFeedback> : null}
          {request.isLoading ? (
            <DataState description="正在读取全平台理论题库。" loading title="理论题库加载中" />
          ) : (
            <>
              <DataTable caption="理论题库管理列表" columns={columns} density="comfortable" emptyDescription="当前筛选条件下没有可展示的理论题目。" emptyTitle="没有匹配题目" onRowClick={openEditor} rowKey={(question) => question.id ?? question.title} rows={rows} />
              <PaginationBar onPageChange={queryState.setPage} page={page} pageCount={pageCount} total={filtered.length} />
            </>
          )}
        </main>
      </div>
      <TheoryQuestionDialog
        loading={saving}
        onClose={() => { setEditorOpen(false); setActiveQuestion(null) }}
        onSave={saveQuestion}
        open={editorOpen}
        question={activeQuestion}
      />
      <TheoryQuestionImportDialog
        existing={questions}
        onClose={() => setImportOpen(false)}
        onCompleted={async (message) => { await request.mutate(); setFeedback({ tone: 'success', message }) }}
        onRefresh={request.mutate}
        open={importOpen}
      />
      <VNextConfirmDialog
        confirmationText={deleteTarget?.title}
        description="比赛试卷保存的是题目快照；删除共享题目不会修改已有试卷内容。"
        message={deleteTarget ? `将永久删除题库题目“${deleteTarget.title}”。` : ''}
        onClose={() => setDeleteTarget(null)}
        onConfirm={removeQuestion}
        open={Boolean(deleteTarget)}
        title="删除理论题目？"
      />
    </div>
  )
}
