# 靶机节点部署指南

## 1. 在靶机服务器上安装依赖
```bash
# Docker
curl -fsSL https://get.docker.com | sudo bash
sudo usermod -aG docker $USER

# KVM (可选，用于 Windows VM)
sudo apt install -y qemu-kvm libvirt-daemon-system virtinst
```

## 2. 在平台管理面板添加节点
进入管理面板 → 节点管理 → 添加靶机服务器
填写：IP、SSH 用户名、密码
平台自动连接并检测能力

## 3. 验证
节点状态显示为"Online"即就绪
题目部署时选择此节点即可
```
