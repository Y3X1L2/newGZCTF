/* The GZ::CTF Project @unknown
 * 
 * License   : GNU Affero General Public License v3.0 (Core)
 * License   : LicenseRef-GZCTF-Restricted (Restricted components)
 * Commit    : Unofficial build version
 * Build     : 2026-05-20T05:57:19.720Z
 * Copyright (C) 2022-2026 GZTimeWalker. All Rights Reserved.
 */
import{g as e}from"./ngkkuwm9.js";import{t}from"./fxwlajkf2.js";import{t as n}from"./gi0mgpwi2.js";import{t as r}from"./fxim7u5m2.js";import{t as i}from"./g76hzan02.js";import{k as a}from"./index.ot524qw3.js";var o=e();function s({node:e}){let s=e.status===`Online`?`green`:e.status===`Offline`?`red`:`yellow`;return(0,o.jsxs)(r,{shadow:`sm`,padding:`md`,withBorder:!0,"data-testid":`node-card-${e.id}`,children:[(0,o.jsxs)(t,{justify:`space-between`,mb:`xs`,children:[(0,o.jsx)(n,{fw:700,children:e.name}),(0,o.jsx)(a,{color:s,children:e.status})]}),(0,o.jsx)(n,{size:`sm`,c:`dimmed`,children:e.hostAddress}),(0,o.jsxs)(n,{size:`xs`,mt:`xs`,children:[`CPU: `,(e.cpuLoad*100).toFixed(0),`%`]}),(0,o.jsx)(i,{value:e.cpuLoad*100,color:e.cpuLoad>.8?`red`:`blue`,size:`sm`,mb:`xs`}),(0,o.jsxs)(n,{size:`xs`,children:[`容器: `,e.currentContainers,`/`,e.maxContainers]}),(0,o.jsxs)(n,{size:`xs`,children:[`VM: `,e.currentVms,`/`,e.maxVms]})]})}export{s as t};