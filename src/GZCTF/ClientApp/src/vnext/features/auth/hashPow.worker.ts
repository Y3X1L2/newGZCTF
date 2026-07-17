interface PowRequest {
  challenge: string
  difficulty: number
}

interface PowResult {
  nonce: string | null
  time: number
  rate: number
}

function parsePrefix(hex: string) {
  const bytes = new Uint8Array(hex.length / 2 + 4)
  for (let index = 0; index < hex.length; index += 2) bytes[index / 2] = Number.parseInt(hex.slice(index, index + 2), 16)
  crypto.getRandomValues(bytes.subarray(hex.length / 2))
  return bytes
}

function concatNonce(prefix: Uint8Array, nonce: number) {
  const buffer = new Uint8Array(prefix.length + 4)
  buffer.set(prefix, 0)
  for (let index = 0; index < 4; index += 1) buffer[prefix.length + index] = (nonce >> (24 - index * 8)) & 0xff
  return buffer
}

function leadingZeros(hash: Uint8Array) {
  let count = 0
  for (const byte of hash) {
    if (byte === 0) {
      count += 8
      continue
    }
    for (let mask = 0x80; mask > 0 && (byte & mask) === 0; mask >>= 1) count += 1
    break
  }
  return count
}

function nonceValue(prefix: Uint8Array, nonce: number) {
  const random = Array.from(prefix.slice(prefix.length - 4), (byte) => byte.toString(16).padStart(2, '0')).join('')
  return `${random}${nonce.toString(16).padStart(8, '0')}`
}

async function solve(request: PowRequest): Promise<PowResult> {
  if (!crypto?.subtle) return { nonce: null, time: 0, rate: 0 }

  const prefix = parsePrefix(request.challenge)
  let nonce = Math.floor(Math.random() * 0xffffffff)
  const initialNonce = nonce
  const startedAt = performance.now()

  while (true) {
    const digest = await crypto.subtle.digest('SHA-256', concatNonce(prefix, nonce))
    if (leadingZeros(new Uint8Array(digest)) >= request.difficulty) break
    nonce += 1
  }

  const time = Math.max(1, performance.now() - startedAt)
  return { nonce: nonceValue(prefix, nonce), time, rate: (nonce - initialNonce) / time }
}

self.onmessage = async (event: MessageEvent<PowRequest>) => self.postMessage(await solve(event.data))

export {}
