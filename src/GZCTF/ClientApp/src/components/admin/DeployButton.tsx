import { Button } from '@mantine/core';
import { notifications } from '@mantine/notifications';
import { useState } from 'react';

export function DeployButton({ onDeployed }: { onDeployed?: () => void }) {
  const [loading, setLoading] = useState(false);
  const [showForm, setShowForm] = useState(false);
  const [host, setHost] = useState('');
  const [user, setUser] = useState('root');
  const [pass, setPass] = useState('');
  const [name, setName] = useState('');

  const handleDeploy = async () => {
    if (!host.trim() || !user.trim() || !pass.trim()) {
      notifications.show({ title: '请填写完整', message: 'IP地址、用户名、密码为必填', color: 'yellow' });
      return;
    }
    setLoading(true);
    try {
      const res = await fetch('/api/v1/nodes', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ hostAddress: host, username: user, password: pass, nodeName: name || undefined }),
      });
      const data = await res.json();
      if (res.ok) {
        notifications.show({
          title: '部署成功',
          message: `节点 ${data.nodeName || host} 已连接`,
          color: 'green',
        });
        setHost(''); setUser('root'); setPass(''); setName('');
        setShowForm(false);
        onDeployed?.();
      } else {
        notifications.show({
          title: '部署失败',
          message: data.message || '请检查服务器状态',
          color: 'red',
        });
      }
    } catch {
      notifications.show({ title: '部署失败', message: '请检查网络连接', color: 'red' });
    } finally { setLoading(false); }
  };

  if (!showForm) {
    return <Button color="green" onClick={() => setShowForm(true)} data-testid="one-click-deploy">一键部署</Button>;
  }

  return (
    <div style={{ display: 'flex', gap: 8, alignItems: 'flex-end', flexWrap: 'wrap' }}>
      <div style={{ flex: '1 1 150px', minWidth: 120 }}>
        <label style={{ fontSize: 12, color: '#666' }}>IP 地址</label>
        <input style={{ width: '100%', padding: '4px 8px', border: '1px solid #ccc', borderRadius: 4 }}
          value={host} onChange={e => setHost(e.target.value)} placeholder="192.168.1.100" />
      </div>
      <div style={{ flex: '0 0 100px' }}>
        <label style={{ fontSize: 12, color: '#666' }}>用户名</label>
        <input style={{ width: '100%', padding: '4px 8px', border: '1px solid #ccc', borderRadius: 4 }}
          value={user} onChange={e => setUser(e.target.value)} />
      </div>
      <div style={{ flex: '0 0 120px' }}>
        <label style={{ fontSize: 12, color: '#666' }}>密码</label>
        <input type="password" style={{ width: '100%', padding: '4px 8px', border: '1px solid #ccc', borderRadius: 4 }}
          value={pass} onChange={e => setPass(e.target.value)} />
      </div>
      <Button color="green" loading={loading} onClick={handleDeploy}>确认</Button>
      <Button variant="subtle" onClick={() => setShowForm(false)}>取消</Button>
    </div>
  );
}
