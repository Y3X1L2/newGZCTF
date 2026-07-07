/* YINYU CTF Platform @unknown
 *
 * Commit    : Unofficial build version
 * Build     : 2026-07-07T12:59:55.597Z
 */
import{Lt as e,et as t,tt as n,un as r,xn as i}from"./Api.20260707T12595.cj2ps75j.js";import{t as a}from"./ScreenDisplayPage.20260707T12595.csirsdwh.js";var o=i(r(),1),s=e(),c=()=>{let e=t(),{id:r,mode:i}=n(),c=parseInt(r??`-1`,10);return(0,o.useEffect)(()=>{i!==`demo`&&e(`/admin/games/${c}/screen`,{replace:!0})},[i,e,c]),i===`demo`?(0,s.jsx)(a,{gameId:c,demoMode:!0}):null};export{c as default};