/* The GZ::CTF Project @unknown
 * 
 * License   : GNU Affero General Public License v3.0 (Core)
 * License   : LicenseRef-GZCTF-Restricted (Restricted components)
 * Commit    : Unofficial build version
 * Build     : 2026-05-20T05:57:19.720Z
 * Copyright (C) 2022-2026 GZTimeWalker. All Rights Reserved.
 */
import{P as e,W as t,g as n,m as r}from"./ngkkuwm9.js";import{w as i}from"./lo2h6db2.js";import{K as a}from"./hmbv74xw.js";import{t as o}from"./fxwlajkf2.js";var s={bar:`gc`,pulse:`hc`,box:`ic`,back:`jc`,spikes:`kc`,spike:`lc`,l:`mc`,r:`nc`,t:`oc`,b:`pc`},c=n(),l=n=>{let{thickness:l=4,spikeLength:u=250,percentage:d,color:f,...p}=n,m=r(),{colorScheme:h}=i(),g=d<100,_=g?h===`dark`?`light`:f??m.primaryColor:`gray`,v=m.colors[_][5],y=m.colors[_][2];return(0,c.jsx)(a,{py:l*u/100,...p,__vars:{"--thickness":t(l),"--spike-length":`${u}%`,"--neg-spike-length":`${-u}%`,"--percentage":`${d}%`,"--spike-color":v,"--bg-color":y,"--pulsing-display":g?`block`:`none`},children:(0,c.jsx)(`div`,{className:s.back,children:(0,c.jsxs)(o,{justify:`right`,className:s.box,children:[(0,c.jsx)(`div`,{className:s.bar,children:(0,c.jsx)(`div`,{})}),(0,c.jsxs)(`div`,{className:s.spikes,children:[(0,c.jsx)(`div`,{className:e(s.spike,s.r)}),(0,c.jsx)(`div`,{className:e(s.spike,s.l)}),(0,c.jsx)(`div`,{className:e(s.spike,s.t)}),(0,c.jsx)(`div`,{className:e(s.spike,s.b)})]})]})})})};export{l as t};