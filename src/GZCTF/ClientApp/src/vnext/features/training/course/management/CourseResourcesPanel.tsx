import { Download, ExternalLink, FileText, Link2, Pencil, Plus, Trash2 } from 'lucide-react'
import { useMemo, useState } from 'react'
import {
  TrainingCourseModel,
  TrainingCourseResourceEditModel,
  TrainingCourseResourceModel,
  TrainingCourseResourceType,
} from '@Api'
import { FileField, SelectField, TextAreaField, TextField, ToggleField } from '../../../../shared/FormControls'
import { ActionButton, InlineFeedback, VNextDialog } from '../../../../shared/Interaction'
import { DataState, StatusPill } from '../../../../shared/Primitives'
import { errorMessage } from '../../../../shared/errors'
import { safeResourceHref } from '../../../../shared/urls'
import { trainingAdminApi, uploadTrainingAsset } from '../../admin/trainingAdminApi'
import { formatFileSize, formatTrainingDate } from '../../training'
import { CourseManagementPanelHeader } from './CourseManagementPanelHeader'
import styles from './CourseResourcesPanel.module.css'

const emptyDraft = (): TrainingCourseResourceEditModel => ({
  title: '',
  description: '',
  type: TrainingCourseResourceType.File,
  externalUrl: null,
  localFileHash: null,
  order: 1,
  isVisible: true,
})

function resourceTypeLabel(type?: TrainingCourseResourceType) {
  if (type === TrainingCourseResourceType.Link) return '外部链接'
  if (type === TrainingCourseResourceType.Video) return '视频链接'
  return '本地文件'
}

