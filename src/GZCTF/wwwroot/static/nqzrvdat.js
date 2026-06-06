/* The GZ::CTF Project @unknown
 * 
 * License   : GNU Affero General Public License v3.0 (Core)
 * License   : LicenseRef-GZCTF-Restricted (Restricted components)
 * Commit    : Unofficial build version
 * Build     : 2026-05-20T05:57:19.720Z
 * Copyright (C) 2022-2026 GZTimeWalker. All Rights Reserved.
 */
import{g as e}from"./ngkkuwm9.js";import{t}from"./fxwlajkf2.js";import{t as n}from"./gi0mgpwi2.js";import{t as r}from"./fxim7u5m2.js";import{t as i}from"./g76hzan02.js";import{h as a,k as o,x as s}from"./index.ot524qw3.js";import{n as c}from"./j4c390o4.js";var l=e();function u(){let{id:e}=a(),{nodes:u}=c(),d=u?.find(t=>t.id===e);return d?(0,l.jsxs)(r,{shadow:`sm`,padding:`lg`,withBorder:!0,"data-testid":`node-detail-${d.id}`,children:[(0,l.jsxs)(t,{justify:`space-between`,mb:`md`,children:[(0,l.jsx)(s,{order:2,children:d.name}),(0,l.jsx)(o,{size:`lg`,color:d.status===`Online`?`green`:`red`,children:d.status})]}),(0,l.jsxs)(n,{children:[`地址: `,d.hostAddress]}),(0,l.jsx)(n,{mt:`md`,children:`CPU 负载`}),(0,l.jsx)(i,{value:d.cpuLoad*100,size:`lg`,color:d.cpuLoad>.8?`red`:`blue`,mb:`md`}),(0,l.jsx)(n,{children:`内存负载`}),(0,l.jsx)(i,{value:d.memoryLoad*100,size:`lg`,color:d.memoryLoad>.8?`red`:`blue`,mb:`md`}),(0,l.jsxs)(n,{children:[`容器: `,d.currentContainers,`/`,d.maxContainers]}),(0,l.jsxs)(n,{children:[`VM: `,d.currentVms,`/`,d.maxVms]}),d.lastHeartbeat&&(0,l.jsxs)(n,{size:`sm`,c:`dimmed`,mt:`md`,children:[`最后心跳: `,new Date(d.lastHeartbeat).toLocaleString()]})]}):(0,l.jsx)(n,{children:`节点不存在`})}export{u as default};