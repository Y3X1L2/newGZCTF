export function externalEntryHref(entry: string) {
  if (/^[a-z][a-z\d+.-]*:\/\//i.test(entry)) return entry
  return `http://${entry}`
}
