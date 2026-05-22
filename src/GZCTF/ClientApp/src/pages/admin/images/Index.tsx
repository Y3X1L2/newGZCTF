import { useState, useRef } from 'react';
import {
  Table, Title, Text, Badge, Button, Group, Modal, Stack,
  TextInput, Select, FileInput, ActionIcon, Tooltip,
} from '@mantine/core';
import { notifications } from '@mantine/notifications';
import { mdiDeleteOutline, mdiRefresh } from '@mdi/js';
import { Icon } from '@mdi/react';
import useSWR from 'swr';

const fetcher = (url: string) => fetch(url).then(r => r.json());

const imageTypeLabels: Record<number, string> = {
  0: 'Docker', 1: 'Qcow2', 2: 'OVA', 3: 'VMDK',
};

const statusConfig: Record<number, { label: string; color: string }> = {
  0: { label: 'Ready', color: 'green' },
  1: { label: 'Importing', color: 'yellow' },
  2: { label: 'Error', color: 'red' },
};

function RegisterDockerModal({ opened, onClose, onDone }: { opened: boolean; onClose: () => void; onDone: () => void }) {
  const [name, setName] = useState('');
  const [url, setUrl] = useState('');
  const [osType, setOsType] = useState<string>('0');
  const [loading, setLoading] = useState(false);

  const handleSubmit = async () => {
    if (!name.trim() || !url.trim()) return;
    setLoading(true);
    try {
      const res = await fetch('/api/v1/image-templates/register-docker', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ name, registryUrl: url, osType: Number(osType) }),
      });
      if (res.ok) {
        notifications.show({ title: '注册成功', message: `Docker 镜像 ${name} 已注册`, color: 'green' });
        setName(''); setUrl(''); setOsType('0');
        onDone(); onClose();
      } else {
        const data = await res.json().catch(() => ({}));
        notifications.show({ title: '注册失败', message: data.message || '请检查输入', color: 'red' });
      }
    } catch {
      notifications.show({ title: '注册失败', message: '网络错误', color: 'red' });
    } finally { setLoading(false); }
  };

  return (
    <Modal opened={opened} onClose={onClose} title="注册 Docker 镜像">
      <Stack>
        <TextInput label="镜像名称" required value={name} onChange={e => setName(e.currentTarget.value)} placeholder="nginx:latest" />
        <TextInput label="Registry URL" required value={url} onChange={e => setUrl(e.currentTarget.value)} placeholder="registry.example.com/myimage:tag" />
        <Select label="操作系统" data={[{ value: '0', label: 'Linux' }, { value: '1', label: 'Windows' }]} value={osType} onChange={v => setOsType(v ?? '0')} />
        <Button fullWidth loading={loading} onClick={handleSubmit}>注册</Button>
      </Stack>
    </Modal>
  );
}

function ImportLocalModal({ opened, onClose, onDone }: { opened: boolean; onClose: () => void; onDone: () => void }) {
  const [path, setPath] = useState('');
  const [displayName, setDisplayName] = useState('');
  const [loading, setLoading] = useState(false);

  const handleSubmit = async () => {
    if (!path.trim()) return;
    setLoading(true);
    try {
      const res = await fetch('/api/v1/image-templates/import-local', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ localPath: path, displayName: displayName || undefined }),
      });
      if (res.ok) {
        notifications.show({ title: '导入成功', message: '镜像已从本地路径导入', color: 'green' });
        setPath(''); setDisplayName('');
        onDone(); onClose();
      } else {
        const data = await res.json().catch(() => ({}));
        notifications.show({ title: '导入失败', message: data.message || '请检查路径', color: 'red' });
      }
    } catch {
      notifications.show({ title: '导入失败', message: '网络错误', color: 'red' });
    } finally { setLoading(false); }
  };

  return (
    <Modal opened={opened} onClose={onClose} title="从本地路径导入">
      <Stack>
        <TextInput label="服务器本地路径" required value={path} onChange={e => setPath(e.currentTarget.value)} placeholder="/var/lib/images/template.qcow2" />
        <TextInput label="显示名称" value={displayName} onChange={e => setDisplayName(e.currentTarget.value)} placeholder="可选" />
        <Button fullWidth loading={loading} onClick={handleSubmit}>导入</Button>
      </Stack>
    </Modal>
  );
}

