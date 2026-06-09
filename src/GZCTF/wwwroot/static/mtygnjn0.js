/* YINYU CTF Platform @unknown
 *
 * Commit    : Unofficial build version
 * Build     : 2026-06-09T06:10:58.565Z
 */
import{a as e,t}from"./izagbnaw.js";var n=e(t(),1);function r(e){return t=>{if(!t)e(t);else if(typeof t==`function`)e(t);else if(typeof t==`object`&&`nativeEvent`in t){let{currentTarget:n}=t;n.type===`checkbox`?e(n.checked):e(n.value)}else e(t)}}function i(e){let[t,i]=(0,n.useState)(e);return[t,r(i)]}export{i as t};