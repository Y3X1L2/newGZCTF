import { Button, ComboboxItem, Modal, ModalProps, NumberInput, Select, Stack, TextInput } from '@mantine/core'
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
import api, { ChallengeInfoModel, ChallengeCategory, ChallengeType } from '@Api'

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
  const [containerImage, setContainerImage] = useState('')
  const [exposePort, setExposePort] = useState(80)
  const [imageTemplates, setImageTemplates] = useState<{ name: string; registryUrl?: string | null; imageType?: string | number; status?: string | number }[]>([])

  const { t } = useTranslation()

  const isContainer =
    type === ChallengeType.StaticContainer || type === ChallengeType.DynamicContainer

  const dockerTemplates = useMemo(
    () =>
      imageTemplates.filter((template) => {
        const imageType = String(template.imageType ?? '').toLowerCase()
        const status = String(template.status ?? '').toLowerCase()
        return (
          template.registryUrl &&
          (imageType === '0' || imageType === 'docker') &&
          (status === '0' || status === 'ready' || status === '')
        )
      }),
    [imageTemplates]
  )

  useEffect(() => {
    if (!modalProps.opened) return

    fetch('/api/v1/image-templates')
      .then((res) => (res.ok ? res.json() : null))
      .then((data) => setImageTemplates(data?.items ?? data ?? []))
      .catch(() => setImageTemplates([]))
  }, [modalProps.opened])

  const onCreate = async () => {
    if (!title || !category || !type) return
    if (isContainer && !containerImage.trim()) {
      showNotification({
        color: 'red',
        message: '容器题目必须先绑定 Docker 镜像',
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
        containerImage: isContainer ? containerImage.trim() : undefined,
        exposePort: isContainer ? exposePort : undefined,
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
          onChange={setType}
          renderOption={ChallengeTypeItem}
          data={Object.entries(ChallengeType).map((type) => {
            const data = challengeTypeLabelMap.get(type[1])
            return { value: type[1], label: data?.name, ...data } as ComboboxItem
          })}
        />
        {isContainer && (
          <>
            <Select
              label="Docker 镜像"
              placeholder={dockerTemplates.length ? '选择已注册镜像' : '暂无已注册 Docker 镜像'}
              data={dockerTemplates.map((template) => ({
                value: template.registryUrl ?? '',
                label: `${template.name} - ${template.registryUrl}`,
              }))}
              value={dockerTemplates.some((template) => template.registryUrl === containerImage) ? containerImage : null}
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
        <Button fullWidth disabled={disabled} onClick={onCreate}>
          {t('admin.button.challenges.new')}
        </Button>
        </Stack>
      </YinyuModalBody>
    </Modal>
  )
}
