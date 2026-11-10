import { x25519 } from '@noble/curves/ed25519.js'
import type { Schema } from '../../api/client'

export function wireguardKeys() {
  const secret = x25519.utils.randomSecretKey()
  const publicKey = x25519.getPublicKey(secret)
  return {
    privateKey: btoa(String.fromCharCode(...secret)),
    publicKey: btoa(String.fromCharCode(...publicKey)),
  }
}

export function wireguardConfig(connection: Schema<'VPNConnection'>, privateKey?: string) {
  return [
    '[Interface]',
    ...(privateKey ? [`PrivateKey = ${privateKey}`] : []),
    `Address = ${connection.addresses.join(', ')}`,
    `MTU = ${connection.mtu}`,
    '',
    '[Peer]',
    `PublicKey = ${connection.publicKey}`,
    `AllowedIPs = ${connection.allowedIPs.join(', ')}`,
    `Endpoint = ${connection.endpoint}`,
    'PersistentKeepalive = 25',
    '',
  ].join('\n')
}

export function downloadWireguard(config: string) {
  const url = URL.createObjectURL(new Blob([config], { type: 'text/plain' }))
  const link = document.createElement('a')
  link.href = url
  link.download = 'WireGuard.conf'
  link.click()
  setTimeout(() => URL.revokeObjectURL(url), 0)
}
