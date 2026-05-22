import { Button } from '@mantine/core';
import { notifications } from '@mantine/notifications';
import { useState } from 'react';

export function CleanupButton({ onCleanup }: { onCleanup?: () => void }) {
  const [loading, setLoading] = useState(false);

  const handleCleanup = async () => {
    setLoading(true);
    try {
      const listRes = await fetch('/api/v1/nodes');
      if (!listRes.ok) {
        notifications.show({ title: '清理失败', message: '无法获取节点列表', color: 'red' });
        return;
      }
      const nodes = await listRes.json();
      const offlineNodes = nodes.filter((n: any) => n.status === 'Offline');
      if (offlineNodes.length === 0) {
        notifications.show({ title: '无需清理', message: '没有离线节点', color: 'blue' });
        return;
      }
      let removed = 0;
      for (const node of offlineNodes) {
        const res = await fetch(`/api/v1/nodes/${node.id}`, { method: 'DELETE' });
        if (res.ok) removed++;
      }
      notifications.show({ title: '清理完成', message: `已移除 ${removed} 个离线节点`, color: 'green' });
      onCleanup?.();
    } catch {
      notifications.show({ title: '清理失败', message: '请检查网络连接', color: 'red' });
    } finally { setLoading(false); }
  };

  return <Button color="orange" loading={loading} onClick={handleCleanup} data-testid="one-click-cleanup">清理离线节点</Button>;
}
