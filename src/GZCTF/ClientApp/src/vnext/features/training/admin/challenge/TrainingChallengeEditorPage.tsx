import { Save } from 'lucide-react'
import { FormEvent, useEffect, useMemo, useState } from 'react'
import { useNavigate, useParams } from 'react-router'
import {
  ChallengeCategory,
  ChallengeType,
  EnvironmentType,
  FileType,
  ImageStatus,
  ImageType,
  NetworkMode,
  OSType,
  TrainingCourseChallengeCreateModel,
  TrainingCourseImageTemplateModel,
} from '@Api'
import { FileField, SelectField, TextAreaField, TextField, ToggleField } from '../../../../shared/FormControls'
import { ActionButton, InlineFeedback } from '../../../../shared/Interaction'
import { MarkdownContent } from '../../../../shared/MarkdownContent'
import { DataState, StatusPill } from '../../../../shared/Primitives'
import { errorMessage } from '../../../../shared/errors'
import { useVNextPageTitle } from '../../../../shared/useVNextPageTitle'
import { EditorActionBar, EditorSection, TrainingEditorShell } from '../TrainingEditorShell'
import {
  trainingAdminApi,
  uploadTrainingAsset,
  useTrainingAdminChallenge,
  useTrainingAdminCourse,
  useTrainingAdminImageTemplates,
} from '../trainingAdminApi'
import styles from './TrainingChallengeEditorPage.module.css'

const emptyDraft = (): TrainingCourseChallengeCreateModel => ({
  title: '',
  content: '',
  category: ChallengeCategory.Web,
  type: ChallengeType.StaticAttachment,
  environment: EnvironmentType.None,
  imageTemplateId: null,
  containerImage: '',
  memoryLimit: 128,
  cpuCount: 1,
  storageLimit: 256,
  exposePort: 80,
  networkMode: NetworkMode.Open,
  flagTemplate: 'flag{[TEAM_HASH]}',
  staticFlag: '',
  submissionLimit: 0,
  chapterId: null,
  order: 1,
  isRequired: true,
  displayTitle: null,
  attachmentType: FileType.None,
  attachmentFileHash: null,
  attachmentRemoteUrl: null,
})

function templateMatchesEnvironment(template: TrainingCourseImageTemplateModel, environment?: EnvironmentType) {
  if (template.status !== ImageStatus.Ready) return false
  if (environment === EnvironmentType.Docker) return template.imageType === ImageType.Docker
  if (environment === EnvironmentType.WindowsVM) return template.osType === OSType.Windows
  return false
}

