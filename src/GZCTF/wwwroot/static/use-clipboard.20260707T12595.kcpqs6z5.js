/* YINYU CTF Platform @unknown
 *
 * Commit    : Unofficial build version
 * Build     : 2026-07-07T12:59:55.597Z
 */
import{un as e,xn as t}from"./Api.20260707T12595.cj2ps75j.js";var n=t(e(),1);function r(e={timeout:2e3}){let[t,r]=(0,n.useState)(null),[i,a]=(0,n.useState)(!1),[o,s]=(0,n.useState)(null),c=t=>{window.clearTimeout(o),s(window.setTimeout(()=>a(!1),e.timeout)),a(t)};return{copy:e=>{`clipboard`in navigator?navigator.clipboard.writeText(e).then(()=>c(!0)).catch(e=>r(e)):r(Error(`useClipboard: navigator.clipboard is not supported`))},reset:()=>{a(!1),r(null),window.clearTimeout(o)},error:t,copied:i}}export{r as t};