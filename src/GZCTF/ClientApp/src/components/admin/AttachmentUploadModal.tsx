import {
  ActionIcon,
  Button,
  Center,
  FileButton,
  Group,
  Modal,
  ModalProps,
  Overlay,
  Progress,
  ScrollArea,
  Stack,
  Text,
  Title,
  alpha,
  useMantineTheme,
} from '@mantine/core'
import { showNotification } from '@mantine/notifications'
import { mdiCheck, mdiClose } from '@mdi/js'
import { Icon } from '@mdi/react'
import { FC, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useParams } from 'react-router'
import { YinyuModalBody, YinyuPanel } from '@Components/yinyu/YinyuUI'
import { showErrorMsg } from '@Utils/Shared'
import { useEditChallenge } from '@Hooks/useEdit'
import api, { FileType } from '@Api'
import uploadClasses from '@Styles/Upload.module.css'

interface AttachmentUploadModalProps extends ModalProps {
  onUploaded?: () => void
}

export const AttachmentUploadModal: FC<AttachmentUploadModalProps> = (props) => {
  const { onClose, onUploaded, ...modalProps } = props
  const { id, chalId, challengeId } = useParams()
  const [numId, numCId] = [parseInt(id ?? '-1'), parseInt(chalId ?? challengeId ?? '-1')]
  const uploadFileName = `DYN_ATTACHMENT_${numCId}`
  const [disabled, setDisabled] = useState(false)

  const { mutate } = useEditChallenge(numId, numCId)

  const [progress, setProgress] = useState(0)
  const [files, setFiles] = useState<File[]>([])

  const theme = useMantineTheme()

  const { t } = useTranslation()

  const onUpload = async () => {
    if (files.length <= 0) {
      showNotification({
        color: 'red',
        message: t('admin.notification.games.challenges.attachment.no_selected'),
        icon: <Icon path={mdiClose} size={1} />,
      })
      return
    }

    setProgress(0)
    setDisabled(true)

    try {
      const data = await api.assets.assetsUpload(
        {
          files,
        },
        { filename: uploadFileName },
        {
          onUploadProgress: (e) => {
            setProgress((e.loaded / (e.total ?? 1)) * 90)
          },
        }
      )

      setProgress(95)
      if (data.data) {
        await api.edit.editAddFlags(
          numId,
          numCId,
          data.data.map((f, idx) => ({
            flag: files[idx].name,
            attachmentType: FileType.Local,
            fileHash: f.hash,
          }))
        )

        setProgress(0)
        showNotification({
          color: 'teal',
          message: t('admin.notification.games.challenges.attachment.updated'),
          icon: <Icon path={mdiCheck} size={1} />,
        })
        setFiles([])
        mutate()
        onUploaded?.()
        onClose()
      }
    } catch (err) {
      showErrorMsg(err, t)
    } finally {
      setDisabled(false)
    }
  }

  return (
    <Modal {...modalProps} onClose={onClose}>
      <YinyuModalBody>
        <Stack>
          <Text size="sm">
            {t('admin.content.games.challenges.attachment.instruction.dynamic.content')}
            <br />
            <Text fw="bold" span>
              {t('admin.content.games.challenges.attachment.instruction.dynamic.format')}
            </Text>
            <br />
            <Text fw="bold" c="orange" span>
              {t('admin.content.games.challenges.attachment.instruction.amount_double')}
            </Text>
            <br />
          </Text>
          <ScrollArea offsetScrollbars h="40vh" pos="relative">
            {files.length === 0 ? (
              <>
                <Overlay opacity={0.3} color="black" />
                <Center h="calc(40vh - 20px)">
                  <Stack gap={0}>
                    <Title order={2}>{t('admin.placeholder.games.challenges.attachment.no_file_selected.title')}</Title>
                    <Text>{t('admin.placeholder.games.challenges.attachment.no_file_selected.comment')}</Text>
                  </Stack>
                </Center>
              </>
            ) : (
              <Stack gap="xs">
                {files.map((file) => (
                  <YinyuPanel key={file.name} p={4} cells={12}>
                    <Group justify="space-between">
                      <Text lineClamp={1} ff="monospace">
                        {file.name}
                      </Text>
                      <ActionIcon onClick={() => setFiles(files.filter((f) => f !== file))}>
                        <Icon path={mdiClose} size={1} />
                      </ActionIcon>
                    </Group>
                  </YinyuPanel>
                ))}
              </Stack>
            )}
          </ScrollArea>
          <Group grow>
            <FileButton multiple onChange={setFiles}>
              {(props) => (
                <Button {...props} disabled={disabled}>
                  {t('common.button.select_file')}
                </Button>
              )}
            </FileButton>
            <Button
              className={uploadClasses.button}
              disabled={disabled || files.length < 1}
              onClick={onUpload}
              color={progress !== 0 ? 'cyan' : theme.primaryColor}
            >
              <div className={uploadClasses.label}>
                {progress !== 0 ? t('common.button.uploading') : t('admin.button.challenges.flag.add.dynamic')}
              </div>
              {progress !== 0 && (
                <Progress
                  value={progress}
                  className={uploadClasses.progress}
                  color={alpha(theme.colors[theme.primaryColor][2], 0.35)}
                  radius="sm"
                />
              )}
            </Button>
          </Group>
        </Stack>
      </YinyuModalBody>
    </Modal>
  )
}