export function TrainingChallengeEditorPage() {
  const { courseId, challengeId } = useParams()
  const navigate = useNavigate()
  const courseNumber = Number(courseId)
  const challengeNumber = Number(challengeId)
  const validCourse = Number.isInteger(courseNumber) && courseNumber > 0
  const editing = Number.isInteger(challengeNumber) && challengeNumber > 0
  const courseRequest = useTrainingAdminCourse(courseNumber, validCourse)
  const detailRequest = useTrainingAdminChallenge(courseNumber, challengeNumber, validCourse && editing)
  const templatesRequest = useTrainingAdminImageTemplates(courseNumber, validCourse)
  const course = courseRequest.data
  const detail = detailRequest.data
  const [draft, setDraft] = useState<TrainingCourseChallengeCreateModel>(emptyDraft)
  const [attachmentFile, setAttachmentFile] = useState<File | null>(null)
  const [saving, setSaving] = useState(false)
  const [feedback, setFeedback] = useState<{ tone: 'success' | 'danger'; message: string } | null>(null)
  const [titleError, setTitleError] = useState<string | null>(null)

  useVNextPageTitle(editing ? `编辑 ${detail?.title || '课程题目'}` : '创建课程题目')

  useEffect(() => {
    if (!course || editing) return
    setDraft((current) => ({ ...current, order: (course.challenges?.length ?? 0) + 1 }))
  }, [course, editing])

  useEffect(() => {
    if (!detail || !editing) return
    setDraft({ ...emptyDraft(), ...detail })
  }, [detail, editing])

  const availableTemplates = useMemo(
    () => (templatesRequest.data ?? []).filter((template) => templateMatchesEnvironment(template, draft.environment)),
    [draft.environment, templatesRequest.data]
  )
  const dynamicFlag = draft.type === ChallengeType.DynamicContainer || draft.type === ChallengeType.DynamicAttachment

  const updateDraftField = <Key extends keyof TrainingCourseChallengeCreateModel>(
    field: Key,
    value: TrainingCourseChallengeCreateModel[Key]
  ) => {
    setDraft((current) => ({ ...current, [field]: value }))
  }

  const setEnvironment = (environment: EnvironmentType) => {
    setDraft((current) => ({
      ...current,
      environment,
      imageTemplateId: null,
      containerImage: environment === EnvironmentType.None ? null : current.containerImage,
    }))
  }

  const save = async (event?: FormEvent) => {
    event?.preventDefault()
    const title = draft.title.trim()
    if (!title) {
      setTitleError('请输入题目名称。')
      return
    }
    setSaving(true)
    setFeedback(null)
    setTitleError(null)
    try {
      const attachmentFileHash =
        draft.attachmentType === FileType.Local && attachmentFile
          ? await uploadTrainingAsset(attachmentFile)
          : draft.attachmentFileHash
      const payload: TrainingCourseChallengeCreateModel = {
        ...draft,
        title,
        displayTitle: draft.displayTitle?.trim() || null,
        content: draft.content || '',
        containerImage: draft.environment === EnvironmentType.Docker ? draft.containerImage?.trim() || null : null,
        attachmentType: draft.attachmentType ?? FileType.None,
        attachmentFileHash: draft.attachmentType === FileType.Local ? (attachmentFileHash ?? null) : null,
        attachmentRemoteUrl:
          draft.attachmentType === FileType.Remote ? draft.attachmentRemoteUrl?.trim() || null : null,
        flagTemplate: dynamicFlag ? draft.flagTemplate?.trim() || null : null,
        staticFlag: dynamicFlag ? null : draft.staticFlag?.trim() || null,
        order: Math.max(1, Number(draft.order) || 1),
        submissionLimit: Math.max(0, Number(draft.submissionLimit) || 0),
      }
      if (editing) {
        await trainingAdminApi.updateChallenge(courseNumber, challengeNumber, payload)
      } else {
        await trainingAdminApi.createChallenge(courseNumber, payload)
      }
      navigate(`/training/courses/${courseNumber}?tab=challenges`, { replace: true })
    } catch (requestError) {
      setFeedback({ tone: 'danger', message: errorMessage(requestError, '课程题目保存失败。') })
    } finally {
      setSaving(false)
    }
  }

  if (!validCourse) return <DataState description="课程编号不是有效数字。" title="课程参数错误" />
  if (!course || (editing && !detail)) {
    if (!courseRequest.error && (!editing || !detailRequest.error)) {
      return <DataState description="正在读取课程题目和环境模板。" loading title="题目编辑器加载中" />
    }
    return <DataState description="课程或题目不存在，或当前账户没有访问权限。" title="无法打开题目编辑器" />
  }
  if (!course.canEdit)
    return <DataState description="只有课程教师和管理员可以维护课程题目。" title="没有题目编辑权限" />

  return (
    <TrainingEditorShell
      backLabel="返回题目管理"
      backTo={`/training/courses/${courseNumber}?tab=challenges`}
      description="题目属于当前课程，可绑定课程镜像、一个静态附件和一个 Flag 判定规则。"
      eyebrow="COURSE CHALLENGE"
      meta={detail?.hasSubmittedAnswers ? <StatusPill tone="warning">已有学员提交记录</StatusPill> : null}
      title={editing ? '编辑课程题目' : '创建课程题目'}
    >
      {feedback ? <InlineFeedback tone={feedback.tone}>{feedback.message}</InlineFeedback> : null}
      <form className={styles.form} onSubmit={(event) => void save(event)}>
        <EditorSection description="题目名称、分类和章节关系决定学生端的展示位置。" title="题目身份">
          <div className={styles.fieldGrid}>
            <TextField
              error={titleError}
              label="内部名称"
              maxLength={128}
              onValueChange={(value) => updateDraftField('title', value)}
              required
              value={draft.title}
            />
            <TextField
              hint="留空时使用内部名称。"
              label="学生展示名称"
              maxLength={128}
              onValueChange={(value) => updateDraftField('displayTitle', value)}
              value={draft.displayTitle ?? ''}
            />
            <SelectField
              label="题目分类"
              onValueChange={(value) => updateDraftField('category', value as ChallengeCategory)}
              value={draft.category}
            >
              {Object.values(ChallengeCategory).map((category) => (
                <option key={category} value={category}>
                  {category}
                </option>
              ))}
            </SelectField>
            <SelectField
              label="绑定章节"
              onValueChange={(value) =>
                setDraft((current) => ({
                  ...current,
                  chapterId: value ? Number(value) : null,
                }))
              }
              value={draft.chapterId ?? ''}
            >
              <option value="">暂不绑定章节</option>
              {(course.chapters ?? []).map((chapter) => (
                <option key={chapter.id} value={chapter.id}>
                  {chapter.title}
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
            <ToggleField
              checked={draft.isRequired ?? true}
              description="必做题会参与章节完成条件。"
              label="必做题"
              onChange={(checked) => setDraft((current) => ({ ...current, isRequired: checked }))}
            />
          </div>
        </EditorSection>

        <EditorSection description="题目说明使用与学生端一致的 Markdown 渲染器。" title="题目内容">
          <div className={styles.markdownGrid}>
            <TextAreaField
              label="题目说明 Markdown"
              onValueChange={(value) => updateDraftField('content', value)}
              rows={22}
              value={draft.content}
            />
            <article className={styles.preview}>
              <header>实时预览</header>
              <MarkdownContent source={draft.content || '暂无题目说明。'} />
            </article>
          </div>
        </EditorSection>

        <EditorSection description="首阶段每道课程题目只绑定一个本地附件或一个外部链接。" title="题目附件">
          <div className={styles.fieldGrid}>
            <SelectField
              label="附件类型"
              onValueChange={(value) => updateDraftField('attachmentType', value as FileType)}
              value={draft.attachmentType}
            >
              <option value={FileType.None}>无附件</option>
              <option value={FileType.Local}>本地上传</option>
              <option value={FileType.Remote}>外部链接</option>
            </SelectField>
            {draft.attachmentType === FileType.Local ? (
              <FileField
                hint={attachmentFile?.name || detail?.attachmentFileName || undefined}
                label="附件文件"
                onChange={setAttachmentFile}
              />
            ) : null}
            {draft.attachmentType === FileType.Remote ? (
              <TextField
                label="附件外链"
                onValueChange={(value) => updateDraftField('attachmentRemoteUrl', value)}
                placeholder="https://..."
                type="url"
                value={draft.attachmentRemoteUrl ?? ''}
              />
            ) : null}
          </div>
        </EditorSection>

        <EditorSection description="只有当前课程已就绪的模板会出现在选择列表中。" title="运行环境">
          <div className={styles.stack}>
            <div className={styles.fieldGrid}>
              <SelectField
                label="题目类型"
                onValueChange={(value) => updateDraftField('type', value as ChallengeType)}
                value={draft.type}
              >
                {Object.values(ChallengeType).map((type) => (
                  <option key={type} value={type}>
                    {type}
                  </option>
                ))}
              </SelectField>
              <SelectField
                label="运行环境"
                onValueChange={(value) => setEnvironment(value as EnvironmentType)}
                value={draft.environment}
              >
                <option value={EnvironmentType.None}>无运行环境</option>
                <option value={EnvironmentType.Docker}>Docker</option>
                <option value={EnvironmentType.WindowsVM}>Windows VM</option>
              </SelectField>
              {draft.environment !== EnvironmentType.None ? (
                <SelectField
                  label="课程环境模板"
                  onValueChange={(value) => {
                    const template = availableTemplates.find((item) => item.id === Number(value))
                    setDraft((current) => ({
                      ...current,
                      imageTemplateId: template?.id ?? null,
                      containerImage: template?.registryUrl ?? current.containerImage,
                    }))
                  }}
                  value={draft.imageTemplateId ?? ''}
                >
                  <option value="">请选择已就绪模板</option>
                  {availableTemplates.map((template) => (
                    <option key={template.id} value={template.id}>
                      #{template.id} {template.name}
                    </option>
                  ))}
                </SelectField>
              ) : null}
              {draft.environment === EnvironmentType.Docker ? (
                <TextField
                  hint="选择课程模板后会自动填充，必要时可覆盖。"
                  label="容器镜像地址"
                  onValueChange={(value) => updateDraftField('containerImage', value)}
                  value={draft.containerImage ?? ''}
                />
              ) : null}
            </div>
            {draft.environment === EnvironmentType.Docker ? (
              <div className={styles.resourceGrid}>
                <TextField
                  label="内存 MB"
                  min={32}
                  onValueChange={(value) => updateDraftField('memoryLimit', Number(value))}
                  type="number"
                  value={draft.memoryLimit ?? 128}
                />
                <TextField
                  label="CPU"
                  min={1}
                  onValueChange={(value) => updateDraftField('cpuCount', Number(value))}
                  type="number"
                  value={draft.cpuCount ?? 1}
                />
                <TextField
                  label="存储 MB"
                  min={64}
                  onValueChange={(value) => updateDraftField('storageLimit', Number(value))}
                  type="number"
                  value={draft.storageLimit ?? 256}
                />
                <TextField
                  label="暴露端口"
                  max={65535}
                  min={1}
                  onValueChange={(value) => updateDraftField('exposePort', Number(value))}
                  type="number"
                  value={draft.exposePort ?? 80}
                />
              </div>
            ) : null}
          </div>
        </EditorSection>

        <EditorSection description="动态 Flag 必须保留团队哈希占位符；所有题型均不支持多 Flag。" title="判题规则">
          <div className={styles.fieldGrid}>
            <SelectField
              label="网络模式"
              onValueChange={(value) => updateDraftField('networkMode', value as NetworkMode)}
              value={draft.networkMode ?? NetworkMode.Open}
            >
              <option value={NetworkMode.Open}>开放网络</option>
              <option value={NetworkMode.Isolated}>隔离网络</option>
              <option value={NetworkMode.Custom}>自定义网络</option>
            </SelectField>
            <TextField
              hint="0 表示不限制。"
              label="提交次数限制"
              min={0}
              onValueChange={(value) => updateDraftField('submissionLimit', Number(value))}
              type="number"
              value={draft.submissionLimit ?? 0}
            />
            {dynamicFlag ? (
              <TextField
                label="动态 Flag 模板"
                maxLength={120}
                onValueChange={(value) => updateDraftField('flagTemplate', value)}
                placeholder="flag{[TEAM_HASH]}"
                value={draft.flagTemplate ?? ''}
              />
            ) : (
              <TextField
                label="静态 Flag"
                maxLength={127}
                onValueChange={(value) => updateDraftField('staticFlag', value)}
                value={draft.staticFlag ?? ''}
              />
            )}
          </div>
        </EditorSection>

        <EditorActionBar status={feedback?.message || '保存后返回课程题目管理。'}>
          <ActionButton disabled={saving} icon={<Save size={17} />} tone="primary" type="submit">
            {saving ? '正在保存' : editing ? '保存题目' : '创建题目'}
          </ActionButton>
        </EditorActionBar>
      </form>
    </TrainingEditorShell>
  )
}
