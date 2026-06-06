/* The GZ::CTF Project @unknown
 * 
 * License   : GNU Affero General Public License v3.0 (Core)
 * License   : LicenseRef-GZCTF-Restricted (Restricted components)
 * Commit    : Unofficial build version
 * Build     : 2026-05-20T05:57:19.720Z
 * Copyright (C) 2022-2026 GZTimeWalker. All Rights Reserved.
 */
import{a as e,t}from"./izagbnaw.js";import{f as n,g as r,r as i}from"./ngkkuwm9.js";import{C as a,T as o}from"./j3x5isu1.js";var s=e(t(),1),c=r(),l=i(e=>{let{onChange:t,children:r,multiple:i,accept:l,name:u,form:d,resetRef:f,disabled:p,capture:m,inputProps:h,ref:g,..._}=n(`FileButton`,null,e),v=(0,s.useRef)(null),y=()=>{!p&&v.current?.click()};return a(f,()=>{v.current&&(v.current.value=``)}),(0,c.jsxs)(c.Fragment,{children:[(0,c.jsx)(`input`,{style:{display:`none`},type:`file`,accept:l,multiple:i,onChange:e=>{if(e.currentTarget.files===null)return t(i?[]:null);t(i?Array.from(e.currentTarget.files):e.currentTarget.files[0]||null)},ref:o(g,v),name:u,form:d,capture:m,...h}),r({onClick:y,..._})]})});l.displayName=`@mantine/core/FileButton`;export{l as t};