export function CourseResourcesPanel({
  course,
  canOpenLearning,
  onCourseChanged,
}: {
  course: TrainingCourseModel
  canOpenLearning: boolean
  onCourseChanged: () => Promise<unknown>
}) {
  const courseId = course.id ?? 0
  const resources = useMemo(
    () =>
      [...(course.resources ?? [])]
        .filter((resource) => course.canEdit || resource.isVisible !== false)
        .sort((left, right) => (left.order ?? 0) - (right.order ?? 0)),
    [course.canEdit, course.resources]
  )
  const [editOpen, setEditOpen] = useState(false)
  const [editingResource, setEditingResource] = useState<TrainingCourseResourceModel | null>(null)
  const [draft, setDraft] = useState<TrainingCourseResourceEditModel>(emptyDraft)
  const [file, setFile] = useState<File | null>(null)
  const [saving, setSaving] = useState(false)
  const [deleteTarget, setDeleteTarget] = useState<TrainingCourseResourceModel | null>(null)
  const [feedback, setFeedback] = useState<{ tone: 'success' | 'danger'; message: string } | null>(null)

  const updateDraftField = <Key extends keyof TrainingCourseResourceEditModel>(
    field: Key,
    value: TrainingCourseResourceEditModel[Key]
  ) => {
    setDraft((current) => ({ ...current, [field]: value }))
  }

  const openCreate = () => {
    setEditingResource(null)
    setDraft({ ...emptyDraft(), order: resources.length + 1 })
    setFile(null)
    setEditOpen(true)
  }

  const openEdit = (resource: TrainingCourseResourceModel) => {
    setEditingResource(resource)
    setDraft({
      title: resource.title ?? '',
      description: resource.description ?? '',
      type: resource.type ?? TrainingCourseResourceType.File,
      externalUrl: resource.externalUrl ?? null,
      localFileHash: null,
      order: resource.order ?? 1,
      isVisible: resource.isVisible ?? true,
    })
    setFile(null)
    setEditOpen(true)
  }

  const save = async () => {
    const title = draft.title.trim()
    if (!title || saving) return
    setSaving(true)
    setFeedback(null)
    try {
      const localFileHash =
        draft.type === TrainingCourseResourceType.File && file ? await uploadTrainingAsset(file) : draft.localFileHash
      const payload: TrainingCourseResourceEditModel = {
        ...draft,
        title,
        description: draft.description?.trim() || '',
        externalUrl: draft.type === TrainingCourseResourceType.File ? null : draft.externalUrl?.trim() || null,
        localFileHash: draft.type === TrainingCourseResourceType.File ? (localFileHash ?? null) : null,
        order: Math.max(1, Number(draft.order) || 1),
      }
      if (editingResource?.id) {
        await trainingAdminApi.updateResource(courseId, editingResource.id, payload)
      } else {
        await trainingAdminApi.createResource(courseId, payload)
      }
      await onCourseChanged()
      setEditOpen(false)
      setFeedback({ tone: 'success', message: editingResource ? '课程资源已更新。' : '课程资源已添加。' })
    } catch (requestError) {
      setFeedback({ tone: 'danger', message: errorMessage(requestError, '课程资源保存失败。') })
    } finally {
      setSaving(false)
    }
  }

  const deleteResource = async () => {
    if (!deleteTarget?.id || saving) return
    setSaving(true)
    try {
      await trainingAdminApi.deleteResource(courseId, deleteTarget.id)
      await onCourseChanged()
      setDeleteTarget(null)
      setFeedback({ tone: 'success', message: '课程资源已删除。' })
    } catch (requestError) {
      setFeedback({ tone: 'danger', message: errorMessage(requestError, '课程资源删除失败。') })
    } finally {
      setSaving(false)
    }
  }

  return (
    <section className={styles.panel}>
      <CourseManagementPanelHeader
        actions={
          course.canEdit ? (
            <ActionButton icon={<Plus size={17} />} onClick={openCreate} type="button">
              添加资源
            </ActionButton>
          ) : null
        }
        description={canOpenLearning ? '资源可直接打开或下载。' : '报名通过后开放资源下载。'}
        eyebrow="COURSE MATERIALS"
        title="课程资源"
      />
      {feedback ? <InlineFeedback tone={feedback.tone}>{feedback.message}</InlineFeedback> : null}
      {resources.length ? (
        <div className={styles.resourceList}>
          {resources.map((resource) => {
            const href = safeResourceHref(resource.downloadUrl || resource.externalUrl)
            const external = resource.type !== TrainingCourseResourceType.File
            return (
              <article key={resource.id}>
                <span className={styles.resourceIcon}>{external ? <Link2 size={18} /> : <FileText size={18} />}</span>
                <div className={styles.resourceIdentity}>
                  <strong>{resource.title || resource.fileName || '未命名资源'}</strong>
                  <small>{resource.description || resource.fileName || resource.externalUrl || '暂无说明'}</small>
                </div>
                <StatusPill tone={resource.isVisible === false ? 'warning' : 'neutral'}>
                  {resource.isVisible === false ? '已隐藏' : resourceTypeLabel(resource.type)}
                </StatusPill>
                <span className={styles.resourceMeta}>
                  {formatFileSize(resource.fileSize)}
                  <small>{formatTrainingDate(resource.createdAt)}</small>
                </span>
                <div className={styles.resourceActions}>
                  {canOpenLearning && href ? (
                    <a
                      aria-label={external ? '打开资源' : '下载资源'}
                      href={href}
                      rel="noreferrer noopener"
                      target="_blank"
                      title={external ? '打开资源' : '下载资源'}
                    >
                      {external ? <ExternalLink size={16} /> : <Download size={16} />}
                    </a>
                  ) : null}
                  {course.canEdit ? (
                    <>
                      <button aria-label="编辑资源" onClick={() => openEdit(resource)} title="编辑资源" type="button">
                        <Pencil size={16} />
                      </button>
                      <button
                        aria-label="删除资源"
                        onClick={() => setDeleteTarget(resource)}
                        title="删除资源"
                        type="button"
                      >
                        <Trash2 size={16} />
                      </button>
                    </>
                  ) : null}
                </div>
              </article>
            )
          })}
        </div>
      ) : (
        <DataState description="课程教师尚未提供课程级资源。" title="暂无课程资源" />
      )}

      <VNextDialog
        description="本地文件会先上传到平台资源存储，再与当前课程绑定。"
        eyebrow="COURSE MATERIAL"
        footer={
          <>
            <ActionButton onClick={() => setEditOpen(false)} type="button">
              取消
            </ActionButton>
            <ActionButton
              disabled={saving || !draft.title.trim()}
              onClick={() => void save()}
              tone="primary"
              type="button"
            >
              {saving ? '正在保存' : editingResource ? '保存修改' : '添加资源'}
            </ActionButton>
          </>
        }
        onClose={() => setEditOpen(false)}
        open={editOpen}
        title={editingResource ? '编辑课程资源' : '添加课程资源'}
        wide
      >
        <div className={styles.formGrid}>
          <TextField
            label="资源名称"
            maxLength={128}
            onValueChange={(value) => updateDraftField('title', value)}
            required
            value={draft.title}
          />
          <SelectField
            label="资源类型"
            onValueChange={(value) => updateDraftField('type', value as TrainingCourseResourceType)}
            value={draft.type}
          >
            <option value={TrainingCourseResourceType.File}>本地文件</option>
            <option value={TrainingCourseResourceType.Link}>外部链接</option>
            <option value={TrainingCourseResourceType.Video}>视频链接</option>
          </SelectField>
          <TextAreaField
            label="资源说明"
            maxLength={512}
            onValueChange={(value) => updateDraftField('description', value)}
            rows={3}
            value={draft.description}
          />
          {draft.type === TrainingCourseResourceType.File ? (
            <FileField
              hint={
                file
                  ? `待上传：${file.name}`
                  : editingResource?.fileName
                    ? `当前文件：${editingResource.fileName}`
                    : undefined
              }
              label="资源文件"
              onChange={setFile}
            />
          ) : (
            <TextField
              label="资源地址"
              onValueChange={(value) => updateDraftField('externalUrl', value)}
              placeholder="https://..."
              type="url"
              value={draft.externalUrl ?? ''}
            />
          )}
          <TextField
            label="排序"
            min={1}
            onValueChange={(value) => updateDraftField('order', Number(value))}
            type="number"
            value={draft.order ?? 1}
          />
          <ToggleField
            checked={draft.isVisible ?? true}
            description="隐藏资源仍保留在课程中，但学生无法下载。"
            label="学生可见"
            onChange={(checked) => setDraft((current) => ({ ...current, isVisible: checked }))}
          />
        </div>
      </VNextDialog>

      <VNextDialog
        description="删除后学生将无法继续打开或下载该资源。"
        eyebrow="DELETE MATERIAL"
        footer={
          <>
            <ActionButton onClick={() => setDeleteTarget(null)} type="button">
              取消
            </ActionButton>
            <ActionButton
              disabled={saving}
              icon={<Trash2 size={16} />}
              onClick={() => void deleteResource()}
              tone="danger"
              type="button"
            >
              {saving ? '正在删除' : '确认删除'}
            </ActionButton>
          </>
        }
        onClose={() => setDeleteTarget(null)}
        open={Boolean(deleteTarget)}
        title={`删除资源“${deleteTarget?.title ?? ''}”`}
      >
        <InlineFeedback tone="danger">资源绑定会被永久移除，本地上传文件是否清理由平台资源策略决定。</InlineFeedback>
      </VNextDialog>
    </section>
  )
}
