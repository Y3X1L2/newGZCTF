import { Button, Group, Stack, Text, ThemeIcon } from '@mantine/core'
import { showNotification } from '@mantine/notifications'
import { mdiAlertCircleOutline, mdiCheck, mdiClose, mdiDesktopClassic, mdiMonitorScreenshot } from '@mdi/js'
import { Icon } from '@mdi/react'
import { FC, useCallback, useEffect, useState } from 'react'
import { YinyuPanel, YinyuRouteLoader, YinyuStatusPill } from '@Components/yinyu/YinyuUI'
import { VmStatusResponse } from '@Api'

interface VmInstanceEntryProps {
  gameId: number
  challengeId: number
  disabled?: boolean
  onCreateVm?: () => void
  onDestroyVm?: () => void
}

type VmState = 'none' | 'creating' | 'running' | 'ready' | 'error' | 'destroying'

export const VmInstanceEntry: FC<VmInstanceEntryProps> = ({
  gameId,
  challengeId,
  disabled,
  onCreateVm,
  onDestroyVm,
}) => {
  const [vmState, setVmState] = useState<VmState>('none')
  const [vmStatus, setVmStatus] = useState<VmStatusResponse | null>(null)
  const [loading, setLoading] = useState(false)
  const [polling, setPolling] = useState(false)

  const checkVmStatus = useCallback(async () => {
    try {
      const response = await fetch(`/api/Game/${gameId}/Vm/${challengeId}`, {
        credentials: 'include',
      })

      if (response.status === 404) {
        setVmState('none')
        setVmStatus(null)
        return
      }

      if (!response.ok) return

      const data: VmStatusResponse = await response.json()
      setVmStatus(data)

      if (data.status === 'Creating' || data.status === 'Running') {
        if (data.rdpUrl) {
          setVmState('ready')
          setPolling(false)
        } else {
          setVmState(data.status === 'Creating' ? 'creating' : 'running')
          setPolling(true)
        }
      } else if (data.status === 'Error') {
        setVmState('error')
        setPolling(false)
      } else if (data.status === 'Destroyed') {
        setVmState('none')
        setVmStatus(null)
        setPolling(false)
      }
    } catch (err) {
      console.error('Failed to check VM status:', err)
    }
  }, [gameId, challengeId])

  useEffect(() => {
    checkVmStatus()
  }, [checkVmStatus])

  useEffect(() => {
    if (!polling) return

    const interval = setInterval(checkVmStatus, 5000)
    return () => clearInterval(interval)
  }, [polling, checkVmStatus])

  const handleCreate = async () => {
    setLoading(true)
    try {
      const response = await fetch(`/api/Game/${gameId}/Container/${challengeId}`, {
        method: 'POST',
        credentials: 'include',
      })

      if (response.ok) {
        setVmState('creating')
        setPolling(true)
        onCreateVm?.()
        showNotification({
          color: 'teal',
          title: '靶机启动中',
          message: 'Windows 虚拟机正在创建，请等待 1-3 分钟。',
          icon: <Icon path={mdiCheck} size={1} />,
        })
      } else {
        const err = await response.json().catch(() => ({}))
        showNotification({
          color: 'red',
          title: '启动失败',
          message: err.title || err.message || '请稍后重试。',
          icon: <Icon path={mdiClose} size={1} />,
        })
      }
    } catch {
      showNotification({
        color: 'red',
        title: '网络错误',
        message: '无法连接服务器，请检查网络后重试。',
        icon: <Icon path={mdiClose} size={1} />,
      })
    } finally {
      setLoading(false)
    }
  }

  const handleDestroy = async () => {
    setLoading(true)
    setVmState('destroying')
    try {
      const response = await fetch(`/api/Game/${gameId}/Vm/${challengeId}`, {
        method: 'DELETE',
        credentials: 'include',
      })

      if (response.ok) {
        setVmState('none')
        setVmStatus(null)
        setPolling(false)
        onDestroyVm?.()
        showNotification({
          color: 'teal',
          title: '靶机已销毁',
          message: '虚拟机资源已释放。',
          icon: <Icon path={mdiCheck} size={1} />,
        })
      } else {
        setVmState('ready')
        showNotification({
          color: 'red',
          title: '销毁失败',
          message: '请稍后重试。',
          icon: <Icon path={mdiClose} size={1} />,
        })
      }
    } catch {
      setVmState('ready')
      showNotification({
        color: 'red',
        title: '网络错误',
        message: '无法连接服务器，请检查网络后重试。',
        icon: <Icon path={mdiClose} size={1} />,
      })
    } finally {
      setLoading(false)
    }
  }

  const handleOpenRdp = () => {
    if (vmStatus?.rdpUrl) {
      window.open(vmStatus.rdpUrl, '_blank', 'noopener,noreferrer')
    }
  }

  if (vmState === 'none') {
    return (
      <YinyuPanel p="sm" cells={24} className="yy-instance-panel yy-vm-entry-panel">
        <Group justify="space-between" wrap="nowrap" className="yy-instance-row">
          <Stack gap={2}>
            <Text size="sm" fw="bold">
              本题需要启动 Windows 远程靶机
            </Text>
            <Text size="xs" fw="bold" className="yy-readable-text">
              点击右侧按钮创建靶机，启动约需 1-3 分钟。
            </Text>
          </Stack>
          <Button
            onClick={handleCreate}
            disabled={disabled || loading}
            leftSection={
              loading ? undefined : <Icon path={mdiDesktopClassic} size={0.9} />
            }
          >
            {loading ? '正在启动' : '启动靶机'}
          </Button>
        </Group>
      </YinyuPanel>
    )
  }

  if (vmState === 'creating' || vmState === 'running') {
    const creating = vmState === 'creating'
    return (
      <YinyuPanel p="sm" cells={28} className="yy-instance-panel yy-vm-entry-panel">
        <Stack gap="sm" w="100%">
          <YinyuStatusPill tone="warm" state="running">
            {creating ? '靶机创建中' : '等待靶机就绪'}
          </YinyuStatusPill>
          <Group justify="space-between" wrap="nowrap" className="yy-instance-row yy-instance-loading-row">
            <YinyuRouteLoader
              title={creating ? '正在启动靶机' : '正在配置远程桌面'}
              description={creating ? '正在克隆镜像并启动虚拟机' : '虚拟机已启动，正在获取网络地址与 RDP 入口'}
              className="yy-instance-loader"
            />
            <Button color="red" variant="light" onClick={handleDestroy} disabled={loading} size="sm">
              取消
            </Button>
          </Group>
        </Stack>
      </YinyuPanel>
    )
  }

  if (vmState === 'ready') {
    return (
      <YinyuPanel p="sm" cells={28} className="yy-instance-panel yy-vm-entry-panel">
        <Stack gap="sm" w="100%">
          <YinyuStatusPill tone="success" state="solved">
            靶机就绪
          </YinyuStatusPill>
          <Group justify="space-between" wrap="nowrap" className="yy-instance-row">
            <Group gap="sm" wrap="nowrap">
              <ThemeIcon color="teal" variant="light" size="lg">
                <Icon path={mdiMonitorScreenshot} size={1} />
              </ThemeIcon>
              <Stack gap={0}>
                <Text size="sm" fw="bold">
                  远程桌面已配置
                </Text>
                <Text size="xs" className="yy-readable-text">
                  IP: {vmStatus?.ipAddress ?? '未知'} | 可通过 RDP 入口进入靶机
                </Text>
              </Stack>
            </Group>
            <Group gap="xs" wrap="nowrap">
              <Button onClick={handleOpenRdp} leftSection={<Icon path={mdiMonitorScreenshot} size={0.9} />} color="teal">
                打开远程桌面
              </Button>
              <Button color="red" variant="light" onClick={handleDestroy} disabled={loading}>
                销毁靶机
              </Button>
            </Group>
          </Group>
        </Stack>
      </YinyuPanel>
    )
  }

  if (vmState === 'destroying') {
    return (
      <YinyuPanel p="sm" cells={20} className="yy-instance-panel yy-vm-entry-panel">
        <YinyuRouteLoader title="正在销毁靶机" description="正在释放虚拟机与远程桌面资源" className="yy-instance-loader" />
      </YinyuPanel>
    )
  }

  return (
    <YinyuPanel p="sm" cells={24} className="yy-instance-panel yy-vm-entry-panel">
      <Group justify="space-between" wrap="nowrap" className="yy-instance-row">
        <Group gap="sm" wrap="nowrap">
          <ThemeIcon color="red" variant="light" size="lg">
            <Icon path={mdiAlertCircleOutline} size={1} />
          </ThemeIcon>
          <Stack gap={0}>
            <Text size="sm" fw="bold" c="red">
              靶机异常
            </Text>
            <Text size="xs" className="yy-readable-text">
              虚拟机创建失败或超时，请重新启动。
            </Text>
          </Stack>
        </Group>
        <Button
          onClick={handleCreate}
          disabled={loading}
        >
          {loading ? '正在重启' : '重新启动'}
        </Button>
      </Group>
    </YinyuPanel>
  )
}
