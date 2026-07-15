import { Save, Trash2 } from 'lucide-react'
import { FormEvent, useEffect, useState } from 'react'
import { useNavigate, useParams } from 'react-router'
import api, { TrainingArticleContentType, TrainingCourseChapterEditModel, TrainingCourseVideoProvider } from '@Api'
import { FileField, SelectField, TextAreaField, TextField, ToggleField } from '../../../../shared/FormControls'
import { ActionButton, InlineFeedback, VNextDialog } from '../../../../shared/Interaction'
import { MarkdownContent } from '../../../../shared/MarkdownContent'
import { DataState, StatusPill } from '../../../../shared/Primitives'
import { errorMessage } from '../../../../shared/errors'
import { useVNextPageTitle } from '../../../../shared/useVNextPageTitle'
import { EditorActionBar, EditorSection, TrainingEditorShell } from '../TrainingEditorShell'
import { trainingAdminApi, uploadTrainingAsset } from '../trainingAdminApi'
import styles from './TrainingChapterEditorPage.module.css'

const emptyDraft = (): TrainingCourseChapterEditModel => ({
  parentId: null,
  title: '',
  summary: '',
  content: '',
  contentType: TrainingArticleContentType.Markdown,
  completionPolicy: {
    requireContentRead: true,
    requireAllRequiredChallenges: true,
    requiredChallengeCount: 0,
    theoryPassRate: 80,
  },
  videoProvider: TrainingCourseVideoProvider.None,
  videoUrl: null,
  videoFileHash: null,
  order: 1,
  isPublished: true,
})

