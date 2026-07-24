import { Box, Database, HardDriveUpload, PackagePlus, RefreshCw, Server, Trash2, Upload } from 'lucide-react'
import { useEffect, useMemo, useState } from 'react'
import { ImageStatus, ImageType, OSType, TrainingCourseImageTemplateModel, TrainingCourseModel } from '@Api'
import { TextField } from '../../../../shared/FormControls'
import { ActionButton, InlineFeedback } from '../../../../shared/Interaction'
import { DataState, StatusPill } from '../../../../shared/Primitives'
import { errorMessage } from '../../../../shared/errors'
import { useTrainingAdminImageTemplates } from '../../admin/trainingAdminApi'
import { CourseRegistrySummary, courseEnvironmentApi } from '../../api/courseEnvironmentApi'
import { formatFileSize, formatTrainingDate } from '../../training'
import {
  CourseEnvironmentDialog,
  CourseEnvironmentDialogs,
  CourseEnvironmentFeedback,
} from './CourseEnvironmentDialogs'
import styles from './CourseEnvironmentPanel.module.css'
import { CourseManagementPanelHeader } from './CourseManagementPanelHeader'

function imageTypeLabel(type?: ImageType) {
  if (type === ImageType.Docker) return 'Docker'
  if (type === ImageType.Qcow2) return 'QCOW2'
  if (type === ImageType.Ova) return 'OVA'
  if (type === ImageType.Vmdk) return 'VMDK'
  return '未知格式'
}

function statusInfo(status?: ImageStatus) {
  if (status === ImageStatus.Ready) return { label: '就绪', tone: 'success' as const }
  if (status === ImageStatus.Importing) return { label: '导入中', tone: 'info' as const }
  if (status === ImageStatus.Error) return { label: '异常', tone: 'warning' as const }
  return { label: '删除中', tone: 'neutral' as const }
}

export function CourseEnvironmentPanel({ course }: { course: TrainingCourseModel }) {
  const courseId = course.id ?? 0
  const templatesRequest = useTrainingAdminImageTemplates(courseId, Boolean(course.canEdit && courseId))
  const [registry, setRegistry] = useState<CourseRegistrySummary | null>(null)
  const [query, setQuery] = useState('')
  const [dialog, setDialog] = useState<CourseEnvironmentDialog>(null)
  const [detaching, setDetaching] = useState(false)
  const [feedback, setFeedback] = useState<CourseEnvironmentFeedback | null>(null)

  useEffect(() => {
    if (!course.canEdit || !courseId) return
    let cancelled = false
    void courseEnvironmentApi
      .registry(courseId)
      .then((data) => {
        if (!cancelled) setRegistry(data)
      })
      .catch(() => {
        if (!cancelled) setRegistry(null)
      })
    return () => {
      cancelled = true
    }
  }, [course.canEdit, courseId])

  const templates = useMemo(() => {
    const keyword = query.trim().toLocaleLowerCase('zh-CN')
    return (templatesRequest.data ?? []).filter((template) => {
      if (!keyword) return true
      return [template.name, template.registryUrl, template.imageHash, template.description, template.id]
        .filter(Boolean)
        .some((value) => String(value).toLocaleLowerCase('zh-CN').includes(keyword))
    })
  }, [query, templatesRequest.data])

  const detach = async (template: TrainingCourseImageTemplateModel) => {
    if (!template.id || detaching) return
    setDetaching(true)
    try {
      await courseEnvironmentApi.detach(courseId, template)
      await templatesRequest.mutate()
      setFeedback({ tone: 'success', message: '环境模板已从当前课程解除。' })
    } catch (requestError) {
      setFeedback({ tone: 'danger', message: errorMessage(requestError, '环境模板解除失败。') })
    } finally {
      setDetaching(false)
    }
  }

  if (!course.canEdit)
    return <DataState description="只有课程教师和管理员可以管理课程环境模板。" title="无法管理环境模板" />

  return (
    <section className={styles.panel}>
      <CourseManagementPanelHeader
        actions={
          <>
            <ActionButton icon={<PackagePlus size={16} />} onClick={() => setDialog('register')} type="button">
              注册 Docker
            </ActionButton>
            <ActionButton icon={<Upload size={16} />} onClick={() => setDialog('docker')} type="button">
              上传 Docker 包
            </ActionButton>
            <ActionButton icon={<HardDriveUpload size={16} />} onClick={() => setDialog('vm')} type="button">
              上传 VM
            </ActionButton>
            <ActionButton icon={<Database size={16} />} onClick={() => setDialog('local')} type="button">
              服务器导入
            </ActionButton>
          </>
        }
        description="这里只展示当前课程可用的镜像，比赛模块和其他课程无法直接访问。"
        eyebrow="COURSE ENVIRONMENTS"
        title="环境模板"
      />
      <div className={styles.registryBar}>
        <Server size={18} />
        <span>
          {registry?.enabled
            ? `课程镜像将同步至 ${registry.address ?? '内网 Registry'}${registry.namespace ? `/${registry.namespace}` : ''}`
            : '当前未检测到可用的内网 Docker Registry，注册和上传 Docker 镜像可能失败。'}
        </span>
        <ActionButton icon={<RefreshCw size={15} />} onClick={() => void templatesRequest.mutate()} type="button">
          刷新
        </ActionButton>
      </div>
      {feedback ? <InlineFeedback tone={feedback.tone}>{feedback.message}</InlineFeedback> : null}
      <TextField
        label="搜索课程模板"
        onValueChange={setQuery}
        placeholder="名称、镜像地址、Hash 或编号"
        value={query}
      />
      {!templatesRequest.data && !templatesRequest.error ? (
        <DataState description="正在读取当前课程的镜像模板。" loading title="环境模板加载中" />
      ) : templatesRequest.error ? (
        <DataState description="环境模板接口暂时不可用。" title="环境模板加载失败" />
      ) : templates.length ? (
        <div className={styles.templateGrid}>
          {templates.map((template) => {
            const state = statusInfo(template.status)
            return (
              <article key={template.id}>
                <header>
                  <span className={styles.templateIcon}>
                    {template.imageType === ImageType.Docker ? <Box size={19} /> : <Server size={19} />}
                  </span>
                  <div>
                    <strong>{template.name || `模板 ${template.id}`}</strong>
                    <small>
                      #{template.id} · {imageTypeLabel(template.imageType)} ·{' '}
                      {template.osType === OSType.Windows ? 'Windows' : 'Linux'}
                    </small>
                  </div>
                  <StatusPill tone={state.tone}>{state.label}</StatusPill>
                </header>
                <p>{template.description || template.registryUrl || template.imageHash || '暂无模板说明。'}</p>
                {template.errorMessage ? <InlineFeedback tone="danger">{template.errorMessage}</InlineFeedback> : null}
                <footer>
                  <span>
                    {formatFileSize(template.fileSize)} · {formatTrainingDate(template.uploadedAt)}
                  </span>
                  <button
                    aria-label="解除课程模板"
                    disabled={detaching}
                    onClick={() => void detach(template)}
                    title="从当前课程解除"
                    type="button"
                  >
                    <Trash2 size={16} />
                  </button>
                </footer>
              </article>
            )
          })}
        </div>
      ) : (
        <DataState description="注册或上传镜像后，模板会只绑定到当前课程。" title="暂无课程环境模板" />
      )}

      <CourseEnvironmentDialogs
        courseId={courseId}
        dialog={dialog}
        onChanged={templatesRequest.mutate}
        onClose={() => setDialog(null)}
        onFeedback={setFeedback}
      />
    </section>
  )
}
