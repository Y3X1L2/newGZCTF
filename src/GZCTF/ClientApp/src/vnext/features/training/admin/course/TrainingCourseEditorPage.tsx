import { Archive, Save, Send, Trash2, Undo2 } from 'lucide-react'
import { FormEvent, useEffect, useMemo, useState } from 'react'
import { useNavigate, useParams } from 'react-router'
import api, { TrainingCourseEditModel, TrainingCourseEnrollmentPolicy, TrainingCourseStatus } from '@Api'
import { FileField, SelectField, TextAreaField, TextField } from '../../../../shared/FormControls'
import { ActionButton, InlineFeedback, VNextDialog } from '../../../../shared/Interaction'
import { MarkdownContent } from '../../../../shared/MarkdownContent'
import { DataState, GeometricPoster, StatusPill } from '../../../../shared/Primitives'
import { errorMessage } from '../../../../shared/errors'
import { useVNextPageTitle } from '../../../../shared/useVNextPageTitle'
import { useCurrentAccount } from '../../../account/useCurrentAccount'
import { courseStatusLabel, courseStatusTone } from '../../training'
import { EditorActionBar, EditorSection, TrainingEditorShell } from '../TrainingEditorShell'
import { trainingAdminApi, uploadTrainingAsset } from '../trainingAdminApi'
import styles from './TrainingCourseEditorPage.module.css'

const emptyDraft = (): TrainingCourseEditModel => ({
  title: '',
  slug: '',
  summary: '',
  description: '',
  coverFileHash: null,
  tags: [],
  enrollmentPolicy: TrainingCourseEnrollmentPolicy.TeacherApproval,
})

