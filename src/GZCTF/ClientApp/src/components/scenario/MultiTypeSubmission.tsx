import { Tabs, TextInput, FileInput, Textarea, Button, Group, Text, Badge } from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { useState } from 'react'

interface MultiTypeSubmissionProps {
  challengeId: number
  instanceId: string
}

export default function MultiTypeSubmission({ challengeId, instanceId }: MultiTypeSubmissionProps) {
  const [activeTab, setActiveTab] = useState<string | null>('Flag')
  const [flag, setFlag] = useState('')
  const [ip, setIp] = useState('')
  const [writeupFile, setWriteupFile] = useState<File | null>(null)
  const [writeupText, setWriteupText] = useState('')
  const [submitting, setSubmitting] = useState(false)

  const submitFlag = async () => {
    if (!flag.trim()) return
    setSubmitting(true)
    try {
      await fetch('/api/v1/submissions', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ challengeId, submissionType: 'Flag', content: { value: flag } }),
      })
      notifications.show({ title: '提交成功', message: 'Flag 已提交', color: 'green' })
      setFlag('')
    } catch {
      notifications.show({ title: '提交失败', message: '请重试', color: 'red' })
    } finally {
      setSubmitting(false)
    }
  }

  const submitIp = async () => {
    if (!ip.trim()) return
    setSubmitting(true)
    try {
      await fetch('/api/v1/submissions', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ challengeId, submissionType: 'IP', content: { value: ip } }),
      })
      notifications.show({ title: '提交成功', message: 'IP 已提交', color: 'green' })
      setIp('')
    } catch {
      notifications.show({ title: '提交失败', message: '请重试', color: 'red' })
    } finally {
      setSubmitting(false)
    }
  }

  const submitWriteup = async () => {
    if (!writeupFile && !writeupText.trim()) return
    setSubmitting(true)
    try {
      if (writeupFile) {
        const formData = new FormData()
        formData.append('file', writeupFile)
        formData.append('challengeId', challengeId.toString())
        formData.append('submissionType', 'Writeup')
        await fetch('/api/v1/submissions/upload', { method: 'POST', body: formData })
      } else {
        await fetch('/api/v1/submissions', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            challengeId,
            submissionType: 'Writeup',
            content: { text: writeupText, format: 'markdown' },
          }),
        })
      }
      notifications.show({ title: '提交成功', message: '解题报告已提交，等待评审', color: 'green' })
      setWriteupFile(null)
      setWriteupText('')
    } catch {
      notifications.show({ title: '提交失败', message: '请重试', color: 'red' })
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <Tabs value={activeTab} onChange={setActiveTab}>
      <Tabs.List>
        <Tabs.Tab value="Flag" data-testid="submission-tab-Flag">
          <Group gap="xs">
            <Text>Flag</Text>
            <Badge size="xs">自动</Badge>
          </Group>
        </Tabs.Tab>
        <Tabs.Tab value="Writeup" data-testid="submission-tab-Writeup">
          <Group gap="xs">
            <Text>解题报告</Text>
            <Badge size="xs" color="orange">
              人工评审
            </Badge>
          </Group>
        </Tabs.Tab>
        <Tabs.Tab value="IP" data-testid="submission-tab-IP">
          <Group gap="xs">
            <Text>攻击者 IP</Text>
            <Badge size="xs">自动</Badge>
          </Group>
        </Tabs.Tab>
      </Tabs.List>

      <Tabs.Panel value="Flag" pt="md">
        <Group>
          <TextInput
            data-testid="flag-input"
            placeholder="flag{...}"
            value={flag}
            onChange={(e) => setFlag(e.currentTarget.value)}
            style={{ flex: 1 }}
          />
          <Button data-testid="submit-flag" loading={submitting} onClick={submitFlag}>
            提交
          </Button>
        </Group>
      </Tabs.Panel>

      <Tabs.Panel value="Writeup" pt="md">
        <FileInput
          data-testid="writeup-file-input"
          label="上传文件 (PDF/Markdown)"
          mb="sm"
          accept=".pdf,.md"
          value={writeupFile}
          onChange={setWriteupFile}
        />
        <Textarea
          label="或直接编写 (Markdown)"
          minRows={6}
          value={writeupText}
          onChange={(e) => setWriteupText(e.currentTarget.value)}
          placeholder="## 解题报告&#10;&#10;### 外网入口&#10;..."
        />
        <Button mt="md" data-testid="submit-writeup" loading={submitting} onClick={submitWriteup}>
          提交报告
        </Button>
      </Tabs.Panel>

      <Tabs.Panel value="IP" pt="md">
        <Group>
          <TextInput
            data-testid="ip-input"
            placeholder="192.168.1.100"
            value={ip}
            onChange={(e) => setIp(e.currentTarget.value)}
            style={{ flex: 1 }}
          />
          <Button data-testid="submit-ip" loading={submitting} onClick={submitIp}>
            提交
          </Button>
        </Group>
      </Tabs.Panel>
    </Tabs>
  )
}
