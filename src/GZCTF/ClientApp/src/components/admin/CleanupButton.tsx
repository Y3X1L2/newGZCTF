import { Button } from '@mantine/core';
import { notifications } from '@mantine/notifications';
import { useState } from 'react';

export function CleanupButton() {
  const [loading, setLoading] = useState(false);

  const handleCleanup = async () => {
    setLoading(true);
    try {
      // Cleanup stale containers via game container endpoints
      const res = await fetch('/api/v1/nodes', { method: 'DELETE' });
      if (res.ok)
        notifications.show({ title: '清理成功', message: '环境已清理', color: 'blue' });
      else
        notifications.show({ title: '清理失败', message: '请检查', color: 'red' });
    } catch {
      notifications.show({ title: '清理失败', message: '请检查网络连接', color: 'red' });
    } finally { setLoading(false); }
  };

  return <Button color="orange" loading={loading} onClick={handleCleanup} data-testid="one-click-cleanup">一键清理</Button>;
}
