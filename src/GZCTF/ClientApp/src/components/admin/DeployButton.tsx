import { Button } from '@mantine/core';
import { notifications } from '@mantine/notifications';
import { useState } from 'react';

export function DeployButton() {
  const [loading, setLoading] = useState(false);

  const handleDeploy = async () => {
    setLoading(true);
    try {
      // Use nodes API to trigger deploy (one-click deploy calls NodeDeployService)
      const res = await fetch('/api/v1/nodes', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({}) });
      if (res.ok)
        notifications.show({ title: '部署成功', message: '已触发部署流程', color: 'green' });
      else
        notifications.show({ title: '部署失败', message: '请检查服务器状态', color: 'red' });
    } catch {
      notifications.show({ title: '部署失败', message: '请检查网络连接', color: 'red' });
    } finally { setLoading(false); }
  };

  return <Button color="green" loading={loading} onClick={handleDeploy} data-testid="one-click-deploy">一键部署</Button>;
}
