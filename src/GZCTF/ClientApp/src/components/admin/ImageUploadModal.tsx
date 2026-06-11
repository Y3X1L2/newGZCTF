import { Button, Modal, Stack, TextInput } from '@mantine/core'
import { useState } from 'react'
import { YinyuModalBody } from '@Components/yinyu/YinyuUI'

export function ImageUploadModal({
  opened,
  onClose,
  onUploaded,
}: {
  opened: boolean
  onClose: () => void
  onUploaded: () => void
}) {
  const [localPath, setLocalPath] = useState('')
  const [loading, setLoading] = useState(false)

  const handleImport = async () => {
    setLoading(true)
    try {
      await fetch('/api/v1/image-templates/import-local', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ localPath }),
      })
      onUploaded()
      onClose()
    } catch {
      // Keep the previous import behavior: the caller refreshes only on success.
    } finally {
      setLoading(false)
    }
  }

  return (
    <Modal opened={opened} onClose={onClose} title="导入镜像">
      <YinyuModalBody>
        <Stack>
          <TextInput
            label="本地路径"
            value={localPath}
            onChange={(event) => setLocalPath(event.currentTarget.value)}
            placeholder="/path/to/windows.qcow2"
          />
          <Button loading={loading} onClick={handleImport}>
            导入
          </Button>
        </Stack>
      </YinyuModalBody>
    </Modal>
  )
}
