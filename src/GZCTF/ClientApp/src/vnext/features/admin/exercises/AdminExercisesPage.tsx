import { Pencil, Plus, Search, Trash2 } from 'lucide-react'
import { useMemo, useState } from 'react'
import { ActionButton, InlineFeedback, VNextConfirmDialog } from '../../../shared/Interaction'
import { DataState } from '../../../shared/Primitives'
import { errorMessage } from '../../../shared/errors'
import { useVNextPageTitle } from '../../../shared/useVNextPageTitle'
import {
  AdminDataColumn,
  AdminPageHeader,
  DataTable,
  FilterToolbar,
  MetricItem,
  MetricStrip,
  StatusBadge,
  ToolbarGroup,
} from '../shared/AdminWorkbench'
import { ExerciseEditorDrawer } from './ExerciseEditorDrawer'
import { exerciseAdminApi, useAdminExercises } from './exerciseAdminApi'
import styles from './AdminExercisesPage.module.css'

export function AdminExercisesPage() {
  useVNextPageTitle('练习题库管理')
  const request = useAdminExercises()
  const [query, setQuery] = useState('')
  const [editingId, setEditingId] = useState<number | null>(null)
  const [editorOpen, setEditorOpen] = useState(false)
  const [deleting, setDeleting] = useState<{ id: number; title: string } | null>(null)
  const [failure, setFailure] = useState<string | null>(null)

  const rows = useMemo(() => {
    const keyword = query.trim().toLocaleLowerCase('zh-CN')
    return (request.data ?? []).filter((exercise) =>
      !keyword || `${exercise.title} ${exercise.category ?? ''} ${exercise.tags?.join(' ') ?? ''} ${exercise.id}`
        .toLocaleLowerCase('zh-CN')
        .includes(keyword))
  }, [query, request.data])

  const columns: AdminDataColumn<(typeof rows)[number]>[] = [
    {
      id: 'exercise',
      header: '题目',
      width: 'wide',
      render: (exercise) => (
        <div className={styles.identity}>
          <strong>{exercise.title}</strong>
          <small>#{exercise.id} · {exercise.tags?.join(' / ') || '无标签'}</small>
        </div>
      ),
    },
    { id: 'category', header: '分类', width: 'compact', render: (exercise) => exercise.category || 'Misc' },
    { id: 'difficulty', header: '难度', width: 'compact', render: (exercise) => exercise.difficulty || 'Baby' },
    { id: 'type', header: '类型', width: 'medium', visibility: 'desktop', render: (exercise) => exercise.type },
    {
      id: 'status',
      header: '状态',
      width: 'compact',
      render: (exercise) => <StatusBadge tone={exercise.isEnabled ? 'success' : 'neutral'}>{exercise.isEnabled ? '启用' : '停用'}</StatusBadge>,
    },
    {
      id: 'activity',
      header: '提交',
      width: 'medium',
      visibility: 'wide',
      render: (exercise) => `${exercise.acceptedCount} 人完成 · ${exercise.submissionCount} 次`,
    },
    {
      id: 'actions',
      header: '操作',
      width: 'compact',
      align: 'right',
      render: (exercise) => (
        <div className={styles.rowActions}>
          <button aria-label={`编辑 ${exercise.title}`} onClick={() => { setEditingId(exercise.id); setEditorOpen(true) }} title="编辑题目" type="button"><Pencil size={16} /></button>
          <button aria-label={`删除 ${exercise.title}`} onClick={() => setDeleting({ id: exercise.id, title: exercise.title })} title="删除题目" type="button"><Trash2 size={16} /></button>
        </div>
      ),
    },
  ]

  const remove = async () => {
    if (!deleting) return false
    setFailure(null)
    try {
      await exerciseAdminApi.remove(deleting.id)
      await request.mutate()
      return true
    } catch (requestError) {
      setFailure(errorMessage(requestError, '练习题删除失败。'))
      return false
    }
  }

  return (
    <div className={styles.page}>
      <AdminPageHeader
        actions={<ActionButton icon={<Plus size={16} />} onClick={() => { setEditingId(null); setEditorOpen(true) }} tone="primary" type="button">创建练习题</ActionButton>}
        description="维护公共自主练习题库、Flags、附件和容器配置；培训课程题目不在此处显示。"
        eyebrow="EXERCISE OPERATIONS"
        title="练习题库"
      />
      <MetricStrip>
        <MetricItem detail="公共题目" label="题目总数" value={request.data?.length ?? 0} />
        <MetricItem detail="学员可见" label="已启用" tone="success" value={request.data?.filter((item) => item.isEnabled).length ?? 0} />
        <MetricItem detail="容器题型" label="动态环境" value={request.data?.filter((item) => item.type?.includes('Container')).length ?? 0} />
      </MetricStrip>
      <FilterToolbar>
        <ToolbarGroup grow>
          <label className={styles.searchBox}>
            <Search size={16} />
            <input aria-label="搜索练习题" onChange={(event) => setQuery(event.currentTarget.value)} placeholder="名称、分类、标签或编号" type="search" value={query} />
          </label>
        </ToolbarGroup>
      </FilterToolbar>
      {failure ? <InlineFeedback tone="danger">{failure}</InlineFeedback> : null}
      {request.error ? <InlineFeedback tone="danger">{errorMessage(request.error, '练习题列表加载失败。')}</InlineFeedback> : null}
      {!request.data && !request.error ? (
        <DataState description="正在读取公共练习题库。" loading title="练习题加载中" />
      ) : (
        <DataTable caption="公共练习题管理列表" columns={columns} emptyDescription="当前没有符合条件的公共练习题。" emptyTitle="没有练习题" rowKey={(exercise) => exercise.id} rows={rows} />
      )}
      <ExerciseEditorDrawer
        exerciseId={editingId}
        onClose={() => setEditorOpen(false)}
        onSaved={() => void request.mutate()}
        open={editorOpen}
      />
      <VNextConfirmDialog
        confirmLabel="删除题目"
        description="删除会同时清理该公共题目的实例、提交记录和独立附件。"
        message={deleting ? `确认删除“${deleting.title}”？` : ''}
        onClose={() => setDeleting(null)}
        onConfirm={remove}
        open={Boolean(deleting)}
        title="删除练习题"
      />
    </div>
  )
}
