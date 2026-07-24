function twoDigits(value: number) {
  return String(value).padStart(2, '0')
}

export function localDateKey(date: Date) {
  if (!Number.isFinite(date.getTime())) return ''
  return `${date.getFullYear()}-${twoDigits(date.getMonth() + 1)}-${twoDigits(date.getDate())}`
}
