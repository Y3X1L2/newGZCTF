/* YINYU CTF Platform @unknown
 *
 * Commit    : Unofficial build version
 * Build     : 2026-06-09T06:10:58.565Z
 */
import{a as e,t}from"./izagbnaw.js";var n=e(t(),1);function r(e={timeout:2e3}){let[t,r]=(0,n.useState)(null),[i,a]=(0,n.useState)(!1),[o,s]=(0,n.useState)(null),c=t=>{window.clearTimeout(o),s(window.setTimeout(()=>a(!1),e.timeout)),a(t)};return{copy:e=>{`clipboard`in navigator?navigator.clipboard.writeText(e).then(()=>c(!0)).catch(e=>r(e)):r(Error(`useClipboard: navigator.clipboard is not supported`))},reset:()=>{a(!1),r(null),window.clearTimeout(o)},error:t,copied:i}}export{r as t};