export function TrainingCourseEditorPage() {
  const { courseId } = useParams()
  const navigate = useNavigate()
  const account = useCurrentAccount()
  const id = Number(courseId)
  const editing = Number.isInteger(id) && id > 0
  const courseRequest = api.trainingCourseAdmin.useTrainingCourseAdminCourse(id, { revalidateOnFocus: false }, editing)
  const course = courseRequest.data
  const [draft, setDraft] = useState<TrainingCourseEditModel>(emptyDraft)
  const [tagsText, setTagsText] = useState('')
  const [coverFile, setCoverFile] = useState<File | null>(null)
  const [saving, setSaving] = useState(false)
  const [changingStatus, setChangingStatus] = useState(false)
  const [deleteOpen, setDeleteOpen] = useState(false)
  const [feedback, setFeedback] = useState<{ tone: 'success' | 'danger'; message: string } | null>(null)
  const [validationError, setValidationError] = useState<string | null>(null)

  useVNextPageTitle(editing ? `编辑 ${course?.title || '课程'}` : '创建课程')

  useEffect(() => {
    if (!course) return
    setDraft({
      title: course.title ?? '',
      slug: course.slug ?? '',
      summary: course.summary ?? '',
      description: course.description ?? '',
      coverFileHash: course.coverFileHash ?? null,
      tags: course.tags ?? [],
      enrollmentPolicy: course.enrollmentPolicy ?? TrainingCourseEnrollmentPolicy.TeacherApproval,
    })
    setTagsText((course.tags ?? []).join('、'))
  }, [course])

  const parsedTags = useMemo(
    () =>
      tagsText
        .split(/[，,、]/)
        .map((tag) => tag.trim())
        .filter((tag, index, list) => Boolean(tag) && list.indexOf(tag) === index),
    [tagsText]
  )

  const updateDraftField = <Key extends keyof TrainingCourseEditModel>(
    field: Key,
    value: TrainingCourseEditModel[Key]
  ) => {
    setDraft((current) => ({ ...current, [field]: value }))
  }

  const save = async (event?: FormEvent) => {
    event?.preventDefault()
    const title = draft.title.trim()
    if (!title) {
      setValidationError('请输入课程名称。')
      return
    }

    setSaving(true)
    setFeedback(null)
    setValidationError(null)
    try {
      const coverFileHash = coverFile ? await uploadTrainingAsset(coverFile) : draft.coverFileHash
      const payload: TrainingCourseEditModel = {
        ...draft,
        title,
        slug: draft.slug?.trim() || '',
        summary: draft.summary?.trim() || '',
        description: draft.description || '',
        coverFileHash,
        tags: parsedTags,
      }

      if (editing) {
        await trainingAdminApi.updateCourse(id, payload)
        await courseRequest.mutate()
        setDraft(payload)
        setCoverFile(null)
        setFeedback({ tone: 'success', message: '课程信息已保存。' })
      } else {
        const created = await trainingAdminApi.createCourse(payload)
        if (!created?.id) throw new Error('课程已创建，但服务器没有返回课程编号。')
        navigate(`/training/courses/${created.id}/edit`, { replace: true })
      }
    } catch (requestError) {
      setFeedback({ tone: 'danger', message: errorMessage(requestError, '课程保存失败。') })
    } finally {
      setSaving(false)
    }
  }

  const changeStatus = async (status: TrainingCourseStatus) => {
    if (!course?.id || changingStatus) return
    setChangingStatus(true)
    setFeedback(null)
    try {
      if (status === TrainingCourseStatus.Published) await trainingAdminApi.publishCourse(course.id)
      else if (status === TrainingCourseStatus.Archived) await trainingAdminApi.archiveCourse(course.id)
      else await trainingAdminApi.moveCourseToDraft(course.id)
      await courseRequest.mutate()
      setFeedback({ tone: 'success', message: '课程状态已更新。' })
    } catch (requestError) {
      setFeedback({ tone: 'danger', message: errorMessage(requestError, '课程状态更新失败。') })
    } finally {
      setChangingStatus(false)
    }
  }

  const deleteCourse = async () => {
    if (!course?.id || !course.canDelete || changingStatus) return
    setChangingStatus(true)
    setFeedback(null)
    try {
      await trainingAdminApi.deleteCourse(course.id)
      navigate('/training', { replace: true })
    } catch (requestError) {
      setFeedback({ tone: 'danger', message: errorMessage(requestError, '课程删除失败。') })
      setDeleteOpen(false)
      setChangingStatus(false)
    }
  }

  if (!editing && !account.isTeacher && !account.error) {
    return <DataState description="只有教师、管理员和超级管理员可以创建课程。" title="没有课程创建权限" />
  }
  if (editing && !course && !courseRequest.error) {
    return <DataState description="正在读取课程配置与权限。" loading title="课程加载中" />
  }
  if (editing && (!course || !course.canEdit)) {
    return <DataState description="课程不存在，或当前账户没有编辑该课程的权限。" title="无法编辑课程" />
  }

  const backTo = editing ? `/training/courses/${id}` : '/training'

  return (
    <TrainingEditorShell
      actions={
        editing && course ? (
          <>
            {course.status !== TrainingCourseStatus.Draft ? (
              <ActionButton
                disabled={changingStatus}
                icon={<Undo2 size={16} />}
                onClick={() => void changeStatus(TrainingCourseStatus.Draft)}
                type="button"
              >
                转为草稿
              </ActionButton>
            ) : null}
            {course.status !== TrainingCourseStatus.Published ? (
              <ActionButton
                disabled={changingStatus}
                icon={<Send size={16} />}
                onClick={() => void changeStatus(TrainingCourseStatus.Published)}
                tone="primary"
                type="button"
              >
                发布课程
              </ActionButton>
            ) : null}
          </>
        ) : null
      }
      backLabel={editing ? '返回课程详情' : '返回培训'}
      backTo={backTo}
      description="课程是章节、资源、实验和理论作业的权限边界。先保存基本信息，再进入课程详情维护教学内容。"
      eyebrow="COURSE WORKSPACE"
      meta={course ? <StatusPill tone={courseStatusTone(course)}>{courseStatusLabel(course)}</StatusPill> : null}
      title={editing ? '编辑课程' : '创建课程'}
    >
      {feedback ? <InlineFeedback tone={feedback.tone}>{feedback.message}</InlineFeedback> : null}
      <form className={styles.form} onSubmit={(event) => void save(event)}>
        <EditorSection description="用于课程目录、搜索结果和课程详情头部。" title="课程身份">
          <div className={styles.fieldGrid}>
            <TextField
              error={validationError}
              label="课程名称"
              maxLength={128}
              onValueChange={(value) => updateDraftField('title', value)}
              placeholder="例如：Web 安全基础"
              required
              value={draft.title}
            />
            <TextField
              hint="用于可读 URL 或外部系统识别，可留空。"
              label="课程标识"
              maxLength={128}
              onValueChange={(value) => updateDraftField('slug', value)}
              placeholder="web-security-basics"
              value={draft.slug}
            />
            <TextField
              hint="使用逗号或顿号分隔，重复标签会自动去除。"
              label="课程标签"
              onValueChange={setTagsText}
              placeholder="CTF、Web、入门"
              value={tagsText}
            />
            <SelectField
              hint="教师审核适合正式培训；自动通过适合公开自学课程。"
              label="报名策略"
              onValueChange={(value) => updateDraftField('enrollmentPolicy', value as TrainingCourseEnrollmentPolicy)}
              value={draft.enrollmentPolicy}
            >
              <option value={TrainingCourseEnrollmentPolicy.TeacherApproval}>教师审核</option>
              <option value={TrainingCourseEnrollmentPolicy.AutoApprove}>自动通过</option>
            </SelectField>
          </div>
        </EditorSection>

        <EditorSection description="摘要用于卡片，详细介绍支持 Markdown。" title="课程说明">
          <div className={styles.stack}>
            <TextAreaField
              label="课程摘要"
              maxLength={512}
              onValueChange={(value) => updateDraftField('summary', value)}
              placeholder="用两到三句话说明课程目标和适合人群。"
              rows={4}
              value={draft.summary}
            />
            <div className={styles.markdownGrid}>
              <TextAreaField
                label="课程介绍 Markdown"
                onValueChange={(value) => updateDraftField('description', value)}
                placeholder="## 课程目标"
                rows={18}
                value={draft.description}
              />
              <article className={styles.preview}>
                <header>实时预览</header>
                <MarkdownContent source={draft.description || '暂无课程介绍。'} />
              </article>
            </div>
          </div>
        </EditorSection>

        <EditorSection description="课程海报统一使用 16:9，建议上传 WebP、PNG 或 JPEG。" title="课程海报">
          <div className={styles.coverGrid}>
            <div className={styles.coverPreview}>
              <GeometricPoster alt="课程海报预览" src={course?.coverUrl} tone="blue" />
            </div>
            <div className={styles.stack}>
              <FileField
                accept="image/png,image/jpeg,image/webp"
                hint={
                  coverFile
                    ? `待上传：${coverFile.name}`
                    : course?.coverFileHash
                      ? '已使用当前课程海报。'
                      : '未上传时使用平台几何占位图。'
                }
                label="选择海报文件"
                onChange={setCoverFile}
              />
              {coverFile ? <InlineFeedback>新海报会在保存课程时上传并生效。</InlineFeedback> : null}
            </div>
          </div>
        </EditorSection>

        {editing && course ? (
          <EditorSection description="归档会停止新报名；删除操作仅在后端允许时开放。" title="生命周期">
            <div className={styles.dangerRow}>
              <div>
                <strong>归档或删除课程</strong>
                <p>归档保留全部教学数据；删除不可撤销，并可能受到已有学习记录限制。</p>
              </div>
              <div>
                {course.status !== TrainingCourseStatus.Archived ? (
                  <ActionButton
                    disabled={changingStatus}
                    icon={<Archive size={16} />}
                    onClick={() => void changeStatus(TrainingCourseStatus.Archived)}
                    type="button"
                  >
                    归档课程
                  </ActionButton>
                ) : null}
                {course.canDelete ? (
                  <ActionButton
                    icon={<Trash2 size={16} />}
                    onClick={() => setDeleteOpen(true)}
                    tone="danger"
                    type="button"
                  >
                    删除课程
                  </ActionButton>
                ) : null}
              </div>
            </div>
          </EditorSection>
        ) : null}

        <EditorActionBar
          status={feedback?.message || (editing ? '保存后保留在当前编辑页面。' : '新课程将以草稿状态创建。')}
        >
          <ActionButton disabled={saving} icon={<Save size={17} />} tone="primary" type="submit">
            {saving ? '正在保存' : editing ? '保存课程' : '创建课程'}
          </ActionButton>
        </EditorActionBar>
      </form>

      <VNextDialog
        description="课程及其章节、资源、题目和学习配置将被删除。已有学习记录时后端可能拒绝本操作。"
        eyebrow="DANGER ZONE"
        footer={
          <>
            <ActionButton onClick={() => setDeleteOpen(false)} type="button">
              取消
            </ActionButton>
            <ActionButton
              disabled={changingStatus}
              icon={<Trash2 size={16} />}
              onClick={() => void deleteCourse()}
              tone="danger"
              type="button"
            >
              {changingStatus ? '正在删除' : '确认删除'}
            </ActionButton>
          </>
        }
        onClose={() => setDeleteOpen(false)}
        open={deleteOpen}
        title={`删除课程“${course?.title ?? ''}”`}
      >
        <InlineFeedback tone="danger">该操作不可撤销。归档通常是更适合保留历史教学数据的选择。</InlineFeedback>
      </VNextDialog>
    </TrainingEditorShell>
  )
}
