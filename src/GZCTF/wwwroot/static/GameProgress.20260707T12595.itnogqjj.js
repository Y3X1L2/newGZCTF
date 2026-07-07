/* YINYU CTF Platform @unknown
 *
 * Commit    : Unofficial build version
 * Build     : 2026-07-07T12:59:55.597Z
 */
import{Ft as e,Lt as t,fn as n}from"./Api.20260707T12595.cj2ps75j.js";import{z as r}from"./index.20260707T12595.zkxqluhk.js";var i={back:`S_`,box:`T_`,bar:`U_`,"yy-progress-sheen":`V_`},a=t(),o=t=>{let{thickness:o=4,spikeLength:s=250,percentage:c,color:l,...u}=t,d=e(),f=c<100,p=f?l??`light`:`gray`,m=d.colors[p]??d.colors.teal,h=l?m[5]:`var(--yy-green)`,g=l?m[2]:`rgba(107, 238, 177, 0.2)`,_=Math.max(0,Math.min(100,Number.isFinite(c)?c:0));return(0,a.jsx)(r,{py:0,...u,__vars:{"--thickness":n(o),"--percentage":`${_}%`,"--spike-color":h,"--bg-color":g,"--pulsing-display":f?`block`:`none`},children:(0,a.jsx)(`div`,{className:i.back,children:(0,a.jsx)(`div`,{className:i.box,children:(0,a.jsx)(`div`,{className:i.bar})})})})};export{o as t};