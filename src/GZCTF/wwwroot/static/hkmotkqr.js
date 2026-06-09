/* YINYU CTF Platform @unknown
 *
 * Commit    : Unofficial build version
 * Build     : 2026-06-09T06:10:58.565Z
 */
function e(e){return typeof e!=`string`||!e.includes(`var(--mantine-scale)`)?e:e.match(/^calc\((.*?)\)$/)?.[1].split(`*`)[0].trim()}function t(t){let n=e(t);return typeof n==`number`?n:typeof n==`string`?n.includes(`calc`)||n.includes(`var`)?n:n.includes(`px`)?Number(n.replace(`px`,``)):n.includes(`rem`)?Number(n.replace(`rem`,``))*16:n.includes(`em`)?Number(n.replace(`em`,``))*16:Number(n):NaN}export{t};