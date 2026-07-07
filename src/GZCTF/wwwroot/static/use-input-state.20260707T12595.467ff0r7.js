/* YINYU CTF Platform @unknown
 *
 * Commit    : Unofficial build version
 * Build     : 2026-07-07T12:59:55.597Z
 */
import{un as e,xn as t}from"./Api.20260707T12595.cj2ps75j.js";var n=t(e(),1);function r(e){return t=>{if(!t)e(t);else if(typeof t==`function`)e(t);else if(typeof t==`object`&&`nativeEvent`in t){let{currentTarget:n}=t;n.type===`checkbox`?e(n.checked):e(n.value)}else e(t)}}function i(e){let[t,i]=(0,n.useState)(e);return[t,r(i)]}export{i as t};