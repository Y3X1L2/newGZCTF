import { Alert, Button, Modal, Stack, Text, TextInput } from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { mdiCheckboxMarkedCircleOutline, mdiPlus, mdiProgressWrench, mdiServerNetwork } from '@mdi/js'
import { Icon } from '@mdi/react'
import { useState } from 'react'
import { YinyuModalBody } from '@Components/yinyu/YinyuUI'

export function AddNodeModal({ opened, onClose, onAdded }: { opened: boolean; onClose: () => void; onAdded: () => void }) {
  const [host, setHost] = useState('')
  const [user, setUser] = useState('root')
  const [pass, setPass] = useState('')
  const [name, setName] = useState('')
  const [loading, setLoading] = useState(false)

  const handleAdd = async () => {
    if (!host.trim() || !user.trim() || !pass) return

    setLoading(true)
    try {
      const res = await fetch('/api/v1/nodes', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          hostAddress: host.trim(),
          username: user.trim(),
          password: pass,
          nodeName: name.trim() || null,
        }),
      })
      const data = await res.json().catch(() => ({}))
      if (res.ok) {
        notifications.show({
          title: '部署成功',
          message: `节点 ${data.nodeName || host} 已接入，能力：${data.capabilities ?? '已检测'}`,
          color: 'green',
        })
        onAdded()
        onClose()
        setHost('')
        setUser('root')
        setPass('')
        setName('')
      } else {
        notifications.show({
          title: '部署失败',
          message: data.message || '请检查服务器地址、账号权限、包源和 Docker/KVM 支持状态',
          color: 'red',
          autoClose: 9000,
        })
      }
    } catch {
      notifications.show({
        title: '连接失败',
        message: '无法连接平台 API',
        color: 'red',
      })
    } finally {
      setLoading(false)
    }
  }

  return (
    <Modal
      opened={opened}
      onClose={loading ? () => undefined : onClose}
      title="添加目标服务器"
      data-testid="add-node-modal"
      radius="sm"
      centered
      closeOnClickOutside={!loading}
    >
      <YinyuModalBody p="md">
        <Stack gap="md">
          <Alert
            variant="light"
            color="blue"
            radius="sm"
            icon={<Icon path={loading ? mdiProgressWrench : mdiServerNetwork} size={0.85} />}
          >
            <Stack gap={4}>
              <Text size="sm" fw={700}>
                {loading ? '正在自动部署节点' : '一站式接入工作节点'}
              </Text>
              <Text size="xs" className="yy-readable-text">
                {loading
                  ? '平台正在通过 SSH 探测环境、安装 Docker/KVM/libvirt、写入 Agent 配置并等待心跳。'
                  : '提交后会自动探测并安装分布式运行所需依赖，完成后节点会出现在调度池中。'}
              </Text>
            </Stack>
          </Alert>
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
            placeholder="10.0.7.125"
            disabled={loading}
          />
          <TextInput
            label="用户名"
            required
            value={user}
            onChange={(event) => setUser(event.currentTarget.value)}
            disabled={loading}
          />
          <TextInput
            label="密码"
            type="password"
            required
            value={pass}
            onChange={(event) => setPass(event.currentTarget.value)}
            disabled={loading}
          />
          <Alert
            variant="outline"
            color="gray"
            radius="sm"
            icon={<Icon path={mdiCheckboxMarkedCircleOutline} size={0.78} />}
          >
            <Text size="xs" className="yy-readable-text">
              目标账号需要 root 或免密 sudo 权限；重复添加同一 IP 会复用原节点并重新安装 Agent。
            </Text>
          </Alert>
          <Button
            fullWidth
            leftSection={<Icon path={mdiPlus} size={0.8} />}
            loading={loading}
            onClick={handleAdd}
            data-testid="confirm-add-node"
          >
            {loading ? '正在部署，等待节点心跳' : '一键部署'}
          </Button>
        </Stack>
      </YinyuModalBody>
    </Modal>
  )
}
