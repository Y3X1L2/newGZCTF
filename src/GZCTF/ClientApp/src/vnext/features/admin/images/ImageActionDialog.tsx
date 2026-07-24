import { Box, FileArchive, FolderInput, Upload } from 'lucide-react'
import { FormEvent, useEffect, useRef, useState } from 'react'
import { OSType } from '@Api'
import { FileField, SelectField, TextField } from '../../../shared/FormControls'
import { ActionButton, InlineFeedback, VNextDialog } from '../../../shared/Interaction'
import { errorMessage } from '../../../shared/errors'
import { imageTemplateAdminApi } from '../api'
import styles from './ImageActionDialog.module.css'

export type ImageActionMode = 'docker-register' | 'docker-upload' | 'local-import' | 'vm-upload'

const actionMeta: Record<ImageActionMode, { eyebrow: string; title: string; description: string }> = {
  'docker-register': {
    eyebrow: 'DOCKER REFERENCE',
    title: '注册 Docker 镜像',
    description: '从可访问的 Registry 拉取镜像，并登记为平台环境模板。',
  },
  'docker-upload': {
    eyebrow: 'DOCKER ARCHIVE',
    title: '上传 Docker Archive',
    description: '上传 docker save 生成的归档包，服务端将推送到内部 Registry。',
  },
  'vm-upload': {
    eyebrow: 'VM ARTIFACT',
    title: '上传 VM 镜像',
    description: '上传原始磁盘镜像，或包含受支持镜像的压缩包。',
  },
  'local-import': {
    eyebrow: 'SERVER IMPORT',
    title: '导入服务器文件',
    description: '登记服务端允许目录中已经存在的 VM 镜像文件。',
  },
}

