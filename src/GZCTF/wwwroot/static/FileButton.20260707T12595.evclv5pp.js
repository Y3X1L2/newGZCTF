/* YINYU CTF Platform @unknown
 *
 * Commit    : Unofficial build version
 * Build     : 2026-07-07T12:59:55.597Z
 */
import{Et as e,Lt as t,Nt as n,un as r,xn as i}from"./Api.20260707T12595.cj2ps75j.js";import{A as a,O as o}from"./Popover.20260707T12595.do0vq1d0.js";var s=i(r(),1),c=t(),l=e(e=>{let{onChange:t,children:r,multiple:i,accept:l,name:u,form:d,resetRef:f,disabled:p,capture:m,inputProps:h,ref:g,..._}=n(`FileButton`,null,e),v=(0,s.useRef)(null),y=()=>{!p&&v.current?.click()};return o(f,()=>{v.current&&(v.current.value=``)}),(0,c.jsxs)(c.Fragment,{children:[(0,c.jsx)(`input`,{style:{display:`none`},type:`file`,accept:l,multiple:i,onChange:e=>{if(e.currentTarget.files===null)return t(i?[]:null);t(i?Array.from(e.currentTarget.files):e.currentTarget.files[0]||null)},ref:a(g,v),name:u,form:d,capture:m,...h}),r({onClick:y,..._})]})});l.displayName=`@mantine/core/FileButton`;export{l as t};