/* The GZ::CTF Project @unknown
 * 
 * License   : GNU Affero General Public License v3.0 (Core)
 * License   : LicenseRef-GZCTF-Restricted (Restricted components)
 * Commit    : Unofficial build version
 * Build     : 2026-05-20T05:57:19.720Z
 * Copyright (C) 2022-2026 GZTimeWalker. All Rights Reserved.
 */
function e(e){return typeof e!=`string`||!e.includes(`var(--mantine-scale)`)?e:e.match(/^calc\((.*?)\)$/)?.[1].split(`*`)[0].trim()}function t(t){let n=e(t);return typeof n==`number`?n:typeof n==`string`?n.includes(`calc`)||n.includes(`var`)?n:n.includes(`px`)?Number(n.replace(`px`,``)):n.includes(`rem`)?Number(n.replace(`rem`,``))*16:n.includes(`em`)?Number(n.replace(`em`,``))*16:Number(n):NaN}export{t};