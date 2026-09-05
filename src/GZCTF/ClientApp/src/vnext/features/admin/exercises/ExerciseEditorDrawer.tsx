import { Plus, Save, Trash2 } from 'lucide-react'
import { useEffect, useState } from 'react'
import {
  AnswerType,
  ChallengeCategory,
  ChallengeType,
  Difficulty,
  EnvironmentType,
  FileType,
  FlagScoreMode,
  NetworkMode,
} from '@Api'
import { FileField, SelectField, TextAreaField, TextField, ToggleField } from '../../../shared/FormControls'
import { ActionButton, InlineFeedback, VNextDrawer } from '../../../shared/Interaction'
import { errorMessage } from '../../../shared/errors'
import {
  ExerciseAdminDraft,
  ExerciseAdminFlag,
  exerciseAdminApi,
  normalizeExerciseRuntime,
  uploadExerciseAsset,
} from './exerciseAdminApi'
import styles from './ExerciseEditorDrawer.module.css'

function emptyFlag(orderIndex = 0): ExerciseAdminFlag {
  return {
    flag: '',
    orderIndex,
    scoreMode: FlagScoreMode.InheritDecay,
    fixedScore: 0,
    maxAttempts: 0,
    answerType: AnswerType.Flag,
    attachmentType: FileType.None,
  }
}

function emptyDraft(): ExerciseAdminDraft {
  return {
    title: '',
    content: '',
    category: ChallengeCategory.Web,
    type: ChallengeType.StaticAttachment,
    difficulty: Difficulty.Baby,
    credit: false,
    isEnabled: true,
    tags: [],
    hints: [],
    containerImage: null,
    memoryLimit: 128,
    storageLimit: 256,
    cpuCount: 1,
    exposePort: 80,
    networkMode: NetworkMode.Open,
    environment: EnvironmentType.None,
    imageTemplateId: null,
    flagTemplate: null,
    submissionLimit: 0,
    flags: [emptyFlag()],
    attachment: { attachmentType: FileType.None },
  }
}

