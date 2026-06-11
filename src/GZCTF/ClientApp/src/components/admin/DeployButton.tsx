import { Button, Group, PasswordInput, TextInput } from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { mdiCheck, mdiClose, mdiPlus } from '@mdi/js'
import { Icon } from '@mdi/react'
import { useState } from 'react'
import { YinyuPanel } from '@Components/yinyu/YinyuUI'

export function DeployButton({ onDeployed }: { onDeployed?: () => void }) {
  const [loading, setLoading] = useState(false)
  const [showForm, setShowForm] = useState(false)
  const [host, setHost] = useState('')
  const [user, setUser] = useState('root')
  const [pass, setPass] = useState('')
  const [name, setName] = useState('')

  const handleDeploy = async () => {
    if (!host.trim() || !user.trim() || !pass.trim()) {
      notifications.show({ title: '请填写完整', message: 'IP 地址、用户名、密码为必填', color: 'yellow' })
      return
    }

    setLoading(true)
    try {
      const res = await fetch('/api/v1/nodes', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ hostAddress: host, username: user, password: pass, nodeName: name || undefined }),
      })
      const data = await res.json()

      if (res.ok) {
        notifications.show({
          title: '部署成功',
          message: `节点 ${data.nodeName || host} 已连接`,
          color: 'green',
        })
        setHost('')
        setUser('root')
        setPass('')
        setName('')
        setShowForm(false)
        onDeployed?.()
      } else {
        notifications.show({
          title: '部署失败',
          message: data.message || '请检查服务器状态',
          color: 'red',
        })
      }
    } catch {
      notifications.show({ title: '部署失败', message: '请检查网络连接', color: 'red' })
    } finally {
      setLoading(false)
    }
  }

  if (!showForm) {
    return (
      <Button
        leftSection={<Icon path={mdiPlus} size={0.82} />}
        onClick={() => setShowForm(true)}
        data-testid="one-click-deploy"
      >
        一键部署
      </Button>
    )
  }

  return (
    <YinyuPanel p="xs" className="yy-inline-deploy">
      <Group gap="xs" align="end" wrap="wrap">
        <TextInput
          label="节点名称"
          value={name}
          onChange={(event) => setName(event.currentTarget.value)}
          placeholder="可选"
          disabled={loading}
        />
        <TextInput
          label="IP 地址"
          required
          value={host}
          onChange={(event) => setHost(event.currentTarget.value)}
          placeholder="192.168.1.100"
          disabled={loading}
        />
        <TextInput
          label="用户名"
          required
          value={user}
          onChange={(event) => setUser(event.currentTarget.value)}
          disabled={loading}
          w={120}
        />
        <PasswordInput
          label="密码"
          required
          value={pass}
          onChange={(event) => setPass(event.currentTarget.value)}
          disabled={loading}
          w={160}
        />
        <Button leftSection={<Icon path={mdiCheck} size={0.82} />} loading={loading} onClick={handleDeploy}>
          确认
        </Button>
        <Button
          variant="default"
          leftSection={<Icon path={mdiClose} size={0.82} />}
          disabled={loading}
          onClick={() => setShowForm(false)}
        >
          取消
        </Button>
      </Group>
    </YinyuPanel>
  )
}
