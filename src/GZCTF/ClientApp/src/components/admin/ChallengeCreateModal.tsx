import { Alert, Button, ComboboxItem, Modal, ModalProps, NumberInput, Select, Stack, TextInput } from '@mantine/core'
import { useInputState } from '@mantine/hooks'
import { showNotification } from '@mantine/notifications'
import { mdiCheck } from '@mdi/js'
import { Icon } from '@mdi/react'
import { FC, useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useNavigate, useParams } from 'react-router'
import { YinyuModalBody } from '@Components/yinyu/YinyuUI'
import { showErrorMsg } from '@Utils/Shared'
import {
  ChallengeCategoryItem,
  ChallengeCategoryList,
  ChallengeTypeItem,
  useChallengeCategoryLabelMap,
  useChallengeTypeLabelMap,
} from '@Utils/Shared'
import api, { ChallengeInfoModel, ChallengeCategory, ChallengeType, EnvironmentType } from '@Api'

interface ImageTemplateOption {
  id?: number
  Id?: number
  name?: string
  Name?: string
  description?: string | null
  Description?: string | null
  registryUrl?: string | null
  RegistryUrl?: string | null
  osType?: string | number
  OSType?: string | number
  imageType?: string | number
  ImageType?: string | number
  status?: string | number
  Status?: string | number
}

function templateKey(value: string | number | undefined | null) {
  return String(value ?? '').toLowerCase()
}

function isReadyTemplate(template: ImageTemplateOption) {
  const status = templateKey(template.status ?? template.Status)
  return status === '0' || status === 'ready' || status === ''
}

function isDockerTemplate(template: ImageTemplateOption) {
  const imageType = templateKey(template.imageType ?? template.ImageType)
  return imageType === '0' || imageType === 'docker'
}

function templateId(template: ImageTemplateOption) {
  return template.id ?? template.Id
}

function templateName(template: ImageTemplateOption) {
  return template.name ?? template.Name ?? `模板 ${templateId(template) ?? ''}`
}

function templateDescription(template: ImageTemplateOption) {
  return template.description ?? template.Description
}

function templateRegistryUrl(template: ImageTemplateOption) {
  return template.registryUrl ?? template.RegistryUrl
}

interface ChallengeCreateModalProps extends ModalProps {
  onAddChallenge: (game: ChallengeInfoModel) => void
}