export default function ImagesPage() {
  const { data, isLoading, mutate } = useSWR('/api/v1/image-templates', fetcher);
  const [dockerModalOpen, setDockerModalOpen] = useState(false);
  const [localModalOpen, setLocalModalOpen] = useState(false);
  const [uploading, setUploading] = useState(false);
  const fileInputRef = useRef<HTMLInputElement>(null);

  const handleUploadArchive = async (file: File | null) => {
    if (!file) return;
    setUploading(true);
    try {
      const formData = new FormData();
      formData.append('file', file);
      const res = await fetch('/api/v1/image-templates/upload', { method: 'POST', body: formData });
      if (res.ok) {
        notifications.show({ title: '上传成功', message: '镜像压缩包已上传并处理', color: 'green' });
        mutate();
      } else {
        const data = await res.json().catch(() => ({}));
        notifications.show({ title: '上传失败', message: data.message || '请检查文件格式', color: 'red' });
      }
    } catch {
      notifications.show({ title: '上传失败', message: '网络错误', color: 'red' });
    } finally { setUploading(false); }
  };

  const handleDelete = async (id: number, name: string) => {
    if (!confirm(`确定删除模板 "${name}"？`)) return;
    try {
      const res = await fetch(`/api/v1/image-templates/${id}`, { method: 'DELETE' });
      if (res.ok) {
        notifications.show({ title: '删除成功', message: `模板 ${name} 已删除`, color: 'green' });
        mutate();
      } else {
        notifications.show({ title: '删除失败', message: '请检查', color: 'red' });
      }
    } catch {
      notifications.show({ title: '删除失败', message: '网络错误', color: 'red' });
    }
  };

  if (isLoading) return <Text>加载中...</Text>;

  return (
    <div>
      <Group justify="space-between" mb="lg">
        <Title order={2}>镜像模板管理</Title>
        <Group>
          <Button variant="default" leftSection={<Icon path={mdiRefresh} size={1} />} onClick={() => mutate()}>刷新</Button>
          <Button onClick={() => setDockerModalOpen(true)}>注册 Docker 镜像</Button>
          <Button variant="default" onClick={() => setLocalModalOpen(true)}>从本地导入</Button>
          <Button variant="default" loading={uploading} onClick={() => fileInputRef.current?.click()}>
            上传压缩包
          </Button>
          <input ref={fileInputRef} type="file" accept=".zip,.tar.gz,.tgz,.tar.xz,.txz" style={{ display: 'none' }}
            onChange={e => handleUploadArchive(e.target.files?.[0] ?? null)} />
        </Group>
      </Group>
      <Table>
        <Table.Thead>
          <Table.Tr>
            <Table.Th>名称</Table.Th>
            <Table.Th>类型</Table.Th>
            <Table.Th>系统</Table.Th>
            <Table.Th>大小</Table.Th>
            <Table.Th>状态</Table.Th>
            <Table.Th>操作</Table.Th>
          </Table.Tr>
        </Table.Thead>
        <Table.Tbody>
          {(data?.items as any[])?.length === 0 && (
            <Table.Tr><Table.Td colSpan={6} style={{ textAlign: 'center' }}><Text c="dimmed">暂无镜像模板</Text></Table.Td></Table.Tr>
          )}
          {(data?.items as any[])?.map((img: any) => {
            const st = statusConfig[img.status] ?? { label: 'Unknown', color: 'gray' };
            return (
              <Table.Tr key={img.id}>
                <Table.Td>{img.name}</Table.Td>
                <Table.Td>{imageTypeLabels[img.imageType] ?? img.imageType}</Table.Td>
                <Table.Td><Badge>{img.osType === 0 ? 'Linux' : 'Windows'}</Badge></Table.Td>
                <Table.Td>{img.fileSize > 0 ? `${(img.fileSize / 1024 / 1024).toFixed(1)} MB` : '-'}</Table.Td>
                <Table.Td><Badge color={st.color}>{st.label}</Badge></Table.Td>
                <Table.Td>
                  <Tooltip label="删除">
                    <ActionIcon color="red" variant="subtle" onClick={() => handleDelete(img.id, img.name)}>
                      <Icon path={mdiDeleteOutline} size={1} />
                    </ActionIcon>
                  </Tooltip>
                </Table.Td>
              </Table.Tr>
            );
          })}
        </Table.Tbody>
      </Table>
      <RegisterDockerModal opened={dockerModalOpen} onClose={() => setDockerModalOpen(false)} onDone={() => mutate()} />
      <ImportLocalModal opened={localModalOpen} onClose={() => setLocalModalOpen(false)} onDone={() => mutate()} />
    </div>
  );
}