export function ExerciseEditorDrawer({
  exerciseId,
  open,
  onClose,
  onSaved,
}: {
  exerciseId: number | null
  open: boolean
  onClose: () => void
  onSaved: () => void
}) {
  const [draft, setDraft] = useState<ExerciseAdminDraft>(emptyDraft)
  const [attachmentFile, setAttachmentFile] = useState<File | null>(null)
  const [flagFiles, setFlagFiles] = useState<Record<number, File | null>>({})
  const [loading, setLoading] = useState(false)
  const [saving, setSaving] = useState(false)
  const [failure, setFailure] = useState<string | null>(null)

  useEffect(() => {
    if (!open) return
    setDraft(emptyDraft())
    setAttachmentFile(null)
    setFlagFiles({})
    setFailure(null)
    if (!exerciseId) return
    setLoading(true)
    void exerciseAdminApi.detail(exerciseId)
      .then((detail) => setDraft({ ...emptyDraft(), ...detail }))
      .catch((requestError) => setFailure(errorMessage(requestError, '练习题配置加载失败。')))
      .finally(() => setLoading(false))
  }, [exerciseId, open])

  const update = <Key extends keyof ExerciseAdminDraft>(field: Key, value: ExerciseAdminDraft[Key]) =>
    setDraft((current) => ({ ...current, [field]: value }))

  const updateFlag = (index: number, patch: Partial<ExerciseAdminFlag>) =>
    setDraft((current) => ({
      ...current,
      flags: current.flags.map((flag, flagIndex) => flagIndex === index ? { ...flag, ...patch } : flag),
    }))

  const save = async () => {
    if (!draft.title.trim()) {
      setFailure('请输入题目名称。')
      return
    }
    if (draft.type !== ChallengeType.DynamicContainer && !draft.flags.some((flag) => flag.flag.trim())) {
      setFailure('至少需要配置一个 Flag。')
      return
    }
    setSaving(true)
    setFailure(null)
    try {
      const attachmentHash = attachmentFile ? await uploadExerciseAsset(attachmentFile) : draft.attachment?.fileHash
      const flags = await Promise.all(draft.flags.map(async (flag, index) => ({
        ...flag,
        flag: flag.flag.trim(),
        orderIndex: index,
        fileHash: flagFiles[index] ? await uploadExerciseAsset(flagFiles[index] as File) : flag.fileHash,
        remoteUrl: flag.attachmentType === FileType.Remote ? flag.remoteUrl?.trim() || null : null,
      })))
      const payload = normalizeExerciseRuntime({
        ...draft,
        title: draft.title.trim(),
        tags: draft.tags.map((tag) => tag.trim()).filter(Boolean),
        containerImage: draft.containerImage?.trim() || null,
        flagTemplate: draft.flagTemplate?.trim() || null,
        flags: draft.type === ChallengeType.DynamicContainer ? [] : flags,
        attachment: {
          attachmentType: draft.attachment?.attachmentType ?? FileType.None,
          fileHash: draft.attachment?.attachmentType === FileType.Local ? attachmentHash : null,
          remoteUrl: draft.attachment?.attachmentType === FileType.Remote
            ? draft.attachment.remoteUrl?.trim() || null
            : null,
        },
      })
      if (exerciseId) await exerciseAdminApi.update(exerciseId, payload)
      else await exerciseAdminApi.create(payload)
      onSaved()
      onClose()
    } catch (requestError) {
      setFailure(errorMessage(requestError, '练习题保存失败。'))
    } finally {
      setSaving(false)
    }
  }

  const container = draft.type === ChallengeType.StaticContainer || draft.type === ChallengeType.DynamicContainer

  return (
    <VNextDrawer
      description="只维护公共练习题库，不会读取或修改培训课程题目。"
      eyebrow="EXERCISE MANAGEMENT"
      footer={
        <>
          <ActionButton disabled={saving} onClick={onClose} type="button">取消</ActionButton>
          <ActionButton disabled={saving || loading} icon={<Save size={16} />} onClick={() => void save()} tone="primary" type="button">
            {saving ? '正在保存' : '保存题目'}
          </ActionButton>
        </>
      }
      onClose={onClose}
      open={open}
      size="wide"
      title={exerciseId ? '编辑练习题' : '创建练习题'}
    >
      {failure ? <InlineFeedback tone="danger">{failure}</InlineFeedback> : null}
      {loading ? <p className={styles.loading}>正在读取题目配置...</p> : (
        <div className={styles.form}>
          <section>
            <h3>基本信息</h3>
            <div className={styles.fieldGrid}>
              <TextField label="题目名称" maxLength={256} onValueChange={(value) => update('title', value)} required value={draft.title} />
              <SelectField label="分类" onValueChange={(value) => update('category', value as ChallengeCategory)} value={draft.category}>
                {Object.values(ChallengeCategory).map((value) => <option key={value} value={value}>{value}</option>)}
              </SelectField>
              <SelectField label="难度" onValueChange={(value) => update('difficulty', value as Difficulty)} value={draft.difficulty}>
                {Object.values(Difficulty).map((value) => <option key={value} value={value}>{value}</option>)}
              </SelectField>
              <SelectField label="题目类型" onValueChange={(value) => update('type', value as ChallengeType)} value={draft.type}>
                {Object.values(ChallengeType).map((value) => <option key={value} value={value}>{value}</option>)}
              </SelectField>
              <TextField label="标签" onValueChange={(value) => update('tags', value.split(','))} placeholder="SQL 注入, RCE" value={draft.tags.join(', ')} />
              <TextField label="提交上限" min={0} onValueChange={(value) => update('submissionLimit', Math.max(0, Number(value)))} type="number" value={draft.submissionLimit} />
            </div>
            <ToggleField checked={draft.isEnabled} description="关闭后学员端不可见，也不能创建新实例。" label="启用题目" onChange={(value) => update('isEnabled', value)} />
          </section>

          <section>
            <h3>题目内容</h3>
            <TextAreaField label="Markdown" onValueChange={(value) => update('content', value)} rows={14} value={draft.content} />
          </section>

          <section>
            <h3>题目附件</h3>
            <div className={styles.fieldGrid}>
              <SelectField label="附件类型" onValueChange={(value) => update('attachment', { ...draft.attachment, attachmentType: value as FileType })} value={draft.attachment?.attachmentType ?? FileType.None}>
                {Object.values(FileType).map((value) => <option key={value} value={value}>{value}</option>)}
              </SelectField>
              {draft.attachment?.attachmentType === FileType.Local ? <FileField hint={draft.attachment.fileHash || undefined} label="上传文件" onChange={setAttachmentFile} /> : null}
              {draft.attachment?.attachmentType === FileType.Remote ? <TextField label="远程地址" onValueChange={(value) => update('attachment', { ...draft.attachment, attachmentType: FileType.Remote, remoteUrl: value })} type="url" value={draft.attachment.remoteUrl ?? ''} /> : null}
            </div>
          </section>

          {container ? (
            <section>
              <h3>容器配置</h3>
              <div className={styles.fieldGrid}>
                <TextField label="镜像" onValueChange={(value) => update('containerImage', value)} value={draft.containerImage ?? ''} />
                <TextField label="暴露端口" min={1} onValueChange={(value) => update('exposePort', Number(value))} type="number" value={draft.exposePort ?? 80} />
                <TextField label="内存 MB" min={16} onValueChange={(value) => update('memoryLimit', Number(value))} type="number" value={draft.memoryLimit ?? 128} />
                <TextField label="存储 MB" min={16} onValueChange={(value) => update('storageLimit', Number(value))} type="number" value={draft.storageLimit ?? 256} />
                <TextField label="CPU 配额" min={1} onValueChange={(value) => update('cpuCount', Number(value))} type="number" value={draft.cpuCount ?? 1} />
                <SelectField label="网络模式" onValueChange={(value) => update('networkMode', value as NetworkMode)} value={draft.networkMode}>
                  {Object.values(NetworkMode).map((value) => <option key={value} value={value}>{value}</option>)}
                </SelectField>
                {draft.type === ChallengeType.DynamicContainer ? <TextField label="动态 Flag 模板" onValueChange={(value) => update('flagTemplate', value)} value={draft.flagTemplate ?? ''} /> : null}
              </div>
            </section>
          ) : null}

          {draft.type !== ChallengeType.DynamicContainer ? (
            <section>
              <div className={styles.sectionTitle}>
                <h3>Flags</h3>
                <ActionButton icon={<Plus size={16} />} onClick={() => update('flags', [...draft.flags, emptyFlag(draft.flags.length)])} type="button">添加 Flag</ActionButton>
              </div>
              <div className={styles.flagList}>
                {draft.flags.map((flag, index) => (
                  <div className={styles.flagRow} key={flag.id ?? `new-${index}`}>
                    <div className={styles.flagRowHeader}>
                      <strong>Flag {index + 1}</strong>
                      <button aria-label={`删除 Flag ${index + 1}`} onClick={() => update('flags', draft.flags.filter((_, itemIndex) => itemIndex !== index))} title="删除 Flag" type="button"><Trash2 size={16} /></button>
                    </div>
                    <div className={styles.fieldGrid}>
                      <TextField label="Flag 内容" onValueChange={(value) => updateFlag(index, { flag: value })} required value={flag.flag} />
                      <TextField label="显示名称" onValueChange={(value) => updateFlag(index, { customName: value })} value={flag.customName ?? ''} />
                      <TextField label="说明" onValueChange={(value) => updateFlag(index, { description: value })} value={flag.description ?? ''} />
                      <TextField label="尝试上限" min={0} onValueChange={(value) => updateFlag(index, { maxAttempts: Math.max(0, Number(value)) })} type="number" value={flag.maxAttempts} />
                      <SelectField label="附件类型" onValueChange={(value) => updateFlag(index, { attachmentType: value as FileType })} value={flag.attachmentType}>
                        {Object.values(FileType).map((value) => <option key={value} value={value}>{value}</option>)}
                      </SelectField>
                      {flag.attachmentType === FileType.Local ? <FileField hint={flag.fileHash || undefined} label="Flag 附件" onChange={(file) => setFlagFiles((current) => ({ ...current, [index]: file }))} /> : null}
                      {flag.attachmentType === FileType.Remote ? <TextField label="附件地址" onValueChange={(value) => updateFlag(index, { remoteUrl: value })} type="url" value={flag.remoteUrl ?? ''} /> : null}
                    </div>
                  </div>
                ))}
              </div>
            </section>
          ) : null}
        </div>
      )}
    </VNextDrawer>
  )
}