export const ChallengeCreateModal: FC<ChallengeCreateModalProps> = (props) => {
  const { id } = useParams()
  const { onAddChallenge, ...modalProps } = props
  const [disabled, setDisabled] = useState(false)
  const navigate = useNavigate()
  const challengeCategoryLabelMap = useChallengeCategoryLabelMap()
  const challengeTypeLabelMap = useChallengeTypeLabelMap()

  const [title, setTitle] = useInputState('')
  const [category, setCategory] = useState<string | null>(null)
  const [type, setType] = useState<string | null>(null)
  const [environment, setEnvironment] = useState<EnvironmentType>(EnvironmentType.None)
  const [containerImage, setContainerImage] = useState('')
  const [exposePort, setExposePort] = useState(80)
  const [imageTemplateId, setImageTemplateId] = useState<string | null>(null)
  const [imageTemplates, setImageTemplates] = useState<ImageTemplateOption[]>([])

  const { t } = useTranslation()

  const isContainerType =
    type === ChallengeType.StaticContainer || type === ChallengeType.DynamicContainer
  const isDockerEnv = isContainerType && environment === EnvironmentType.Docker
  const isWindowsVmEnv = isContainerType && environment === EnvironmentType.WindowsVM

  const dockerTemplates = useMemo(
    () =>
      imageTemplates.filter((template) => {
        return templateRegistryUrl(template) && isDockerTemplate(template) && isReadyTemplate(template)
      }),
    [imageTemplates]
  )

  const windowsTemplates = useMemo(
    () =>
      imageTemplates.filter((template) => {
        const osType = templateKey(template.osType ?? template.OSType)
        return isReadyTemplate(template) && !isDockerTemplate(template) && (osType === '1' || osType === 'windows')
      }),
    [imageTemplates]
  )

  useEffect(() => {
    if (!modalProps.opened) return

    fetch('/api/v1/image-templates?pageSize=100')
      .then((res) => (res.ok ? res.json() : null))
      .then((data) => setImageTemplates(data?.items ?? data ?? []))
      .catch(() => setImageTemplates([]))
  }, [modalProps.opened])

  useEffect(() => {
    if (!isContainerType) {
      setEnvironment(EnvironmentType.None)
      return
    }

    if (environment === EnvironmentType.None) {
      setEnvironment(EnvironmentType.Docker)
    }
  }, [environment, isContainerType])

  const onCreate = async () => {
    if (!title || !category || !type) return
    if (isDockerEnv && !containerImage.trim()) {
      showNotification({
        color: 'red',
        message: '容器题目必须先绑定 Docker 镜像',
      })
      return
    }

    if (isWindowsVmEnv && !imageTemplateId) {
      showNotification({
        color: 'red',
        message: 'Windows 虚拟机题目必须先选择 Windows 镜像模板',
      })
      return
    }

    setDisabled(true)
    const numId = parseInt(id ?? '-1')

    try {
      const res = await api.edit.editAddGameChallenge(numId, {
        title: title,
        category: category as ChallengeCategory,
        type: type as ChallengeType,
        environment: isContainerType ? environment : EnvironmentType.None,
        containerImage: isDockerEnv ? containerImage.trim() : undefined,
        exposePort: isDockerEnv ? exposePort : undefined,
        imageTemplateId: isWindowsVmEnv && imageTemplateId ? Number(imageTemplateId) : undefined,
      })
      showNotification({
        color: 'teal',
        message: t('admin.notification.games.challenges.created'),
        icon: <Icon path={mdiCheck} size={1} />,
      })
      onAddChallenge(res.data)
      navigate(`/admin/games/${id}/challenges/${res.data.id}`)
    } catch (e) {
      showErrorMsg(e, t)
    } finally {
      setDisabled(false)
    }
  }

  return (
    <Modal {...modalProps}>
      <YinyuModalBody>
        <Stack>
        <TextInput
          label={t('admin.content.games.challenges.title')}
          type="text"
          required
          placeholder="Title"
          value={title}
          onChange={setTitle}
        />
        <Select
          required
          label={t('admin.content.games.challenges.category')}
          placeholder="Category"
          value={category}
          onChange={setCategory}
          renderOption={ChallengeCategoryItem}
          data={ChallengeCategoryList.map((category) => {
            const data = challengeCategoryLabelMap.get(category)
            return { value: category, label: data?.name, ...data } as ComboboxItem
          })}
        />
        <Select
          required
          label={t('admin.content.games.challenges.type.label')}
          description={t('admin.content.games.challenges.type.description')}
          placeholder="Type"
          value={type}
          onChange={(value) => {
            setType(value)
            const nextIsContainer =
              value === ChallengeType.StaticContainer || value === ChallengeType.DynamicContainer
            setEnvironment(nextIsContainer ? EnvironmentType.Docker : EnvironmentType.None)
            if (!nextIsContainer) {
              setContainerImage('')
              setImageTemplateId(null)
            }
          }}
          renderOption={ChallengeTypeItem}
          data={Object.entries(ChallengeType).map((type) => {
            const data = challengeTypeLabelMap.get(type[1])
            return { value: type[1], label: data?.name, ...data } as ComboboxItem
          })}
        />
        {isContainerType && (
          <Select
            required
            label="环境类型"
            placeholder="选择运行环境"
            value={environment}
            onChange={(value) => {
              const next = (value as EnvironmentType | null) ?? EnvironmentType.Docker
              setEnvironment(next)
              if (next !== EnvironmentType.Docker) {
                setContainerImage('')
              }
              if (next !== EnvironmentType.WindowsVM) {
                setImageTemplateId(null)
              }
            }}
            data={[
              { value: EnvironmentType.Docker, label: 'Docker 容器' },
              { value: EnvironmentType.WindowsVM, label: 'Windows 虚拟机 (RDP)' },
            ]}
          />
        )}
        {isDockerEnv && (
          <>
            <Select
              label="Docker 镜像"
              placeholder={dockerTemplates.length ? '选择已注册镜像' : '暂无已注册 Docker 镜像'}
              data={dockerTemplates.map((template) => ({
                value: templateRegistryUrl(template) ?? '',
                label: `${templateName(template)} - ${templateRegistryUrl(template)}`,
              }))}
              value={dockerTemplates.some((template) => templateRegistryUrl(template) === containerImage) ? containerImage : null}
              onChange={(value) => setContainerImage(value ?? '')}
              searchable
              clearable
              required
            />
            <TextInput
              label="容器镜像地址"
              placeholder="10.24.0.28:5000/ctf/web/example:latest"
              value={containerImage}
              onChange={(event) => setContainerImage(event.currentTarget.value)}
              required
            />
            <NumberInput
              label="开放端口"
              min={1}
              max={65535}
              value={exposePort}
              onChange={(value) => setExposePort(Number(value) || 80)}
              required
            />
          </>
        )}
        {isWindowsVmEnv && (
          <>
            <Select
              label="Windows 镜像模板"
              placeholder={windowsTemplates.length ? '选择 Windows 镜像模板' : '暂无就绪 Windows 镜像模板'}
              data={windowsTemplates
                .flatMap((template) => {
                  const id = templateId(template)
                  const description = templateDescription(template)
                  return id
                    ? [{
                        value: String(id),
                        label: `${templateName(template)}${description ? ` - ${description}` : ''}`,
                      }]
                    : []
                })}
              value={imageTemplateId}
              onChange={(value) => setImageTemplateId(value ? String(value) : null)}
              searchable
              clearable
              required
            />
            <Alert color="blue" variant="light">
              Windows 虚拟机题目只需要选择镜像模板，不需要 Docker 镜像地址和开放端口。
            </Alert>
          </>
        )}
        <Button fullWidth disabled={disabled} onClick={onCreate}>
          {t('admin.button.challenges.new')}
        </Button>
        </Stack>
      </YinyuModalBody>
    </Modal>
  )
}
