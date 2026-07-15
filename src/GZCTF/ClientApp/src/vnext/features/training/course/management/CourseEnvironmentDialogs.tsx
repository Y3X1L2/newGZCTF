import { useState } from 'react'
import { FileField, SelectField, TextField } from '../../../../shared/FormControls'
import { ActionButton, VNextDialog } from '../../../../shared/Interaction'
import { errorMessage } from '../../../../shared/errors'
import { courseEnvironmentApi } from '../../api/courseEnvironmentApi'
import styles from './CourseEnvironmentPanel.module.css'

export type CourseEnvironmentDialog = 'register' | 'docker' | 'vm' | 'local' | null
export type CourseEnvironmentFeedback = { tone: 'success' | 'danger'; message: string }

interface CourseEnvironmentDialogsProps {
  courseId: number
  dialog: CourseEnvironmentDialog
  onChanged: () => Promise<unknown>
  onClose: () => void
  onFeedback: (feedback: CourseEnvironmentFeedback) => void
}

export function CourseEnvironmentDialogs({
  courseId,
  dialog,
  onChanged,
  onClose,
  onFeedback,
}: CourseEnvironmentDialogsProps) {
  const [saving, setSaving] = useState(false)
  const [registerName, setRegisterName] = useState('')
  const [registryUrl, setRegistryUrl] = useState('')
  const [registryAuth, setRegistryAuth] = useState('')
  const [dockerFile, setDockerFile] = useState<File | null>(null)
  const [dockerName, setDockerName] = useState('')
  const [dockerSourceImage, setDockerSourceImage] = useState('')
  const [vmFile, setVmFile] = useState<File | null>(null)
  const [vmArchive, setVmArchive] = useState('false')
  const [localPath, setLocalPath] = useState('')
  const [localName, setLocalName] = useState('')

  const close = () => {
    if (!saving) onClose()
  }

  const registerDocker = async () => {
    if (!registerName.trim() || !registryUrl.trim() || saving) return
    setSaving(true)
    try {
      await courseEnvironmentApi.registerDocker(courseId, {
        name: registerName.trim(),
        registryUrl: registryUrl.trim(),
        registryAuth: registryAuth.trim() || null,
      })
      await onChanged()
      onClose()
      setRegisterName('')
      setRegistryUrl('')
      setRegistryAuth('')
      onFeedback({ tone: 'success', message: 'Docker 镜像已注册，后台正在拉取和同步。' })
    } catch (requestError) {
      onFeedback({ tone: 'danger', message: errorMessage(requestError, 'Docker 镜像注册失败。') })
    } finally {
      setSaving(false)
    }
  }

  const uploadDocker = async () => {
    if (!dockerFile || !dockerName.trim() || saving) return
    setSaving(true)
    try {
      await courseEnvironmentApi.uploadDocker(courseId, {
        file: dockerFile,
        name: dockerName.trim(),
        sourceImage: dockerSourceImage.trim(),
      })
      await onChanged()
      onClose()
      setDockerFile(null)
      setDockerName('')
      setDockerSourceImage('')
      onFeedback({ tone: 'success', message: 'Docker 镜像包已上传，导入任务已经开始。' })
    } catch (requestError) {
      onFeedback({ tone: 'danger', message: errorMessage(requestError, 'Docker 镜像包上传失败。') })
    } finally {
      setSaving(false)
    }
  }

  const uploadVm = async () => {
    if (!vmFile || saving) return
    setSaving(true)
    try {
      await courseEnvironmentApi.uploadVm(courseId, vmFile, vmArchive === 'true')
      await onChanged()
      onClose()
      setVmFile(null)
      onFeedback({ tone: 'success', message: 'VM 镜像已上传，导入任务已经开始。' })
    } catch (requestError) {
      onFeedback({ tone: 'danger', message: errorMessage(requestError, 'VM 镜像上传失败。') })
    } finally {
      setSaving(false)
    }
  }

  const importLocal = async () => {
    if (!localPath.trim() || saving) return
    setSaving(true)
    try {
      await courseEnvironmentApi.importLocal(courseId, {
        localPath: localPath.trim(),
        displayName: localName.trim() || null,
      })
      await onChanged()
      onClose()
      setLocalPath('')
      setLocalName('')
      onFeedback({ tone: 'success', message: '服务器本地镜像已加入当前课程。' })
    } catch (requestError) {
      onFeedback({ tone: 'danger', message: errorMessage(requestError, '本地镜像导入失败。') })
    } finally {
      setSaving(false)
    }
  }

  return (
    <>
      <VNextDialog
        description="平台会拉取镜像并同步至配置的课程 Registry。"
        eyebrow="REGISTER DOCKER"
        footer={
          <>
            <ActionButton onClick={close} type="button">
              取消
            </ActionButton>
            <ActionButton
              disabled={saving || !registerName.trim() || !registryUrl.trim()}
              onClick={() => void registerDocker()}
              tone="primary"
              type="button"
            >
              {saving ? '正在注册' : '注册并拉取'}
            </ActionButton>
          </>
        }
        onClose={close}
        open={dialog === 'register'}
        title="注册 Docker 镜像"
        wide
      >
        <div className={styles.formGrid}>
          <TextField label="显示名称" onValueChange={setRegisterName} required value={registerName} />
          <TextField
            label="镜像地址"
            onValueChange={setRegistryUrl}
            placeholder="docker.io/library/alpine:latest"
            required
            value={registryUrl}
          />
          <TextField
            hint="私有仓库认证信息，可留空。"
            label="Registry 认证"
            onValueChange={setRegistryAuth}
            value={registryAuth}
          />
        </div>
      </VNextDialog>

      <VNextDialog
        description="上传由 docker save 导出的 tar、tar.gz 或 tgz 文件。"
        eyebrow="UPLOAD DOCKER"
        footer={
          <>
            <ActionButton onClick={close} type="button">
              取消
            </ActionButton>
            <ActionButton
              disabled={saving || !dockerFile || !dockerName.trim()}
              onClick={() => void uploadDocker()}
              tone="primary"
              type="button"
            >
              {saving ? '正在上传' : '上传并导入'}
            </ActionButton>
          </>
        }
        onClose={close}
        open={dialog === 'docker'}
        title="上传 Docker 镜像包"
        wide
      >
        <div className={styles.formGrid}>
          <TextField label="模板名称" onValueChange={setDockerName} required value={dockerName} />
          <TextField
            hint="可选，用于记录镜像包原始标签。"
            label="原始镜像名"
            onValueChange={setDockerSourceImage}
            placeholder="example/app:latest"
            value={dockerSourceImage}
          />
          <FileField
            accept=".tar,.tgz,.gz,application/x-tar,application/gzip"
            hint={dockerFile?.name}
            label="Docker 镜像包"
            onChange={setDockerFile}
          />
        </div>
      </VNextDialog>

      <VNextDialog
        description="普通模式上传单个虚拟磁盘；归档模式用于包含描述文件和磁盘的模板包。"
        eyebrow="UPLOAD VM"
        footer={
          <>
            <ActionButton onClick={close} type="button">
              取消
            </ActionButton>
            <ActionButton disabled={saving || !vmFile} onClick={() => void uploadVm()} tone="primary" type="button">
              {saving ? '正在上传' : '上传并导入'}
            </ActionButton>
          </>
        }
        onClose={close}
        open={dialog === 'vm'}
        title="上传虚拟机模板"
        wide
      >
        <div className={styles.formGrid}>
          <SelectField label="上传模式" onValueChange={setVmArchive} value={vmArchive}>
            <option value="false">单个虚拟磁盘</option>
            <option value="true">虚拟机归档包</option>
          </SelectField>
          <FileField hint={vmFile?.name} label="模板文件" onChange={setVmFile} />
        </div>
      </VNextDialog>

      <VNextDialog
        description="路径必须位于服务器允许导入的镜像目录中，不接受客户端本机路径。"
        eyebrow="IMPORT LOCAL"
        footer={
          <>
            <ActionButton onClick={close} type="button">
              取消
            </ActionButton>
            <ActionButton
              disabled={saving || !localPath.trim()}
              onClick={() => void importLocal()}
              tone="primary"
              type="button"
            >
              {saving ? '正在导入' : '导入课程'}
            </ActionButton>
          </>
        }
        onClose={close}
        open={dialog === 'local'}
        title="从服务器目录导入"
        wide
      >
        <div className={styles.formGrid}>
          <TextField
            label="服务器文件路径"
            onValueChange={setLocalPath}
            placeholder="/srv/images/template.qcow2"
            required
            value={localPath}
          />
          <TextField label="显示名称" onValueChange={setLocalName} value={localName} />
        </div>
      </VNextDialog>
    </>
  )
}
