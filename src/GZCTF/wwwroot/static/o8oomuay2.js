/* YINYU CTF Platform @unknown
 *
 * Commit    : Unofficial build version
 * Build     : 2026-06-09T06:10:58.565Z
 */
import{a as e,t}from"./izagbnaw.js";import{d as n,h as r,i}from"./i794nfmz.js";import{C as a,x as o}from"./h7v7f6o0.js";var s=e(t(),1),c=r(),l=i(e=>{let{onChange:t,children:r,multiple:i,accept:l,name:u,form:d,resetRef:f,disabled:p,capture:m,inputProps:h,ref:g,..._}=n(`FileButton`,null,e),v=(0,s.useRef)(null),y=()=>{!p&&v.current?.click()};return o(f,()=>{v.current&&(v.current.value=``)}),(0,c.jsxs)(c.Fragment,{children:[(0,c.jsx)(`input`,{style:{display:`none`},type:`file`,accept:l,multiple:i,onChange:e=>{if(e.currentTarget.files===null)return t(i?[]:null);t(i?Array.from(e.currentTarget.files):e.currentTarget.files[0]||null)},ref:a(g,v),name:u,form:d,capture:m,...h}),r({onClick:y,..._})]})});l.displayName=`@mantine/core/FileButton`;export{l as t};