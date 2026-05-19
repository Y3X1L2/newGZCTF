import { Button } from '@mantine/core';
import { notifications } from '@mantine/notifications';
import { useState } from 'react';
import { useDeploy } from '../../hooks/useNodes';

export function CleanupButton() {
  const [loading, setLoading] = useState(false);
  const { cleanup } = useDeploy();

  const handleCleanup = async () => {
    setLoading(true);
    try {
      await cleanup();
      notifications.show({ title: '清理成功', message: '环境已清理', color: 'blue' });
    } catch {
      notifications.show({ title: '清理失败', message: '请检查', color: 'red' });
    } finally { setLoading(false); }
  };

  return <Button color="orange" loading={loading} onClick={handleCleanup} data-testid="one-click-cleanup">一键清理</Button>;
}
