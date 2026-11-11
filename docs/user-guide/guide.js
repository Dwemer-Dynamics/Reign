/* Dependency-free offline search. Exports only pure ranking helpers for checks. */
(function(root){
  'use strict';
  function normalize(s){return String(s).normalize('NFKD').replace(/[\u0300-\u036f]/g,'').toLowerCase().replace(/[’']/g,'').replace(/[^a-z0-9\\]+/g,' ').trim();}
  const STOP=new Set(['a','an','the','and','to','of','in','is','it','how','do','i','can','my','why','does','what','with','for','cant','cannot','doesnt','isnt','wont','dont','me','you','your','am','are','be','get','would','should','please','need','but']);
  function tokens(q){return [...new Set(normalize(q).split(/\s+/).filter(w=>w&&!STOP.has(w)))];}
  function rank(data,query){
    const phrase=normalize(query), words=tokens(query);
    if(!phrase||!words.length)return [];
    const matches=data.map(t=>{
      const title=normalize(t.title),aliases=(t.aliases||[]).map(normalize),text=normalize(t.text),chapter=normalize(t.chapter);
      let found=0,score=0;
      for(const w of words){let best=title.includes(w)?14:aliases.some(a=>a.includes(w))?11:chapter.includes(w)?4:text.includes(w)?1:0;if(best){found++;score+=best;}}
      if(!found)return null;
      if(title===phrase)score+=100;else if(title.includes(phrase))score+=35;
      if(aliases.includes(phrase))score+=80;else if(aliases.some(a=>a.includes(phrase)))score+=22;
      return {...t,score:score+50*found/words.length,matchedTerms:found,allTerms:found===words.length};
    }).filter(Boolean);
    const exact=matches.filter(t=>t.allTerms);
    return (exact.length?exact:matches.filter(t=>t.matchedTerms>=Math.max(1,Math.ceil(words.length*.6)))).sort((a,b)=>b.score-a.score||a.title.localeCompare(b.title));
  }
  const api={normalize,tokens,rank};
  if(typeof module!=='undefined'&&module.exports)module.exports=api;
  root.ReignGuideSearch=api;
  if(typeof document==='undefined')return;
  const data=JSON.parse(document.getElementById('search-data').textContent);
  const field=document.getElementById('search'),results=document.getElementById('results'),reading=document.getElementById('reading'),list=document.getElementById('result-list'),status=document.getElementById('search-status'),clear=document.getElementById('clear-search'),sidebar=document.getElementById('sidebar'),menu=document.getElementById('menu');
  let beforeSearch=0,active=false;
  function highlight(el,text,query){
    // Use DOM text nodes, never HTML from a query or a stored excerpt.
    const words=tokens(query).filter(w=>w.length>1).sort((a,b)=>b.length-a.length);
    if(!words.length){el.append(document.createTextNode(text));return;}
    const pattern=new RegExp('('+words.map(w=>w.replace(/[.*+?^${}()|[\]\\]/g,'\\$&')).join('|')+')','ig');
    let last=0;for(const m of text.matchAll(pattern)){el.append(document.createTextNode(text.slice(last,m.index)));const mark=document.createElement('mark');mark.textContent=m[0];el.append(mark);last=m.index+m[0].length;}el.append(document.createTextNode(text.slice(last)));
  }
  function snippet(text,q){const ws=tokens(q);let i=text.toLowerCase().indexOf(String(q).toLowerCase());if(i<0)i=ws.reduce((a,w)=>{let p=text.toLowerCase().indexOf(w);return p>=0&&p<a?p:a;},text.length);if(i===text.length)i=0;const start=Math.max(0,i-70);return (start?'…':'')+text.slice(start,start+290)+(start+290<text.length?'…':'');}
  function render(){
    const q=field.value.trim();clear.hidden=!q;
    if(!q){results.hidden=true;reading.hidden=false;document.body.classList.remove('searching');if(active){active=false;window.scrollTo(0,beforeSearch);}return;}
    if(!active){beforeSearch=window.scrollY;active=true;window.scrollTo(0,0);}
    reading.hidden=true;results.hidden=false;document.body.classList.add('searching');list.replaceChildren();
    const matches=rank(data,q);
    status.textContent=matches.length?`${matches[0].allTerms?'':'Closest matches: '}${matches.length} topic${matches.length===1?'':'s'} for “${q}”`:`No topics found for “${q}”. Try fewer words, an everyday synonym, or the A–Z index.`;
    for(const t of matches){
      const li=document.createElement('li'),meta=document.createElement('div'),a=document.createElement('a'),p=document.createElement('p');
      meta.className='result-meta';meta.textContent=t.chapter+(t.preview?' · Preview included':'');a.href='#'+t.id;highlight(a,t.title,q);p.className='result-snippet';highlight(p,snippet(t.text,q),q);li.append(meta,a,p);list.append(li);
    }
  }
  function leaveSearch(restore=true){if(!restore)active=false;field.value='';render();}
  field.addEventListener('input',render);
  clear.addEventListener('click',()=>{leaveSearch();field.focus();});
  document.getElementById('back-to-reading').addEventListener('click',()=>{leaveSearch();field.focus();});
  field.addEventListener('keydown',e=>{if(e.key==='Escape'&&field.value){e.preventDefault();leaveSearch();}if(e.key==='Enter'){const a=list.querySelector('a');if(a){e.preventDefault();a.click();}}});
  document.addEventListener('keydown',e=>{if((e.ctrlKey||e.metaKey)&&e.key.toLowerCase()==='k'){e.preventDefault();field.focus();field.select();}});
  menu.addEventListener('click',()=>{const open=!sidebar.classList.contains('open');sidebar.classList.toggle('open',open);menu.setAttribute('aria-expanded',String(open));});
  document.addEventListener('keydown',e=>{if(e.key==='Escape'&&sidebar.classList.contains('open')){sidebar.classList.remove('open');menu.setAttribute('aria-expanded','false');menu.focus();}});
  const expand=document.getElementById('expand-contents');expand.addEventListener('click',()=>{const open=expand.getAttribute('aria-pressed')!=='true';sidebar.querySelectorAll('details').forEach(d=>d.open=open);expand.setAttribute('aria-pressed',String(open));expand.textContent=open?'Collapse all':'Expand all';});
  document.addEventListener('click',e=>{const a=e.target.closest('a[href^="#"]');if(!a)return;const id=decodeURIComponent(a.getAttribute('href').slice(1)),target=document.getElementById(id);if(!target)return;e.preventDefault();leaveSearch(false);sidebar.classList.remove('open');menu.setAttribute('aria-expanded','false');try{history.pushState(null,'','#'+id);}catch(_){location.hash=id;}target.scrollIntoView({block:'start'});if(target.hasAttribute('tabindex'))target.focus({preventScroll:true});updateCurrent(id);});
  function updateCurrent(id){sidebar.querySelectorAll('a[aria-current]').forEach(a=>a.removeAttribute('aria-current'));const a=Array.from(sidebar.querySelectorAll('a')).find(a=>a.getAttribute('href')==='#'+id);if(a){a.setAttribute('aria-current','location');const d=a.closest('details');if(d)d.open=true;}}
  function goHash(){leaveSearch(false);const id=decodeURIComponent(location.hash.slice(1)),t=document.getElementById(id);if(t){t.scrollIntoView({block:'start'});updateCurrent(id);}}
  window.addEventListener('popstate',goHash);window.addEventListener('hashchange',goHash);
  document.getElementById('print').addEventListener('click',()=>{leaveSearch(false);window.print();});
  document.getElementById('top').addEventListener('click',()=>{leaveSearch(false);document.getElementById('welcome').scrollIntoView();document.getElementById('welcome').focus({preventScroll:true});});
  if(location.hash)requestAnimationFrame(goHash);
})(typeof globalThis!=='undefined'?globalThis:this);
