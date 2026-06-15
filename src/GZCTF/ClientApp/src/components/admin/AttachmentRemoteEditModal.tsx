import { Button, Modal, ModalProps, Stack, Text, Textarea } from '@mantine/core'
import { showNotification } from '@mantine/notifications'
import { mdiCheck } from '@mdi/js'
import { Icon } from '@mdi/react'
import { FC, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useParams } from 'react-router'
import { YinyuModalBody } from '@Components/yinyu/YinyuUI'
import { showErrorMsg } from '@Utils/Shared'
import { useEditChallenge } from '@Hooks/useEdit'
import api, { FileType, FlagCreateModel } from '@Api'
import misc from '@Styles/Misc.module.css'

interface AttachmentRemoteEditModalProps extends ModalProps {
  onUploaded?: () => void
}

export const AttachmentRemoteEditModal: FC<AttachmentRemoteEditModalProps> = (props) => {
  const { onClose, onUploaded, ...modalProps } = props
  const { id, chalId, challengeId } = useParams()
  const [numId, numCId] = [parseInt(id ?? '-1'), parseInt(chalId ?? challengeId ?? '-1')]

  const [disabled, setDisabled] = useState(false)

  const { mutate } = useEditChallenge(numId, numCId)

  const [text, setText] = useState('')
  const [flags, setFlags] = useState<FlagCreateModel[]>([])

  const { t } = useTranslation()

  useEffect(() => {
    const list: FlagCreateModel[] = []
    text.split('\n').forEach((line) => {
      let part = line.split(' ')
      part = part.length === 1 ? line.split('\t') : part

      if (part.length !== 2) return

      list.push({
        flag: part[0],
        attachmentType: FileType.Remote,
        remoteUrl: part[1],
      })
    })
    setFlags(list)
  }, [text])

  const onUpload = async () => {
    if (flags.length <= 0) return

    try {
      await api.edit.editAddFlags(numId, numCId, flags)
      showNotification({
        color: 'teal',
        message: t('admin.notification.games.challenges.attachment.updated'),
        icon: <Icon path={mdiCheck} size={1} />,
      })
      setText('')
      mutate()
      onUploaded?.()
      onClose()
    } catch (e) {
      showErrorMsg(e, t)
    } finally {
      setDisabled(false)
    }
  }

  return (
    <Modal {...modalProps} onClose={onClose}>
      <YinyuModalBody>
        <Stack>
          <Text>
            {t('admin.content.games.challenges.attachment.instruction.remote.content')}
            <br />
            <Text fw="bold" span>
              {t('admin.content.games.challenges.attachment.instruction.remote.format')}
            </Text>
            <br />
            <Text fw="bold" c="orange" span>
              {t('admin.content.games.challenges.attachment.instruction.amount_double')}
            </Text>
            <br />
          </Text>
          <Textarea
            required
            autosize
            minRows={8}
            maxRows={12}
            value={text}
            classNames={{ input: misc.ffmono }}
            onChange={(e) => setText(e.target.value)}
            placeholder={'flag{hello_world} http://example.com/1.zip\nflag{he11o_world} http://example.com/2.zip'}
          />
          <Button fullWidth disabled={disabled} onClick={onUpload}>
            {t('admin.button.games.challenges.attachment.batch_add')}
          </Button>
        </Stack>
      </YinyuModalBody>
    </Modal>
  )
}