export function ImageActionDialog({
  mode,
  open,
  onClose,
  onCompleted,
}: {
  mode: ImageActionMode
  open: boolean
  onClose: () => void
  onCompleted: () => void | Promise<void>
}) {
  const [name, setName] = useState('')
  const [registryUrl, setRegistryUrl] = useState('')
  const [sourceImage, setSourceImage] = useState('')
  const [localPath, setLocalPath] = useState('')
  const [osType, setOsType] = useState(OSType.Linux)
  const [vmMode, setVmMode] = useState<'archive' | 'disk'>('archive')
  const [file, setFile] = useState<File | null>(null)
  const [submitting, setSubmitting] = useState(false)
  const [progress, setProgress] = useState<number | null>(null)
  const [failure, setFailure] = useState<string | null>(null)
  const uploadController = useRef<AbortController | null>(null)
  const meta = actionMeta[mode]

  useEffect(() => {
    if (open) return
    setName('')
    setRegistryUrl('')
    setSourceImage('')
    setLocalPath('')
    setOsType(OSType.Linux)
    setVmMode('archive')
    setFile(null)
    setSubmitting(false)
    setProgress(null)
    setFailure(null)
    uploadController.current = null
  }, [open])

  const close = () => {
    if (submitting && uploadController.current) uploadController.current.abort()
    onClose()
  }

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    if (submitting) return
    setFailure(null)
    setSubmitting(true)
    setProgress(null)

    try {
      if (mode === 'docker-register') {
        await imageTemplateAdminApi.registerDocker({ name: name.trim(), registryUrl: registryUrl.trim(), osType })
      } else if (mode === 'local-import') {
        await imageTemplateAdminApi.importLocal({ localPath: localPath.trim(), displayName: name.trim() || null })
      } else {
        if (!file) throw new Error('请选择需要上传的文件。')
        const controller = new AbortController()
        uploadController.current = controller
        const options = { signal: controller.signal, onProgress: setProgress }
        if (mode === 'docker-upload') {
          await imageTemplateAdminApi.uploadDockerArchive(
            { file, name: name.trim(), sourceImage: sourceImage.trim() || undefined, osType },
            options
          )
        } else if (vmMode === 'archive') {
          await imageTemplateAdminApi.uploadVmArchive(file, options)
        } else {
          await imageTemplateAdminApi.uploadVm(file, options)
        }
      }
      await onCompleted()
      onClose()
    } catch (error) {
      if (error instanceof DOMException && error.name === 'AbortError') setFailure('上传已取消。')
      else setFailure(errorMessage(error, '环境模板操作失败。'))
    } finally {
      uploadController.current = null
      setSubmitting(false)
    }
  }

  const uploadMode = mode === 'docker-upload' || mode === 'vm-upload'
  const valid =
    mode === 'docker-register'
      ? Boolean(name.trim() && registryUrl.trim())
      : mode === 'local-import'
        ? Boolean(localPath.trim())
        : mode === 'docker-upload'
          ? Boolean(file && name.trim())
          : Boolean(file)

  return (
    <VNextDialog
      description={meta.description}
      eyebrow={meta.eyebrow}
      footer={
        <>
          <ActionButton disabled={submitting && !uploadMode} onClick={close} type="button">
            {submitting && uploadMode ? '取消上传' : '取消'}
          </ActionButton>
          <ActionButton
            disabled={!valid || submitting}
            onClick={() => undefined}
            tone="primary"
            type="submit"
            form="image-action-form"
          >
            {submitting ? '正在处理' : '确认'}
          </ActionButton>
        </>
      }
      onClose={close}
      open={open}
      title={meta.title}
      wide
    >
      <form className={styles.form} id="image-action-form" onSubmit={(event) => void submit(event)}>
        {mode === 'docker-register' ? (
          <>
            <TextField label="模板名称" onValueChange={setName} required value={name} />
            <TextField
              label="Registry 镜像引用"
              onValueChange={setRegistryUrl}
              placeholder="registry.example.com/team/challenge:tag"
              required
              value={registryUrl}
            />
            <SelectField label="操作系统" onValueChange={(value) => setOsType(Number(value) as OSType)} value={osType}>
              <option value={OSType.Linux}>Linux</option>
              <option value={OSType.Windows}>Windows</option>
            </SelectField>
          </>
        ) : null}

        {mode === 'docker-upload' ? (
          <>
            <TextField label="模板名称" onValueChange={setName} required value={name} />
            <TextField
              hint="归档包含多个镜像时用于选择目标镜像。"
              label="源镜像名称"
              onValueChange={setSourceImage}
              placeholder="challenge:latest"
              value={sourceImage}
            />
            <SelectField label="操作系统" onValueChange={(value) => setOsType(Number(value) as OSType)} value={osType}>
              <option value={OSType.Linux}>Linux</option>
              <option value={OSType.Windows}>Windows</option>
            </SelectField>
            <FileField accept=".tar,.tar.gz,.tgz" label="Docker Archive" onChange={setFile} />
          </>
        ) : null}

        {mode === 'vm-upload' ? (
          <>
            <SelectField
              label="上传类型"
              onValueChange={(value) => setVmMode(value as 'archive' | 'disk')}
              value={vmMode}
            >
              <option value="archive">VM 压缩包</option>
              <option value="disk">原始磁盘镜像</option>
            </SelectField>
            <FileField
              accept={vmMode === 'archive' ? '.zip,.tar.gz,.tgz,.tar.xz,.txz' : '.qcow2,.ova,.vmdk'}
              label={vmMode === 'archive' ? 'VM 压缩包' : 'VM 镜像文件'}
              onChange={setFile}
            />
          </>
        ) : null}

        {mode === 'local-import' ? (
          <>
            <TextField
              label="服务器文件路径"
              onValueChange={setLocalPath}
              placeholder="/var/lib/gzctf/images/template.qcow2"
              required
              value={localPath}
            />
            <TextField label="显示名称" onValueChange={setName} value={name} />
          </>
        ) : null}

        {file ? (
          <div className={styles.fileSummary}>
            {mode === 'docker-upload' ? <Box size={17} /> : <FileArchive size={17} />}
            <span>{file.name}</span>
            <small>{(file.size / 1024 / 1024).toFixed(1)} MB</small>
          </div>
        ) : mode === 'local-import' ? (
          <div className={styles.modeMark}>
            <FolderInput size={17} />
            服务端本地导入
          </div>
        ) : mode === 'docker-register' ? (
          <div className={styles.modeMark}>
            <Box size={17} />
            Registry 拉取
          </div>
        ) : (
          <div className={styles.modeMark}>
            <Upload size={17} />
            等待选择文件
          </div>
        )}

        {submitting && uploadMode ? (
          <div className={styles.progress}>
            <span>
              上传进度
              <strong>{progress === null ? '计算中' : `${Math.round(progress * 100)}%`}</strong>
            </span>
            <progress max={1} value={progress ?? undefined} />
          </div>
        ) : null}

        {failure ? <InlineFeedback tone="danger">{failure}</InlineFeedback> : null}
      </form>
    </VNextDialog>
  )
}