export function TrainingChapterEditorPage() {
  const { courseId, chapterId } = useParams()
  const navigate = useNavigate()
  const courseNumber = Number(courseId)
  const chapterNumber = Number(chapterId)
  const validCourse = Number.isInteger(courseNumber) && courseNumber > 0
  const editing = Number.isInteger(chapterNumber) && chapterNumber > 0
  const courseRequest = api.trainingCourseAdmin.useTrainingCourseAdminCourse(
    courseNumber,
    { revalidateOnFocus: false },
    validCourse
  )
  const chapterRequest = api.trainingCourse.useTrainingCourseChapter(
    courseNumber,
    chapterNumber,
    { revalidateOnFocus: false },
    validCourse && editing
  )
  const course = courseRequest.data
  const chapter = chapterRequest.data
  const [draft, setDraft] = useState<TrainingCourseChapterEditModel>(emptyDraft)
  const [videoFile, setVideoFile] = useState<File | null>(null)
  const [saving, setSaving] = useState(false)
  const [deleteOpen, setDeleteOpen] = useState(false)
  const [feedback, setFeedback] = useState<{ tone: 'success' | 'danger'; message: string } | null>(null)
  const [titleError, setTitleError] = useState<string | null>(null)

  const updateDraftField = <Key extends keyof TrainingCourseChapterEditModel>(
    field: Key,
    value: TrainingCourseChapterEditModel[Key]
  ) => {
    setDraft((current) => ({ ...current, [field]: value }))
  }

  useVNextPageTitle(editing ? `编辑 ${chapter?.title || '章节'}` : '添加章节')

  useEffect(() => {
    if (!course || editing) return
    setDraft((current) => ({ ...current, order: (course.chapters?.length ?? 0) + 1 }))
  }, [course, editing])

  useEffect(() => {
    if (!chapter || !editing) return
    setDraft({
      parentId: chapter.parentId ?? null,
      title: chapter.title ?? '',
      summary: chapter.summary ?? '',
      content: chapter.content ?? '',
      contentType: chapter.contentType ?? TrainingArticleContentType.Markdown,
      completionPolicy: {
        requireContentRead: chapter.completionPolicy?.requireContentRead ?? true,
        requireAllRequiredChallenges: chapter.completionPolicy?.requireAllRequiredChallenges ?? true,
        requiredChallengeCount: chapter.completionPolicy?.requiredChallengeCount ?? 0,
        theoryPassRate: chapter.completionPolicy?.theoryPassRate ?? 80,
      },
      videoProvider: chapter.videoProvider ?? TrainingCourseVideoProvider.None,
      videoUrl: chapter.videoUrl ?? null,
      videoFileHash: null,
      order: chapter.order ?? 1,
      isPublished: chapter.isPublished ?? true,
    })
  }, [chapter, editing])

  const returnToCourse = () => navigate(`/training/courses/${courseNumber}?tab=chapters`)

  const save = async (event?: FormEvent) => {
    event?.preventDefault()
    const title = draft.title.trim()
    if (!title) {
      setTitleError('请输入章节名称。')
      return
    }

    setSaving(true)
    setFeedback(null)
    setTitleError(null)
    try {
      const videoFileHash =
        draft.videoProvider === TrainingCourseVideoProvider.LocalFile && videoFile
          ? await uploadTrainingAsset(videoFile)
          : draft.videoFileHash
      const payload: TrainingCourseChapterEditModel = {
        ...draft,
        title,
        summary: draft.summary?.trim() || '',
        content: draft.content || '',
        videoUrl:
          draft.videoProvider === TrainingCourseVideoProvider.ExternalUrl ? draft.videoUrl?.trim() || null : null,
        videoFileHash: draft.videoProvider === TrainingCourseVideoProvider.LocalFile ? (videoFileHash ?? null) : null,
        order: Math.max(1, Number(draft.order) || 1),
        completionPolicy: {
          requireContentRead: draft.completionPolicy?.requireContentRead ?? true,
          requireAllRequiredChallenges: draft.completionPolicy?.requireAllRequiredChallenges ?? true,
          requiredChallengeCount: Math.max(0, Number(draft.completionPolicy?.requiredChallengeCount) || 0),
          theoryPassRate: Math.min(100, Math.max(0, Number(draft.completionPolicy?.theoryPassRate) || 0)),
        },
      }

      if (editing) await trainingAdminApi.updateChapter(courseNumber, chapterNumber, payload)
      else await trainingAdminApi.createChapter(courseNumber, payload)
      await courseRequest.mutate()
      returnToCourse()
    } catch (requestError) {
      setFeedback({ tone: 'danger', message: errorMessage(requestError, '章节保存失败。') })
    } finally {
      setSaving(false)
    }
  }

  const deleteChapter = async () => {
    if (!editing || saving) return
    setSaving(true)
    try {
      await trainingAdminApi.deleteChapter(courseNumber, chapterNumber)
      returnToCourse()
    } catch (requestError) {
      setFeedback({ tone: 'danger', message: errorMessage(requestError, '章节删除失败。') })
      setDeleteOpen(false)
      setSaving(false)
    }
  }

  if (!validCourse) return <DataState description="课程编号不是有效数字。" title="课程参数错误" />
  if (!course || (editing && !chapter)) {
    if (!courseRequest.error && (!editing || !chapterRequest.error)) {
      return <DataState description="正在读取课程、章节正文和编辑权限。" loading title="编辑器加载中" />
    }
    return <DataState description="课程或章节不存在，或当前账户没有访问权限。" title="无法打开章节编辑器" />
  }
  if (!course.canEdit) {
    return <DataState description="只有课程授课教师、课程创建者和管理员可以维护章节。" title="没有章节编辑权限" />
  }

  const policy = draft.completionPolicy ?? {}

  return (
    <TrainingEditorShell
      backLabel="返回课程章节"
      backTo={`/training/courses/${courseNumber}?tab=chapters`}
      description="正文编辑与预览保持同屏；实验和理论练习在课程详情的独立管理页面配置。"
      eyebrow="CHAPTER WORKSPACE"
      meta={
        <StatusPill tone={draft.isPublished ? 'success' : 'warning'}>
          {draft.isPublished ? '发布后可见' : '教师草稿'}
        </StatusPill>
      }
      title={editing ? '编辑章节' : '添加章节'}
    >
      {feedback ? <InlineFeedback tone={feedback.tone}>{feedback.message}</InlineFeedback> : null}
      <form className={styles.form} onSubmit={(event) => void save(event)}>
        <div className={styles.editorGrid}>
          <div className={styles.editorColumn}>
            <EditorSection description="章节名称、层级和排序决定左侧课程目录结构。" title="章节信息">
              <div className={styles.stack}>
                <TextField
                  error={titleError}
                  label="章节名称"
                  maxLength={128}
                  onValueChange={(value) => updateDraftField('title', value)}
                  placeholder="例如：SSTI 原理与实验"
                  required
                  value={draft.title}
                />
                <TextAreaField
                  label="章节摘要"
                  maxLength={512}
                  onValueChange={(value) => updateDraftField('summary', value)}
                  rows={3}
                  value={draft.summary}
                />
                <div className={styles.twoColumns}>
                  <SelectField
                    label="上级章节"
                    onValueChange={(value) =>
                      setDraft((current) => ({
                        ...current,
                        parentId: value ? Number(value) : null,
                      }))
                    }
                    value={draft.parentId ?? ''}
                  >
                    <option value="">无上级章节</option>
                    {(course.chapters ?? [])
                      .filter((item) => item.id && item.id !== chapterNumber)
                      .map((item) => (
                        <option key={item.id} value={item.id}>
                          {item.title}
                        </option>
                      ))}
                  </SelectField>
                  <TextField
                    label="排序"
                    min={1}
                    onValueChange={(value) => updateDraftField('order', Number(value))}
                    type="number"
                    value={draft.order ?? 1}
                  />
                </div>
                <ToggleField
                  checked={draft.isPublished ?? true}
                  description="未发布章节仅课程教师和管理员可见。"
                  label="发布章节"
                  onChange={(checked) => setDraft((current) => ({ ...current, isPublished: checked }))}
                />
              </div>
            </EditorSection>

            <EditorSection description="章节正文以 Markdown 为主，保存后学生页面使用相同渲染器。" title="知识正文">
              <div className={styles.stack}>
                <SelectField
                  label="正文格式"
                  onValueChange={(value) => updateDraftField('contentType', value as TrainingArticleContentType)}
                  value={draft.contentType}
                >
                  <option value={TrainingArticleContentType.Markdown}>Markdown</option>
                  <option value={TrainingArticleContentType.Html}>HTML</option>
                </SelectField>
                <TextAreaField
                  label="章节内容"
                  onValueChange={(value) => updateDraftField('content', value)}
                  placeholder="# 本章目标"
                  rows={28}
                  value={draft.content}
                />
              </div>
            </EditorSection>

            <EditorSection description="视频与正文处于同一章节，学生无需跳转到外部页面。" title="教学视频">
              <div className={styles.stack}>
                <SelectField
                  label="视频来源"
                  onValueChange={(value) => updateDraftField('videoProvider', value as TrainingCourseVideoProvider)}
                  value={draft.videoProvider}
                >
                  <option value={TrainingCourseVideoProvider.None}>不配置视频</option>
                  <option value={TrainingCourseVideoProvider.ExternalUrl}>外部视频地址</option>
                  <option value={TrainingCourseVideoProvider.LocalFile}>上传本地视频</option>
                </SelectField>
                {draft.videoProvider === TrainingCourseVideoProvider.ExternalUrl ? (
                  <TextField
                    label="视频地址"
                    onValueChange={(value) => updateDraftField('videoUrl', value)}
                    placeholder="https://..."
                    type="url"
                    value={draft.videoUrl ?? ''}
                  />
                ) : null}
                {draft.videoProvider === TrainingCourseVideoProvider.LocalFile ? (
                  <FileField
                    accept="video/*"
                    hint={videoFile ? `待上传：${videoFile.name}` : editing ? '不重新选择则保留现有视频。' : undefined}
                    label="本地视频文件"
                    onChange={setVideoFile}
                  />
                ) : null}
              </div>
            </EditorSection>

            <EditorSection description="章节完成按钮会实时说明尚未满足的条件。" title="完成条件">
              <div className={styles.stack}>
                <ToggleField
                  checked={policy.requireContentRead ?? true}
                  description="要求学生阅读到章节末尾。"
                  label="要求完成正文阅读"
                  onChange={(checked) =>
                    setDraft((current) => ({
                      ...current,
                      completionPolicy: { ...current.completionPolicy, requireContentRead: checked },
                    }))
                  }
                />
                <ToggleField
                  checked={policy.requireAllRequiredChallenges ?? true}
                  description="标记为必做的实例题必须全部完成。"
                  label="要求完成必做实验"
                  onChange={(checked) =>
                    setDraft((current) => ({
                      ...current,
                      completionPolicy: { ...current.completionPolicy, requireAllRequiredChallenges: checked },
                    }))
                  }
                />
                <div className={styles.twoColumns}>
                  <TextField
                    hint="为 0 时只按必做标记判断。"
                    label="最低实验数量"
                    min={0}
                    onValueChange={(value) =>
                      setDraft((current) => ({
                        ...current,
                        completionPolicy: {
                          ...current.completionPolicy,
                          requiredChallengeCount: Number(value),
                        },
                      }))
                    }
                    type="number"
                    value={policy.requiredChallengeCount ?? 0}
                  />
                  <TextField
                    hint="未配置理论试卷时不会阻塞章节完成。"
                    label="理论通过线 (%)"
                    max={100}
                    min={0}
                    onValueChange={(value) =>
                      setDraft((current) => ({
                        ...current,
                        completionPolicy: {
                          ...current.completionPolicy,
                          theoryPassRate: Number(value),
                        },
                      }))
                    }
                    type="number"
                    value={policy.theoryPassRate ?? 80}
                  />
                </div>
              </div>
            </EditorSection>
          </div>

          <aside className={styles.previewColumn}>
            <header>
              <span>LIVE PREVIEW</span>
              <h2>{draft.title || '未命名章节'}</h2>
              {draft.summary ? <p>{draft.summary}</p> : null}
            </header>
            <MarkdownContent source={draft.content || '暂无章节内容。'} />
          </aside>
        </div>

        {editing ? (
          <EditorSection description="删除章节不会自动删除课程题库中的题目，但会解除章节内容关系。" title="危险操作">
            <div className={styles.deleteRow}>
              <div>
                <strong>删除当前章节</strong>
                <p>已有学习记录或关联内容时，后端可能拒绝删除。</p>
              </div>
              <ActionButton icon={<Trash2 size={16} />} onClick={() => setDeleteOpen(true)} tone="danger" type="button">
                删除章节
              </ActionButton>
            </div>
          </EditorSection>
        ) : null}

        <EditorActionBar status={feedback?.message || '保存成功后返回课程章节列表。'}>
          <ActionButton disabled={saving} icon={<Save size={17} />} tone="primary" type="submit">
            {saving ? '正在保存' : editing ? '保存章节' : '创建章节'}
          </ActionButton>
        </EditorActionBar>
      </form>

      <VNextDialog
        description="该操作不可撤销。建议先将章节设为未发布，确认不再使用后再删除。"
        eyebrow="DANGER ZONE"
        footer={
          <>
            <ActionButton onClick={() => setDeleteOpen(false)} type="button">
              取消
            </ActionButton>
            <ActionButton
              disabled={saving}
              icon={<Trash2 size={16} />}
              onClick={() => void deleteChapter()}
              tone="danger"
              type="button"
            >
              {saving ? '正在删除' : '确认删除'}
            </ActionButton>
          </>
        }
        onClose={() => setDeleteOpen(false)}
        open={deleteOpen}
        title={`删除章节“${chapter?.title ?? ''}”`}
      >
        <InlineFeedback tone="danger">章节正文、完成条件和章节关联将被删除。</InlineFeedback>
      </VNextDialog>
    </TrainingEditorShell>
  )
}
