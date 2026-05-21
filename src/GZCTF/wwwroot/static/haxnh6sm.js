/* The GZ::CTF Project @unknown
 * 
 * License   : GNU Affero General Public License v3.0 (Core)
 * License   : LicenseRef-GZCTF-Restricted (Restricted components)
 * Commit    : Unofficial build version
 * Build     : 2026-05-20T05:57:19.720Z
 * Copyright (C) 2022-2026 GZTimeWalker. All Rights Reserved.
 */
import{g as e}from"./ngkkuwm9.js";import{t}from"./gi0mgpwi2.js";import{t as n}from"./d67vm5lx2.js";import{n as r}from"./f9u6cnui.js";import{k as i,x as a}from"./index.ot524qw3.js";var o=e(),s=e=>fetch(e).then(e=>e.json());function c(){let{data:e,isLoading:c}=r(`/api/v1/image-templates`,s);return c?(0,o.jsx)(t,{children:`加载中...`}):(0,o.jsxs)(`div`,{children:[(0,o.jsx)(a,{order:2,mb:`lg`,children:`镜像模板管理`}),(0,o.jsxs)(n,{children:[(0,o.jsx)(n.Thead,{children:(0,o.jsxs)(n.Tr,{children:[(0,o.jsx)(n.Th,{children:`名称`}),(0,o.jsx)(n.Th,{children:`类型`}),(0,o.jsx)(n.Th,{children:`系统`}),(0,o.jsx)(n.Th,{children:`大小`}),(0,o.jsx)(n.Th,{children:`状态`})]})}),(0,o.jsx)(n.Tbody,{children:e?.map(e=>(0,o.jsxs)(n.Tr,{children:[(0,o.jsx)(n.Td,{children:e.name}),(0,o.jsx)(n.Td,{children:e.imageType}),(0,o.jsx)(n.Td,{children:(0,o.jsx)(i,{children:e.osType===0?`Linux`:`Windows`})}),(0,o.jsxs)(n.Td,{children:[(e.fileSize/1024/1024).toFixed(1),` MB`]}),(0,o.jsx)(n.Td,{children:(0,o.jsx)(i,{color:e.status===0?`green`:`yellow`,children:e.status===0?`Ready`:`Importing`})})]},e.id))})]})]})}export{c as default};