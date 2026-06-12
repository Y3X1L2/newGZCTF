export function hexToRgbNormalized(hex: string) {
  const n = hex.startsWith('#') ? hex.slice(1) : hex
  const bigint = Number.parseInt(n, 16)
  return [((bigint >> 16) & 255) / 255, ((bigint >> 8) & 255) / 255, (bigint & 255) / 255]
}
