import { Button } from '@mantine/core';
import { notifications } from '@mantine/notifications';
import { useState } from 'react';
import { useDeploy } from '../../hooks/useNodes';

export function DeployButton() {
  const [loading, setLoading] = useState(false);
  const { deploy } = useDeploy();

  const handleDeploy = async () => {
    setLoading(true);
    try {
      await deploy();
      notifications.show({ title: '部署成功', message: '所有服务已启动', color: 'green' });
    } catch {
      notifications.show({ title: '部署失败', message: '请检查服务器状态', color: 'red' });
    } finally { setLoading(false); }
  };

  return <Button color="green" loading={loading} onClick={handleDeploy} data-testid="one-click-deploy">一键部署</Button>;
}
